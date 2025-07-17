using System;
using System.Reflection;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using UnityEngine;
using System.Linq;
using Verse.Sound;
using HarmonyLib;

namespace Teleportarium
{
    public class CompCogitator : ThingComp
    {
        private Mote chargingGlowMote;
        private bool poweringUp = false;
        private int powerUpTicks = 0;
        private bool recallPending = false;
        private int recallTicks = 0;
        private List<Thing> recallThings = null;
        private Teleportarium.CompTeleportHomer recallHomer = null;
        private const int RecallDelay = 360; // 6 seconds at 60 ticks/sec
        private const float DefaultRecallPowerDrain = 20000f; // ~2000 watts second
        private Map targetMap;
        private IntVec3 targetCell;
        private const int PowerUpDuration = 360; // 6 seconds at 60 ticks/sec
        private const float DefaultPowerDrain = 20000f; // ~2000 watts second

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            var powerComp = parent.TryGetComp<CompPowerTrader>();
            var mannableComp = parent.TryGetComp<CompMannable>();
            bool isPowered = powerComp != null && powerComp.PowerOn;
            bool isManned = mannableComp != null && mannableComp.MannedNow;
            if (isPowered && isManned && !poweringUp)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Activate Teleportarium",
                    defaultDesc = "Begin teleportation sequence. Select a destination pad, then a map and spot.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower"),
                    action = ShowPlatformSelectionMenu
                };
                yield return new Command_Action
                {
                    defaultLabel = "Recall equipped pawn",
                    defaultDesc = "Recall a pawn with a teleport homer. All pawns and items in a 2x2 area around them will be teleported here.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower"),
                    action = ShowRecallPawnDialog
                };
            }
        }

        // Show a menu to select a destination platform on the same power net
        private void ShowPlatformSelectionMenu()
        {
            var powerComp = parent.TryGetComp<CompPowerTrader>();
            if (powerComp == null || powerComp.PowerNet == null)
            {
                Messages.Message("No power network found!", parent, MessageTypeDefOf.RejectInput);
                return;
            }
            var platforms = powerComp.PowerNet.powerComps
                .Select(pc => pc.parent)
                .OfType<Teleportarium.Building_TeleportariumPlatform>()
                .ToList();
            if (platforms.Count == 0)
            {
                Messages.Message("No teleportarium platforms found on the same power network!", parent, MessageTypeDefOf.RejectInput);
                return;
            }
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (var pad in platforms)
            {
                string label = string.IsNullOrEmpty(pad.customName) ? pad.LabelCap : pad.customName;
                options.Add(new FloatMenuOption(label, () => BeginTargetingWithPad(pad)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // Begin targeting, storing the selected platform for teleport
        private Building_TeleportariumPlatform selectedPad;
        private void BeginTargetingWithPad(Building_TeleportariumPlatform pad)
        {
            selectedPad = pad;
            BeginTargeting();
        }

        // Show a dialog to select a pawn with a teleport homer for recall
        private void ShowRecallPawnDialog()
        {
            var pawnsWithHomer = new List<Pawn>();
            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn.apparel != null)
                    {
                        foreach (var apparel in pawn.apparel.WornApparel)
                        {
                            var homer = apparel.GetComp<Teleportarium.CompTeleportHomer>();
                            if (homer != null && homer.CanRecall)
                            {
                                pawnsWithHomer.Add(pawn);
                            }
                        }
                    }
                }
            }
            if (pawnsWithHomer.Count == 0)
            {
                Messages.Message("No pawns with a charged teleport homer found!", MessageTypeDefOf.RejectInput);
                return;
            }
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (var pawn in pawnsWithHomer)
            {
                var pawnLabel = pawn.LabelShortCap + " (" + pawn.Map.Parent.Label + ")";
                options.Add(new FloatMenuOption(pawnLabel, () => RecallPawn(pawn)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // Recall logic for selected pawn
        private void RecallPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                Log.Error("RecallPawn: pawn is null");
                return;
            }
            if (pawn.apparel == null)
            {
                Log.Error($"RecallPawn: pawn.apparel is null for {pawn}");
                return;
            }
            var homer = pawn.apparel.WornApparel.Select(a => a.GetComp<Teleportarium.CompTeleportHomer>()).FirstOrDefault(c => c != null && c.CanRecall);
            if (homer == null)
            {
                Log.Warning($"RecallPawn: No charged teleport homer found for pawn {pawn}");
                return;
            }
            var map = pawn.Map;
            if (map == null)
            {
                Log.Error($"RecallPawn: pawn.Map is null for recall");
                return;
            }
            // Check for active non-player shields before recall teleporting
            if (HasActiveNonPlayerShields(map))
            {
                Messages.Message("Teleportation recall failed: hostile shields are active!", parent, MessageTypeDefOf.RejectInput);
                return;
            }
            var center = pawn.Position;
            // Determine recall radius based on teleporter pad size
            int recallRadius = 1; // Default radius
            var powerComp = parent.TryGetComp<CompPowerTrader>();
            Thing recallPad = null;
            if (powerComp != null && powerComp.PowerNet != null)
            {
                recallPad = powerComp.PowerNet.powerComps
                    .Select(pc => pc.parent)
                    .FirstOrDefault(t => t.def.defName == "TeleportariumPlatformLarge") ??
                    powerComp.PowerNet.powerComps.Select(pc => pc.parent)
                    .FirstOrDefault(t => t.def.defName == "TeleportariumPlatform");
                if (recallPad != null)
                {
                    // Use a larger radius for large pads
                    if (recallPad.def.defName == "TeleportariumPlatformLarge")
                        recallRadius = 2;
                    else
                        recallRadius = 1;
                }
                else
                {
                    Log.Warning("RecallPawn: No recallPad found on power net.");
                }
            }
            else
            {
                Log.Warning("RecallPawn: powerComp or powerComp.PowerNet is null.");
            }
            var cells = GenRadial.RadialCellsAround(center, recallRadius, true).ToList();
            var thingsToRecall = new List<Thing>();
            foreach (var cell in cells)
            {
                if (!cell.InBounds(map)) continue;
                thingsToRecall.AddRange(map.thingGrid.ThingsListAt(cell).Where(t => t is Pawn || !(t is Building)));
            }
            // Always include the pawn with the teleport homer
            if (!thingsToRecall.Contains(pawn))
            {
                thingsToRecall.Add(pawn);
            }
            if (thingsToRecall.Count == 0)
            {
                Log.Warning("RecallPawn: No pawns or items to recall in radius (should not happen).");
                return;
            }
            // Begin recall powering up phase
            recallPending = true;
            recallTicks = 0;
            recallThings = thingsToRecall;
            recallHomer = homer;
            Messages.Message("Teleportarium recall powering up!", parent, MessageTypeDefOf.PositiveEvent);
        }

        private void BeginTargeting()
        {
            Find.World.renderer.wantedMode = WorldRenderMode.Planet;
            Find.WorldTargeter.BeginTargeting(
                delegate (GlobalTargetInfo target)
                {
                    var map = Find.Maps.FirstOrDefault(m => m.Tile == target.Tile);
                    if (map == null)
                    {
                        Messages.Message("You must have visibility of this map tile before teleporting!", MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    Current.Game.CurrentMap = map;
                    CameraJumper.TryJump(new GlobalTargetInfo(new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), map), CameraJumper.MovementMode.Pan);
                    BeginCellTargeting(map);
                    return true;
                },
                true
            );
        }

        private void BeginCellTargeting(Map destMap)
        {
            Find.Targeter.BeginTargeting(
                new TargetingParameters { canTargetLocations = true, canTargetBuildings = false, canTargetPawns = false },
                target => OnCellTargetSelected(destMap, target),
                null, null, null, null, null, true, null, null
            );
        }

        private void OnCellTargetSelected(Map destMap, LocalTargetInfo target)
        {
            if (destMap != null && target.Cell.IsValid)
            {
                if (parent == null)
                {
                    Log.Error("OnCellTargetSelected: parent is null");
                }
                targetMap = destMap;
                targetCell = target.Cell;
                poweringUp = true;
                powerUpTicks = 0;
                Messages.Message("Teleportarium powering up!", parent, MessageTypeDefOf.PositiveEvent);
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (poweringUp)
            {
                powerUpTicks++;
                CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
                float extraDrain = power != null ? power.Props.PowerConsumption * 1000f : DefaultPowerDrain;
                if (power != null)
                {
                    power.PowerOutput = -extraDrain;
                    // Log current stored energy before failure check
                    if (power.PowerNet != null)
                    {
                        float storedEnergy = power.PowerNet.CurrentStoredEnergy(); // in watt days
                        float requiredWattDays = extraDrain / 60000f;
                        if (storedEnergy < requiredWattDays)
                        {
                            // Cause breakdown in cogitator
                            var breakdownComp = parent.TryGetComp<CompBreakdownable>();
                            breakdownComp?.DoBreakdown();
                            // Cause breakdown and fire in teleport pad
                            if (selectedPad != null)
                            {
                                var padBreakdownComp = selectedPad.TryGetComp<CompBreakdownable>();
                                padBreakdownComp?.DoBreakdown();
                                // Start a fire on the pad
                                FireUtility.TryStartFireIn(selectedPad.Position, selectedPad.Map, 1.0f, null, null);
                            }
                            poweringUp = false;
                            powerUpTicks = 0;
                            power.PowerOutput = -power.Props.PowerConsumption;
                            if (chargingGlowMote != null && !chargingGlowMote.Destroyed)
                                chargingGlowMote.Destroy();
                            chargingGlowMote = null;
                            ReenableSoS2Shields(selectedPad.Map);
                            return;
                        }
                    }
                }
                // Spawn the lightning glow fleck over the teleporter pad every 10 ticks during charging
                if (selectedPad != null && powerUpTicks % 10 == 0)
                {
                    Vector3 padCenter = selectedPad.Position.ToVector3Shifted();
                    Map padMap = selectedPad.Map;
                    if (padMap != null)
                    {
                        FleckMaker.ThrowLightningGlow(padCenter, padMap, 3.5f); // Adjust size as desired
                    }
                }
                if (powerUpTicks == 1)
                {
                    // Play thundercrack sound at start
                    SoundDef sound = SoundDef.Named("teleporter_40k_thundercrack");
                    sound?.PlayOneShot(SoundInfo.InMap(parent));
                    DisableSoS2Shields(selectedPad.Map);
                }
                if (powerUpTicks >= PowerUpDuration)
                {
                    DoTeleport();
                    ReenableSoS2Shields(selectedPad.Map);
                    poweringUp = false;
                    powerUpTicks = 0;
                    if (power != null)
                        power.PowerOutput = -power.Props.PowerConsumption;
                    if (chargingGlowMote != null && !chargingGlowMote.Destroyed)
                        chargingGlowMote.Destroy();
                    chargingGlowMote = null;
                }
            }
            if (recallPending)
            {
                recallTicks++;
                CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
                float extraRecallDrain = power != null ? power.Props.PowerConsumption * 1000f : DefaultRecallPowerDrain;
                if (power != null)
                {
                    power.PowerOutput = -extraRecallDrain;
                    // Log current stored energy before failure check
                    if (power.PowerNet != null)
                    {
                        float storedEnergy = power.PowerNet.CurrentStoredEnergy(); // in watt days
                        float requiredWattDays = extraRecallDrain / 60000f;
                        if (storedEnergy < requiredWattDays)
                        {
                            Messages.Message("Teleportation recall failed: insufficient power! Device damaged.", parent, MessageTypeDefOf.NegativeEvent);
                            // Cause breakdown in cogitator
                            var breakdownCompRecall = parent.TryGetComp<CompBreakdownable>();
                            breakdownCompRecall?.DoBreakdown();
                            // Cause breakdown and fire in teleport pad
                            Thing recallPad = null;
                            var recallPowerComp = parent.TryGetComp<CompPowerTrader>();
                            if (recallPowerComp != null && recallPowerComp.PowerNet != null)
                            {
                                recallPad = recallPowerComp.PowerNet.powerComps
                                    .Select(pc => pc.parent)
                                    .FirstOrDefault(t => t.def.defName == "TeleportariumPlatform" || t.def.defName == "TeleportariumPlatformLarge");
                            }
                            if (recallPad != null)
                            {
                                var padBreakdownComp = recallPad.TryGetComp<CompBreakdownable>();
                                padBreakdownComp?.DoBreakdown();
                                // Start a fire on the pad
                                FireUtility.TryStartFireIn(recallPad.Position, recallPad.Map, 1.0f, null, null);
                            }
                            recallPending = false;
                            recallTicks = 0;
                            recallThings = null;
                            recallHomer = null;
                            power.PowerOutput = -power.Props.PowerConsumption;
                            return;
                        }
                    }
                }
                // Spawn the lightning glow fleck over the teleporter pad every 10 ticks during recall powering up
                var powerCompRecall = parent.TryGetComp<CompPowerTrader>();
                Thing platformRecall = null;
                if (powerCompRecall != null && powerCompRecall.PowerNet != null)
                {
                    platformRecall = powerCompRecall.PowerNet.powerComps
                        .Select(pc => pc.parent)
                        .FirstOrDefault(t => t.def.defName == "TeleportariumPlatform" || t.def.defName == "TeleportariumPlatformLarge");
                }
                if (platformRecall != null && recallTicks % 10 == 0)
                {
                    Vector3 padCenter = platformRecall.Position.ToVector3Shifted();
                    Map padMap = platformRecall.Map;
                    if (padMap != null)
                    {
                        FleckMaker.ThrowLightningGlow(padCenter, padMap, 3.5f);
                    }
                }
                if (recallTicks == 1 && platformRecall != null)
                {
                    // Play thundercrack sound at start of recall
                    SoundDef sound = SoundDef.Named("teleporter_40k_thundercrack");
                    sound?.PlayOneShot(SoundInfo.InMap(platformRecall));
                }
                if (recallTicks >= RecallDelay)
                {
                    // Find linked platform on same power net
                    var powerComp = parent.TryGetComp<CompPowerTrader>();
                    Thing platform = null;
                    if (powerComp != null && powerComp.PowerNet != null)
                    {
                        platform = powerComp.PowerNet.powerComps
                            .Select(pc => pc.parent)
                            .FirstOrDefault(t => t.def.defName == "TeleportariumPlatform" || t.def.defName == "TeleportariumPlatformLarge");
                    }
                    if (platform == null)
                    {
                        Messages.Message("No linked teleportarium platform found on the same power network!", parent, MessageTypeDefOf.RejectInput);
                        recallPending = false;
                        recallTicks = 0;
                        recallThings = null;
                        recallHomer = null;
                        if (power != null)
                            power.PowerOutput = -power.Props.PowerConsumption;
                        return;
                    }
                    var destCells = platform.OccupiedRect().Cells.ToList();
                    int i = 0;
                    foreach (var thing in recallThings)
                    {
                        IntVec3 dest = destCells[i % destCells.Count];
                        if (thing.Spawned)
                            thing.DeSpawn();
                        GenSpawn.Spawn(thing, dest, platform.Map);
                        i++;
                    }
                    recallHomer.ConsumeCharge();
                    // Play base game lightning sound effect (on-map variant for more impact)
                    SoundDef lightningSound = SoundDef.Named("Thunder_OnMap");
                    lightningSound?.PlayOneShot(SoundInfo.InMap(platform));
                    Messages.Message($"Teleport recall complete! ({recallHomer.ChargesLeft} charges left)", platform, MessageTypeDefOf.PositiveEvent);
                    recallPending = false;
                    recallTicks = 0;
                    recallThings = null;
                    recallHomer = null;
                    if (power != null)
                        power.PowerOutput = -power.Props.PowerConsumption;
                }
            }
        }

        private void DoTeleport()
        {
            // Find linked platform on same power net
            var platform = selectedPad;
            if (platform == null)
            {
                Messages.Message("No destination teleportarium platform selected!", parent, MessageTypeDefOf.RejectInput);
                return;
            }
            var platformCells = platform.OccupiedRect().Cells.ToList();
            var things = platformCells
                .SelectMany(c => platform.Map.thingGrid.ThingsListAt(c))
                .Where(t => t != platform && !(t is Building))
                .ToList();

            if (things.Count == 0)
            {
                Messages.Message("No pawns or items to teleport!", platform, MessageTypeDefOf.RejectInput);
                return;
            }
            foreach (var thing in things)
            {
                IntVec3 dest;
                dest = CellFinder.RandomClosewalkCellNear(targetCell, targetMap, 5);
                if (thing.Spawned)
                {
                    thing.DeSpawn();
                }
                GenSpawn.Spawn(thing, dest, targetMap);
            }
            // Play base game lightning sound effect (on-map variant for more impact)
            SoundDef lightningSound = SoundDef.Named("Thunder_OnMap");
            lightningSound?.PlayOneShot(SoundInfo.InMap(platform));
            Messages.Message("Teleportation complete!", platform, MessageTypeDefOf.PositiveEvent);
        }

        // Returns true if there are any active shields not owned by the player on the given map
        public static bool HasActiveNonPlayerShields(Map map)
        {
            if (!ModsConfig.IsActive("kentington.saveourship2"))
                return false;

            var shipMapCompType = AccessTools.TypeByName("SaveOurShip2.ShipMapComp");
            if (shipMapCompType == null) return false;
            var getCompMethod = typeof(Map).GetMethod("GetComponent", new[] { typeof(Type) });
            var shipMapComp = getCompMethod?.Invoke(map, new object[] { shipMapCompType });
            if (shipMapComp == null) return false;
            var shieldsField = shipMapCompType.GetField("Shields", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var shields = shieldsField?.GetValue(shipMapComp) as System.Collections.IEnumerable;
            if (shields == null) return false;
            var compShipHeatShieldType = AccessTools.TypeByName("SaveOurShip2.CompShipHeatShield");
            foreach (var shieldObj in shields)
            {
                if (compShipHeatShieldType == null || !compShipHeatShieldType.IsInstanceOfType(shieldObj)) continue;
                var parentField = compShipHeatShieldType.GetField("parent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var parent = parentField?.GetValue(shieldObj) as Thing;
                if (parent?.Faction == Faction.OfPlayer) continue;
                var flickCompField = compShipHeatShieldType.GetField("flickComp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var flickComp = flickCompField?.GetValue(shieldObj);
                if (flickComp == null) continue;
                var switchIsOnProp = flickComp.GetType().GetProperty("SwitchIsOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (switchIsOnProp != null && (bool)switchIsOnProp.GetValue(flickComp))
                {
                    return true;
                }
            }
            return false;
        }
        
        // Disables all player-owned SoS2 shields on the given map using reflection
        // Usage: DisableSoS2Shields(map);
        public static void DisableSoS2Shields(Map map)
        {
            if (!ModsConfig.IsActive("kentington.saveourship2"))
                return;

            var shipMapCompType = AccessTools.TypeByName("SaveOurShip2.ShipMapComp");
            if (shipMapCompType == null) return;
            var getCompMethod = typeof(Map).GetMethod("GetComponent", new[] { typeof(Type) });
            var shipMapComp = getCompMethod?.Invoke(map, new object[] { shipMapCompType });
            if (shipMapComp == null) { Log.Message("[Teleportarium] ShipMapComp not found for map"); return; }
            var shieldsField = shipMapCompType.GetField("Shields", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var shields = shieldsField?.GetValue(shipMapComp) as System.Collections.IEnumerable;
            if (shields == null) { Log.Message("[Teleportarium] No shields found on ShipMapComp"); return; }
            var compShipHeatShieldType = AccessTools.TypeByName("SaveOurShip2.CompShipHeatShield");
            foreach (var shieldObj in shields)
            {
                if (compShipHeatShieldType == null || !compShipHeatShieldType.IsInstanceOfType(shieldObj)) continue;
                var parentField = compShipHeatShieldType.GetField("parent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var parent = parentField?.GetValue(shieldObj) as Thing;
                if (parent?.Faction != Faction.OfPlayer) continue;
                var flickCompField = compShipHeatShieldType.GetField("flickComp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var flickComp = flickCompField?.GetValue(shieldObj);
                if (flickComp == null) continue;
                var switchIsOnProp = flickComp.GetType().GetProperty("SwitchIsOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (switchIsOnProp != null && (bool)switchIsOnProp.GetValue(flickComp))
                {
                    switchIsOnProp.SetValue(flickComp, false);
                }
            }
        }

        // Re-enables all player-owned SoS2 shields on the given map using reflection
        // Usage: ReenableSoS2Shields(map);
        public static void ReenableSoS2Shields(Map map)
        {
            if (!ModsConfig.IsActive("kentington.saveourship2"))
                return;

            var shipMapCompType = AccessTools.TypeByName("SaveOurShip2.ShipMapComp");
            if (shipMapCompType == null) return;
            var getCompMethod = typeof(Map).GetMethod("GetComponent", new[] { typeof(Type) });
            var shipMapComp = getCompMethod?.Invoke(map, new object[] { shipMapCompType });
            if (shipMapComp == null) { Log.Message("[Teleportarium] ShipMapComp not found for map"); return; }
            var shieldsField = shipMapCompType.GetField("Shields", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var shields = shieldsField?.GetValue(shipMapComp) as System.Collections.IEnumerable;
            if (shields == null) { Log.Message("[Teleportarium] No shields found on ShipMapComp"); return; }
            var compShipHeatShieldType = AccessTools.TypeByName("SaveOurShip2.CompShipHeatShield");
            foreach (var shieldObj in shields)
            {
                if (compShipHeatShieldType == null || !compShipHeatShieldType.IsInstanceOfType(shieldObj)) continue;
                var parentField = compShipHeatShieldType.GetField("parent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var parent = parentField?.GetValue(shieldObj) as Thing;
                if (parent?.Faction != Faction.OfPlayer) continue;
                var flickCompField = compShipHeatShieldType.GetField("flickComp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var flickComp = flickCompField?.GetValue(shieldObj);
                if (flickComp == null) continue;
                var switchIsOnProp = flickComp.GetType().GetProperty("SwitchIsOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (switchIsOnProp != null && !(bool)switchIsOnProp.GetValue(flickComp))
                {
                    switchIsOnProp.SetValue(flickComp, true);
                }
            }
        }
    }
}
