using Verse;
using UnityEngine;
namespace Teleportarium
{
    public class Dialog_RenamePad : Window
    {
        private Building_TeleportariumPlatform pad;
        private string nameBuffer;
        public override Vector2 InitialSize => new Vector2(320f, 150f);
        public Dialog_RenamePad(Building_TeleportariumPlatform pad)
        {
            this.pad = pad;
            this.nameBuffer = pad.customName ?? pad.LabelCap;
            forcePause = true;
            absorbInputAroundWindow = true;
        }
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "Enter new teleporter pad name:");
            nameBuffer = Widgets.TextField(new Rect(0f, 40f, inRect.width, 30f), nameBuffer);
            if (Widgets.ButtonText(new Rect(0f, 80f, inRect.width, 30f), "OK"))
            {
                pad.customName = nameBuffer.Trim();
                Close();
            }
        }
    }
}
