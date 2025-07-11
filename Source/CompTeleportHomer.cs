using Verse;
using RimWorld;
using System.Collections.Generic;

namespace Teleportarium
{
    public class CompProperties_TeleportHomer : CompProperties
    {
        public int recallCharges = 3;
        public CompProperties_TeleportHomer()
        {
            this.compClass = typeof(CompTeleportHomer);
        }
    }

    public class CompTeleportHomer : ThingComp
    {
        public CompProperties_TeleportHomer Props => (CompProperties_TeleportHomer)props;
        private int chargesLeft = -1;
        private IntVec3 lastTeleportCell = IntVec3.Invalid;
        private Map lastTeleportMap = null;

        public CompRefuelable RefuelableComp => parent.GetComp<CompRefuelable>();

        public int ChargesLeft
        {
            get
            {
                var refuel = RefuelableComp;
                if (refuel != null)
                    return (int)refuel.Fuel;
                return Props.recallCharges;
            }
        }

        public void ConsumeCharge()
        {
            var refuel = RefuelableComp;
            if (refuel != null && refuel.Fuel > 0)
            {
                refuel.ConsumeFuel(1f);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref chargesLeft, "chargesLeft", Props.recallCharges);
            Scribe_References.Look(ref lastTeleportMap, "lastTeleportMap");
            Scribe_Values.Look(ref lastTeleportCell, "lastTeleportCell", IntVec3.Invalid);
        }

        public void SetLastTeleport(Map map, IntVec3 cell)
        {
            lastTeleportMap = map;
            lastTeleportCell = cell;
        }

        public bool CanRecall => chargesLeft != 0 && lastTeleportMap != null && lastTeleportCell.IsValid;

        public bool TryRecall(Pawn pawn)
        {
            if (!CanRecall || pawn == null || pawn.Map != lastTeleportMap)
                return false;
            if (ChargesLeft < 1)
                return false;
            pawn.DeSpawn();
            GenSpawn.Spawn(pawn, lastTeleportCell, lastTeleportMap);
            ConsumeCharge();
            Messages.Message($"{pawn.Name} recalled to Teleportarium! ({ChargesLeft} charges left)", pawn, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Pawn pawn = parent as Pawn;
            if (pawn != null && CanRecall)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Recall to Teleportarium",
                    defaultDesc = $"Teleport instantly to the last Teleportarium platform. Charges left: {chargesLeft}",
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/DesirePower"),
                    action = () => TryRecall(pawn)
                };
            }
        }
    }
}
