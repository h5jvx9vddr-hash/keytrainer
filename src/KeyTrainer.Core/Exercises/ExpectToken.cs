using System.Text.RegularExpressions;
using KeyTrainer.Core.Input;

namespace KeyTrainer.Core.Exercises;

/// <summary>An expected token from exercises.json: a combo in normal form or a text pattern.</summary>
public sealed partial class ExpectToken
{
    readonly Regex? _textPattern;

    ExpectToken(TokenKind kind, string value, Regex? textPattern)
    {
        Kind = kind;
        Value = value;
        _textPattern = textPattern;
    }

    public TokenKind Kind { get; }

    /// <summary>Normal-form combo, or the text pattern as written.</summary>
    public string Value { get; }

    /// <summary>Parses "Strg + Shift + t" or "text:ping *". Throws <see cref="FormatException"/>.</summary>
    public static ExpectToken Parse(string raw)
    {
        if (raw.StartsWith(InputToken.TextPrefix, StringComparison.Ordinal))
        {
            var pattern = raw[InputToken.TextPrefix.Length..];
            if (pattern.Trim().Length == 0) throw new FormatException($"Leerer Text in \"{raw}\"");
            return new ExpectToken(TokenKind.Text, pattern, BuildPattern(pattern));
        }
        return new ExpectToken(TokenKind.Combo, Combo.Normalize(raw), null);
    }

    /// <summary>
    /// Combos must match exactly. Text matches case-insensitively, with runs of whitespace treated as one
    /// space and "*" matching any characters (commands are case-insensitive on Windows).
    /// </summary>
    public bool Matches(InputToken actual)
    {
        if (actual.Kind != Kind) return false;
        return Kind == TokenKind.Combo
            ? actual.Value == Value
            : _textPattern!.IsMatch(CollapseWhitespace(actual.Value));
    }

    public override string ToString() => Kind == TokenKind.Text ? InputToken.TextPrefix + Value : Value;

    static Regex BuildPattern(string pattern)
    {
        var parts = CollapseWhitespace(pattern).Split('*').Select(Regex.Escape);
        return new Regex("^" + string.Join(".*", parts) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    static string CollapseWhitespace(string s) => Whitespace().Replace(s.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
