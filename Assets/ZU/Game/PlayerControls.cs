// The local player's controls (src/client/Input.ts): keyboard + mouse through the player's bindings (Options >
// Controls, ZuSettings.controls) - every action checks the codes bound to it, the hero's own override first, so any
// key, mouse button or wheel notch can drive any action. Aim angles are integrated here (the Overwatch-scale
// sensitivity, per hero, zoomed, invert Y); buttons are latched so a press between two simulation steps is never lost.
using UnityEngine;
using UnityEngine.InputSystem;
using ZU.Game.UI.Toolkit;
using ZU.Sim;

namespace ZU.Game
{
    public class PlayerControls
    {
        /// <summary>radians per mouse count at Sensitivity 15 (the TS Input.sens; Options' Sensitivity multiplies it)</summary>
        public const float BASE_SENS = 0.0022f;
        double yaw, pitch;
        bool jumpLatch, a1Latch, a2Latch, ultLatch, meleeLatch, reloadLatch, swoopLatch;
        string hero = "";

        static ZuSettings S => ZuSettings.Current;
        bool H(string action) => Keys.Held(ZuSettings.BindsFor(S, hero, action));
        bool P(string action) => Keys.Pressed(ZuSettings.BindsFor(S, hero, action));

        public void Begin(Actor me) { yaw = me.yaw; pitch = 0; hero = me.def.id; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        public void Read(Actor me, World w)
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null || me == null) return;
            hero = me.def.id;
            if (kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; return; }
            if (Cursor.lockState != CursorLockMode.Locked) return;
            var C = S.controls;
            var d = mouse.delta.ReadValue();
            float k = BASE_SENS * (float)S.sens * (float)(C.heroSens.TryGetValue(hero, out var hs) ? hs : 1) * (me.Sv("zoom") != 0 ? (float)C.zoomSens : 1);
            // (the sim's yaw turns the other way to Unity's: mouse right = yaw down, as in the TS client)
            yaw -= d.x * k;
            pitch = Mathf.Clamp((float)(pitch + d.y * k * (C.invertY ? -1 : 1)), -1.45f, 1.45f);
            jumpLatch |= P("jump"); a1Latch |= P("a1"); a2Latch |= P("a2"); ultLatch |= P("ult");
            meleeLatch |= P("melee"); reloadLatch |= P("reload"); swoopLatch |= P("swoop");
        }

        /// <summary>write the controls into the actor's input right before a simulation step</summary>
        public void Apply(Actor me, World w)
        {
            if (me == null) return;
            var i = me.input;
            hero = me.def.id;
            bool locked = Cursor.lockState == CursorLockMode.Locked && Keyboard.current != null && Mouse.current != null;
            i.yaw = yaw; i.pitch = pitch;
            if (!locked) { i.mx = i.mz = 0; i.fire = i.alt = false; return; }
            i.mz = (H("forward") ? 1 : 0) - (H("back") ? 1 : 0);
            i.mx = (H("right") ? 1 : 0) - (H("left") ? 1 : 0);
            // (World.Pressed finds the edges: a held key is one press; the latch keeps a tap shorter than a step)
            bool jump = H("jump");
            i.jump = jumpLatch || jump; i.jumpHeld = jump; i.descend = H("crouch");
            i.fire = H("fire"); i.alt = H("alt");
            i.a1 = a1Latch || H("a1"); i.a2 = a2Latch || H("a2"); i.ult = ultLatch || H("ult");
            i.melee = meleeLatch || H("melee"); i.reload = reloadLatch || H("reload"); i.swoop = swoopLatch || H("swoop");
            // Hibiki's Mag-Grind: its own binding (Space by default - jump held; his hero set puts it on the left button)
            i.grind = H("grind");
            jumpLatch = a1Latch = a2Latch = ultLatch = meleeLatch = reloadLatch = swoopLatch = false;
        }
    }
}
