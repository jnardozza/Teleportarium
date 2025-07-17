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

        public CompApparelReloadable ReloadableComp => parent.GetComp<CompApparelReloadable>();

        public int ChargesLeft
        {
            get
            {
                var reload = ReloadableComp;
                if (reload != null)
                    return reload.RemainingCharges;
                return Props.recallCharges;
            }
        }

        public void ConsumeCharge()
        {
            var reload = ReloadableComp;
            if (reload != null && reload.RemainingCharges > 0)
            {
                reload.UsedOnce();
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref chargesLeft, "chargesLeft", Props.recallCharges);
        }

        public bool CanRecall => ChargesLeft > 0;
    }
}
