// The settings' input codes (the browser's KeyboardEvent.code names the TS stores: KeyW, ShiftLeft, Mouse0, WheelUp)
// read through the Input System: held / pressed this frame, and "what did the player just press" for rebinding.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ZU.Game.UI.Toolkit
{
    public static class Keys
    {
        static readonly Dictionary<string, Key> NAMED = new Dictionary<string, Key>
        {
            ["Space"] = Key.Space, ["Enter"] = Key.Enter, ["Tab"] = Key.Tab, ["Backquote"] = Key.Backquote, ["Quote"] = Key.Quote, ["Semicolon"] = Key.Semicolon,
            ["Comma"] = Key.Comma, ["Period"] = Key.Period, ["Slash"] = Key.Slash, ["Backslash"] = Key.Backslash, ["BracketLeft"] = Key.LeftBracket, ["BracketRight"] = Key.RightBracket,
            ["Minus"] = Key.Minus, ["Equal"] = Key.Equals, ["Backspace"] = Key.Backspace, ["Escape"] = Key.Escape, ["Delete"] = Key.Delete, ["Insert"] = Key.Insert,
            ["Home"] = Key.Home, ["End"] = Key.End, ["PageUp"] = Key.PageUp, ["PageDown"] = Key.PageDown, ["CapsLock"] = Key.CapsLock,
            ["ShiftLeft"] = Key.LeftShift, ["ShiftRight"] = Key.RightShift, ["ControlLeft"] = Key.LeftCtrl, ["ControlRight"] = Key.RightCtrl, ["AltLeft"] = Key.LeftAlt, ["AltRight"] = Key.RightAlt,
            ["ArrowUp"] = Key.UpArrow, ["ArrowDown"] = Key.DownArrow, ["ArrowLeft"] = Key.LeftArrow, ["ArrowRight"] = Key.RightArrow,
            ["NumpadEnter"] = Key.NumpadEnter, ["NumpadAdd"] = Key.NumpadPlus, ["NumpadSubtract"] = Key.NumpadMinus, ["NumpadMultiply"] = Key.NumpadMultiply, ["NumpadDivide"] = Key.NumpadDivide, ["NumpadDecimal"] = Key.NumpadPeriod,
        };

        /// <summary>the keyboard key for a code (KeyW, Digit1, F8, Numpad4, ...), or Key.None</summary>
        public static Key ToKey(string code)
        {
            if (string.IsNullOrEmpty(code)) return Key.None;
            if (NAMED.TryGetValue(code, out var k)) return k;
            if (code.Length == 4 && code.StartsWith("Key") && char.IsLetter(code[3])) return Key.A + (char.ToUpperInvariant(code[3]) - 'A');
            if (code.Length == 6 && code.StartsWith("Digit") && char.IsDigit(code[5])) return code[5] == '0' ? Key.Digit0 : Key.Digit1 + (code[5] - '1');
            if (code.Length == 7 && code.StartsWith("Numpad") && char.IsDigit(code[6])) return Key.Numpad0 + (code[6] - '0');
            if (code.Length >= 2 && code[0] == 'F' && int.TryParse(code.Substring(1), out int f) && f >= 1 && f <= 12) return Key.F1 + (f - 1);
            return Key.None;
        }

        static ButtonControl Mouse(string code)
        {
            var m = UnityEngine.InputSystem.Mouse.current; if (m == null) return null;
            switch (code)
            {
                case "Mouse0": return m.leftButton; case "Mouse1": return m.middleButton; case "Mouse2": return m.rightButton;
                case "Mouse3": return m.backButton; case "Mouse4": return m.forwardButton;
            }
            return null;
        }

        static ButtonControl Control(string code)
        {
            var b = Mouse(code);
            if (b != null) return b;
            var kb = Keyboard.current; var k = ToKey(code);
            return kb != null && k != Key.None ? kb[k] : null;
        }

        static float Wheel() { var m = UnityEngine.InputSystem.Mouse.current; return m != null ? m.scroll.ReadValue().y : 0; }

        public static bool Held(string code)
        {
            if (code == "WheelUp") return Wheel() > 0; if (code == "WheelDown") return Wheel() < 0;
            var c = Control(code); return c != null && c.isPressed;
        }
        public static bool Pressed(string code)
        {
            if (code == "WheelUp") return Wheel() > 0; if (code == "WheelDown") return Wheel() < 0;
            var c = Control(code); return c != null && c.wasPressedThisFrame;
        }
        public static bool Held(IEnumerable<string> codes) { if (codes != null) foreach (var c in codes) if (Held(c)) return true; return false; }
        public static bool Pressed(IEnumerable<string> codes) { if (codes != null) foreach (var c in codes) if (Pressed(c)) return true; return false; }

        /// <summary>the code of whatever was pressed this frame - any key, mouse button or wheel notch (rebinding)</summary>
        public static string AnyPressed()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m != null)
            {
                foreach (var c in new[] { "Mouse0", "Mouse1", "Mouse2", "Mouse3", "Mouse4" }) if (Mouse(c)?.wasPressedThisFrame == true) return c;
                float w = Wheel(); if (w > 0) return "WheelUp"; if (w < 0) return "WheelDown";
            }
            var kb = Keyboard.current; if (kb == null) return null;
            foreach (var kc in kb.allKeys)
                if (kc != null && kc.wasPressedThisFrame) return CodeOf(kc.keyCode);
            return null;
        }

        /// <summary>Key -> the browser's code name</summary>
        public static string CodeOf(Key k)
        {
            foreach (var kv in NAMED) if (kv.Value == k) return kv.Key;
            if (k >= Key.A && k <= Key.Z) return "Key" + (char)('A' + (k - Key.A));
            if (k >= Key.Digit1 && k <= Key.Digit9) return "Digit" + (char)('1' + (k - Key.Digit1));
            if (k == Key.Digit0) return "Digit0";
            if (k >= Key.Numpad0 && k <= Key.Numpad9) return "Numpad" + (k - Key.Numpad0);
            if (k >= Key.F1 && k <= Key.F12) return "F" + (k - Key.F1 + 1);
            return k.ToString();
        }
    }
}
