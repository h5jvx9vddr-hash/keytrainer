using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using KeyTrainer.Core.Exercises;

namespace KeyTrainer.Core.Windows;

public enum WindowPlacement
{
    Normal,
    Maximized,
    LeftHalf,
    RightHalf,
}

/// <summary>What the trainer knows about the foreground window.</summary>
/// <param name="Process">Executable file name, e.g. "mmc.exe".</param>
public sealed record WindowSnapshot(string Process, string WindowClass, string Title, WindowPlacement Placement = WindowPlacement.Normal);

public static class WindowMatcher
{
    static readonly ConcurrentDictionary<string, Regex> Cache = new();

    /// <summary>True if every field set in <paramref name="match"/> fits the window.</summary>
    public static bool Matches(WindowMatch match, WindowSnapshot window)
    {
        if (match.Process is { } process && !AnyOf(process, window.Process, StringComparison.OrdinalIgnoreCase))
            return false;
        if (match.WindowClass is { } cls && !AnyOf(cls, window.WindowClass, StringComparison.Ordinal))
            return false;
        if (match.TitleRegex is { } pattern)
        {
            var regex = Cache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
            if (!regex.IsMatch(window.Title)) return false;
        }
        return true;
    }

    static bool AnyOf(string alternatives, string value, StringComparison comparison) =>
        alternatives.Split('|').Any(a => string.Equals(a.Trim(), value, comparison));
}
