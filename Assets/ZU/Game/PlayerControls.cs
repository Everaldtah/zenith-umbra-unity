// The local player's controls (the TS client's Input.ts bindings): WASD move, mouse look, SPACE jump / fly, CTRL
// descend, LMB fire, RMB secondary, SHIFT / E abilities, Q ultimate, R reload, C melee, F swoop / grind.
// Mouse aim is integrated every frame; buttons are latched so a press between two simulation steps is never lost.
using UnityEngine;
using UnityEngine.InputSystem;
using ZU.Sim;

namespace ZU.Game
{
    public class PlayerControls
    {
        public float sensitivity = 0.0022f;      // radians per mouse count (the TS default)
        double yaw, pitch;
        bool jumpLatch, a1Latch, a2Latch, ultLatch, meleeLatch, reloadLatch, swoopLatch;

        public void Begin(Actor me) { yaw = me.yaw; pitch = 0; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        public void Read(Actor me, World w)
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null || me == null) return;
            if (kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var d = mouse.delta.ReadValue();
                // (the sim's yaw turns the other way to Unity's: mouse right = yaw down, as in the TS client)
                yaw -= d.x * sensitivity;
                pitch = Mathf.Clamp((float)(pitch + d.y * sensitivity), -1.45f, 1.45f);
            }
            jumpLatch |= kb.spaceKey.wasPressedThisFrame;
            a1Latch |= kb.leftShiftKey.wasPressedThisFrame;
            a2Latch |= kb.eKey.wasPressedThisFrame;
            ultLatch |= kb.qKey.wasPressedThisFrame;
            meleeLatch |= kb.cKey.wasPressedThisFrame || kb.vKey.wasPressedThisFrame;
            reloadLatch |= kb.rKey.wasPressedThisFrame;
            swoopLatch |= kb.fKey.wasPressedThisFrame;
        }

        /// <summary>write the controls into the actor's input right before a simulation step</summary>
        public void Apply(Actor me, World w)
        {
            if (me == null) return;
            var kb = Keyboard.current; var mouse = Mouse.current; var i = me.input;
            bool locked = Cursor.lockState == CursorLockMode.Locked && kb != null && mouse != null;
            i.yaw = yaw; i.pitch = pitch;
            if (!locked) { i.mx = i.mz = 0; i.fire = i.alt = false; return; }
            i.mz = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
            i.mx = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
            // (World.Pressed finds the edges: a held key is one press; the latch keeps a tap shorter than a step)
            i.jump = jumpLatch || kb.spaceKey.isPressed; i.jumpHeld = kb.spaceKey.isPressed;
            i.descend = kb.leftCtrlKey.isPressed;
            i.fire = mouse.leftButton.isPressed; i.alt = mouse.rightButton.isPressed;
            i.a1 = a1Latch || kb.leftShiftKey.isPressed; i.a2 = a2Latch || kb.eKey.isPressed; i.ult = ultLatch || kb.qKey.isPressed;
            i.melee = meleeLatch; i.reload = reloadLatch; i.swoop = swoopLatch || kb.fKey.isPressed;
            i.grind = null;            // (Hibiki grinds on jump held, the TS default when no grind binding is set)
            jumpLatch = a1Latch = a2Latch = ultLatch = meleeLatch = reloadLatch = swoopLatch = false;
        }
    }
}
