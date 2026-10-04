using System;
using System.Collections.Generic;
using System.Linq;

namespace Cutlify.Hotkeys
{
    [Flags]
    public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

    /// <summary>Combinación de teclas global, p. ej. "Win+Shift+S".</summary>
    public readonly struct Hotkey : IEquatable<Hotkey>
    {
        public HotkeyModifiers Modifiers { get; }
        public int Key { get; } // código de tecla virtual

        public Hotkey(HotkeyModifiers mods, int key) { Modifiers = mods; Key = key; }

        public bool IsValid => Key != 0 && Modifiers != HotkeyModifiers.None;

        private static readonly Dictionary<string, int> Named = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Esc"] = 0x1B, ["PrintScreen"] = 0x2C, ["Pause"] = 0x13,
            ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        };

        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var mods = HotkeyModifiers.None;
            int key = 0;
            foreach (var raw in text.Split('+').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= HotkeyModifiers.Ctrl; continue;
                    case "shift": case "mayús": case "mayus": mods |= HotkeyModifiers.Shift; continue;
                    case "alt": mods |= HotkeyModifiers.Alt; continue;
                    case "win": case "windows": mods |= HotkeyModifiers.Win; continue;
                }
                if (key != 0) return false;
                key = ParseKey(raw);
                if (key == 0) return false;
            }
            hotkey = new Hotkey(mods, key);
            return hotkey.IsValid;
        }

        private static int ParseKey(string k)
        {
            if (Named.TryGetValue(k, out var v)) return v;
            if (k.Length == 1)
            {
                char c = char.ToUpperInvariant(k[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
            }
            if (k.Length >= 2 && (k[0] == 'F' || k[0] == 'f') && int.TryParse(k.Substring(1), out int f) && f >= 1 && f <= 24)
                return 0x70 + f - 1;
            return 0;
        }

        public static string KeyName(int vk)
        {
            foreach (var kv in Named) if (kv.Value == vk) return kv.Key;
            if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
            return null;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
            if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            parts.Add(KeyName(Key) ?? $"0x{Key:X2}");
            return string.Join("+", parts);
        }

        public bool Equals(Hotkey other) => Modifiers == other.Modifiers && Key == other.Key;
        public override bool Equals(object obj) => obj is Hotkey h && Equals(h);
        public override int GetHashCode() => ((int)Modifiers << 16) ^ Key;
    }
}
