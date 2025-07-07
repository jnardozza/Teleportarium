using System.Collections.Generic;
using RimWorld;
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
        }

        private void BeginTargeting()
        {
            Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, canTargetBuildings = false, canTargetPawns = false }, OnTargetSelected, null, null, null, null, null, delegate (GlobalTargetInfo target)
            {
                return target.Map != null;
            });
        }

        private void OnTargetSelected(LocalTargetInfo target)
        {
            if (parent.Map != null && target.Cell.IsValid)
            {
                targetMap = parent.Map;
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
            var pawns = platformCells.SelectMany(c => parent.Map.thingGrid.ThingsListAt(c)).OfType<Pawn>().ToList();
            if (pawns.Count == 0)
            {
                Messages.Message("No pawns to teleport!", parent, MessageTypeDefOf.RejectInput);
                return;
            }
            foreach (var pawn in pawns)
            {
                IntVec3 dest = CellFinder.RandomClosewalkCellNear(targetCell, targetMap, 5);
                if (pawn.Spawned)
                {
                    pawn.DeSpawn();
                }
                GenSpawn.Spawn(pawn, dest, targetMap);
            }
            Messages.Message("Teleportarium complete!", parent, MessageTypeDefOf.PositiveEvent);
        }
    }
}
