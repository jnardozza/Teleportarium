using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using UnityEngine;
using System.Linq;

namespace Teleportarium
{
    public class CompTeleportarium : ThingComp
    {
        public CompProperties_Teleportarium Props => (CompProperties_Teleportarium)props;
        private bool poweringUp = false;
        private int powerUpTicks = 0;
        private Map targetMap;
        private IntVec3 targetCell;
        private const int PowerUpDuration = 600; // 10 seconds at 60 ticks/sec
        private const float PowerDrain = 10000f; // Massive power drain

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!poweringUp)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Activate Teleportarium",
                    defaultDesc = "Begin teleportation sequence. Select a map, then a spot.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower"),
                    action = BeginTargeting
                };
            }
            // Recall equipped pawn gizmo
            yield return new Command_Action
            {
                defaultLabel = "Recall equipped pawn",
                defaultDesc = "Recall a pawn with a teleport homer. All pawns and items in a 2x2 area around them will be teleported here.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower"),
                action = BeginRecallTargeting
            };
        }

        // Target a pawn with a teleport homer for recall
        private void BeginRecallTargeting()
        {
            Find.Targeter.BeginTargeting(
                new TargetingParameters {
                    canTargetPawns = true,
                    canTargetBuildings = false,
                    canTargetLocations = false,
                    validator = target =>
                    {
                        Pawn pawn = target.Thing as Pawn;
                        if (pawn == null || pawn.apparel == null)
                            return false;
                        return pawn.apparel.WornApparel.Any(a => a.GetComp<Teleportarium.CompTeleportHomer>() != null && a.GetComp<Teleportarium.CompTeleportHomer>().CanRecall);
                    }
                },
                OnRecallPawnSelected,
                null, null, null, null, null, true, null, null
            );
        }

        // Called when a pawn with a teleport homer is selected
        private void OnRecallPawnSelected(LocalTargetInfo target)
        {
            Pawn pawn = target.Thing as Pawn;
            if (pawn == null || pawn.apparel == null)
            {
                Messages.Message("Invalid target for recall.", MessageTypeDefOf.RejectInput);
                return;
            }
            var homer = pawn.apparel.WornApparel.Select(a => a.GetComp<Teleportarium.CompTeleportHomer>()).FirstOrDefault(c => c != null && c.CanRecall);
            if (homer == null)
            {
                Messages.Message("Targeted pawn does not have a charged teleport homer.", MessageTypeDefOf.RejectInput);
                return;
            }
            // Find all pawns and items in a 2x2 area around the pawn
            var map = pawn.Map;
            var center = pawn.Position;
            var cells = GenRadial.RadialCellsAround(center, 1, true).Take(4).ToList();
            var thingsToRecall = new List<Thing>();
            foreach (var cell in cells)
            {
                if (!cell.InBounds(map)) continue;
                thingsToRecall.AddRange(map.thingGrid.ThingsListAt(cell).Where(t => t is Pawn || !(t is Building)));
            }
            if (thingsToRecall.Count == 0)
            {
                Messages.Message("No pawns or items to recall!", MessageTypeDefOf.RejectInput);
                return;
            }
            // Teleport all to this platform
            var destCells = parent.OccupiedRect().Cells.ToList();
            int i = 0;
            foreach (var thing in thingsToRecall)
            {
                IntVec3 dest = destCells[i % destCells.Count];
                if (thing.Spawned)
                    thing.DeSpawn();
                GenSpawn.Spawn(thing, dest, parent.Map);
                i++;
            }
            homer.ConsumeCharge();
            Messages.Message($"Teleport recall complete! ({homer.ChargesLeft} charges left)", parent, MessageTypeDefOf.PositiveEvent);
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
                        Messages.Message("No map found for selected world tile!", MessageTypeDefOf.RejectInput);
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
                if (power != null)
                {
                    power.PowerOutput = -PowerDrain;
                }
                if (powerUpTicks >= PowerUpDuration)
                {
                    DoTeleport();
                    poweringUp = false;
                    powerUpTicks = 0;
                    if (power != null)
                        power.PowerOutput = -Props.powerConsumption;
                }
            }
        }

        private void DoTeleport()
        {
            var platformCells = parent.OccupiedRect().Cells.ToList();
            var things = platformCells
                .SelectMany(c => parent.Map.thingGrid.ThingsListAt(c))
                .Where(t => t != parent && !(t is Building))
                .ToList();

            if (things.Count == 0)
            {
                Messages.Message("No pawns or items to teleport!", parent, MessageTypeDefOf.RejectInput);
                return;
            }

            foreach (var thing in things)
            {
                IntVec3 dest = CellFinder.RandomClosewalkCellNear(targetCell, targetMap, 5);
                if (thing.Spawned)
                {
                    thing.DeSpawn();
                }
                GenSpawn.Spawn(thing, dest, targetMap);
            }
            Messages.Message("Teleportarium complete!", parent, MessageTypeDefOf.PositiveEvent);
        }
    }
}
