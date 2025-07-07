using RimWorld;
using Verse;
using System.Collections.Generic;
using UnityEngine;

namespace Teleportarium
{
    public class Building_TeleportariumPlatform : Building
    {
        public CompTeleportarium TeleportariumComp => this.GetComp<CompTeleportarium>();

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
                yield return g;
            if (TeleportariumComp != null)
            {
                foreach (var g in TeleportariumComp.CompGetGizmosExtra())
                    yield return g;
            }
        }
    }
}
