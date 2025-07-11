using Verse;

namespace Teleportarium
{
    public class CompProperties_Teleportarium : CompProperties
    {
        public CompProperties_Teleportarium()
        {
            this.compClass = typeof(CompTeleportarium);
        }

        public float powerConsumption = 1000f;
    }
}
