using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Teleportarium
{
    public class Building_TeleportariumCogitator : Building
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
                yield return gizmo;
            var comp = this.GetComp<CompCogitator>();
            if (comp != null)
            {
                foreach (var gizmo in comp.CompGetGizmosExtra())
                    yield return gizmo;
            }
        }
    }
}
