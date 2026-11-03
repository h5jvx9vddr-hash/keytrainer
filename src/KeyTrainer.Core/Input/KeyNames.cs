namespace KeyTrainer.Core.Input;

/// <summary>German display names for keys, as used in the normal form ("Strg+Shift+T").</summary>
public static class KeyNames
{
    // Names for a German (QWERTZ) layout; OEM keys follow the printed key cap.
    static readonly Dictionary<int, string> Names = new()
    {
        [Vk.Back] = "Rücktaste",
        [Vk.Tab] = "Tab",
        [Vk.Return] = "Enter",
        [Vk.Pause] = "Pause",
        [Vk.Capital] = "Feststell",
        [Vk.Escape] = "Esc",
        [Vk.Space] = "Leertaste",
        [Vk.Prior] = "Bild auf",
        [Vk.Next] = "Bild ab",
        [Vk.End] = "Ende",
        [Vk.Home] = "Pos1",
        [Vk.Left] = "Pfeil links",
        [Vk.Up] = "Pfeil hoch",
        [Vk.Right] = "Pfeil rechts",
        [Vk.Down] = "Pfeil runter",
        [Vk.Snapshot] = "Druck",
        [Vk.Insert] = "Einfg",
        [Vk.Delete] = "Entf",
        [Vk.Apps] = "Menü",
        [Vk.Multiply] = "Num *",
        [Vk.Add] = "Num +",
        [Vk.Subtract] = "Num -",
        [Vk.Decimal] = "Num ,",
        [Vk.Divide] = "Num /",
        [Vk.NumLock] = "Num",
        [Vk.Scroll] = "Rollen",
        [0xBA] = "Ü",
        [Vk.OemPlus] = "Plus",
        [Vk.OemComma] = "Komma",
        [Vk.OemMinus] = "Minus",
        [Vk.OemPeriod] = "Punkt",
        [0xBF] = "#",
        [0xC0] = "Ö",
        [0xDB] = "ß",
        [0xDC] = "^",
        [0xDD] = "´",
        [0xDE] = "Ä",
        [0xE2] = "<",
    };

    // Alternative spellings accepted when parsing exercise files.
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Return"] = "Enter",
        ["Eingabe"] = "Enter",
        ["Escape"] = "Esc",
        ["Backspace"] = "Rücktaste",
        ["Space"] = "Leertaste",
        ["Leer"] = "Leertaste",
        ["Del"] = "Entf",
        ["Delete"] = "Entf",
        ["Ins"] = "Einfg",
        ["Insert"] = "Einfg",
        ["Home"] = "Pos1",
        ["End"] = "Ende",
        ["PgUp"] = "Bild auf",
        ["PageUp"] = "Bild auf",
        ["PgDn"] = "Bild ab",
        ["PageDown"] = "Bild ab",
        ["Left"] = "Pfeil links",
        ["Links"] = "Pfeil links",
        ["Right"] = "Pfeil rechts",
        ["Rechts"] = "Pfeil rechts",
        ["Up"] = "Pfeil hoch",
        ["Hoch"] = "Pfeil hoch",
        ["Pfeil oben"] = "Pfeil hoch",
        ["Down"] = "Pfeil runter",
        ["Runter"] = "Pfeil runter",
        ["Pfeil unten"] = "Pfeil runter",
        ["PrintScreen"] = "Druck",
        ["+"] = "Plus",
        ["-"] = "Minus",
        [","] = "Komma",
        ["."] = "Punkt",
    };

    static readonly Dictionary<string, string> Canonical = BuildCanonical();

    public static string Name(int vk)
    {
        if (Names.TryGetValue(vk, out var name)) return name;
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= Vk.NumPad0 and <= Vk.NumPad0 + 9) return $"Num {vk - Vk.NumPad0}";
        if (vk is >= Vk.F1 and <= Vk.F24) return $"F{vk - Vk.F1 + 1}";
        return $"VK{vk:X2}";
    }

    /// <summary>Maps any accepted spelling to the canonical key name, or null if unknown.</summary>
    public static string? Canonicalize(string key)
    {
        key = key.Trim();
        if (Aliases.TryGetValue(key, out var alias)) return alias;
        return Canonical.TryGetValue(key, out var canonical) ? canonical : null;
    }

    static Dictionary<string, string> BuildCanonical()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int vk = 1; vk < 0xFF; vk++)
        {
            var name = Name(vk);
            if (!name.StartsWith("VK", StringComparison.Ordinal)) map.TryAdd(name, name);
        }
        return map;
    }
}
