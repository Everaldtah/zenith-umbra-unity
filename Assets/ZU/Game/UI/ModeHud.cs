// What a mode adds to the match's controls: between Stadium rounds the player shops in the Armory with the cursor free
// and their hero's controls off. The screens are UI Toolkit - the Armory (Toolkit/ArmoryView.cs, the TS Armory.ts) and
// the Stadium / campaign objective bar in the HUD (Toolkit/HudView.cs).
using UnityEngine;

namespace ZU.Game.UI
{
    public static class ModeHud
    {
        /// <summary>the player is shopping in the Armory (controls off, cursor free)</summary>
        public static bool Shopping(MatchRunner r) => r.World?.stadium != null && r.Player != null && r.World.stadium.phase == "armory" && !r.World.stadium.ready.Contains(r.Player.id);

        public static void Update(MatchRunner r)
        {
            if (Shopping(r) && !PauseMenu.Paused) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
    }
}
