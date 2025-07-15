using RimWorld;
using Verse;
using System.Collections.Generic;
using UnityEngine;

namespace Teleportarium
{
    public class Building_TeleportariumPlatform : Building
    {
        public string customName = null;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (string.IsNullOrEmpty(customName))
            {
                int count = map.listerBuildings.AllBuildingsColonistOfDef(this.def).Count;
                customName = $"Teleportarium Platform {count}";
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
                yield return g;
            yield return new Command_Action
            {
                defaultLabel = "Rename Teleporter Pad",
                defaultDesc = "Set a custom name for this teleporter pad.",
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Rename"),
                action = () => Find.WindowStack.Add(new Dialog_RenamePad(this))
            };
        }
    }
}
