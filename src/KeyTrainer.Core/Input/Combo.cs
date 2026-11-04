using System.Text.RegularExpressions;

namespace KeyTrainer.Core.Input;

[Flags]
public enum Mods
{
    None = 0,
    Win = 1,
    Ctrl = 2,
    Alt = 4,
    Shift = 8,
}

/// <summary>
/// A key combination in normal form: modifiers in the order Win, Strg, Alt, Shift,
/// followed by the key, joined with "+" (e.g. "Win+Strg+D", "Strg+Shift+T").
/// A combo with an empty key is a modifier pressed on its own (e.g. "Win").
/// </summary>
public readonly partial record struct Combo(Mods Mods, string Key)
{
    static readonly (Mods Mod, string Name)[] Order =
    [
        (Mods.Win, "Win"),
        (Mods.Ctrl, "Strg"),
        (Mods.Alt, "Alt"),
        (Mods.Shift, "Shift"),
    ];

    static readonly Dictionary<string, Mods> ModAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Win"] = Mods.Win,
        ["Windows"] = Mods.Win,
        ["Meta"] = Mods.Win,
        ["Strg"] = Mods.Ctrl,
        ["Ctrl"] = Mods.Ctrl,
        ["Control"] = Mods.Ctrl,
        ["Alt"] = Mods.Alt,
        ["Shift"] = Mods.Shift,
        ["Umschalt"] = Mods.Shift,
    };

    /// <summary>Key names in display order, e.g. ["Win", "Strg", "D"].</summary>
    public IReadOnlyList<string> Names()
    {
        var parts = new List<string>(5);
        foreach (var (mod, name) in Order)
            if (Mods.HasFlag(mod)) parts.Add(name);
        if (Key.Length > 0) parts.Add(Key);
        return parts;
    }

    public override string ToString() => string.Join("+", Names());

    /// <summary>Parses any accepted spelling, e.g. "shift + ctrl + t" or "Alt+Shift++".</summary>
    public static Combo Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Leere Tastenkombination.");

        // Key names may contain "+" themselves ("Num +").
        if (KeyNames.Canonicalize(text) is { } single) return new Combo(Mods.None, single);

        // "Alt + Shift + +" -> "Alt+Shift++"; spaces inside key names ("Pfeil links") stay.
        var trimmed = SpacesAroundPlus().Replace(text.Trim(), "+");

        var parts = new List<string>();
        // A trailing "++" means the plus key itself.
        if (trimmed.EndsWith("++", StringComparison.Ordinal))
        {
            parts.AddRange(trimmed[..^2].Split('+'));
            parts.Add("+");
        }
        else
        {
            parts.AddRange(trimmed.Split('+'));
        }

        var mods = Mods.None;
        string key = "";
        foreach (var raw in parts)
        {
            var part = raw.Trim();
            if (part.Length == 0) throw new FormatException($"Ungültige Tastenkombination: \"{text}\"");
            if (ModAliases.TryGetValue(part, out var mod))
            {
                mods |= mod;
                continue;
            }
            if (key.Length > 0) throw new FormatException($"Mehr als eine Haupttaste in \"{text}\"");
            key = KeyNames.Canonicalize(part) ?? throw new FormatException($"Unbekannte Taste \"{part}\" in \"{text}\"");
        }
        return new Combo(mods, key);
    }

    public static string Normalize(string text) => Parse(text).ToString();

    [GeneratedRegex(@"\s*\+\s*")]
    private static partial Regex SpacesAroundPlus();
}
