using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using KeyTrainer.Core.Exercises;

namespace KeyTrainer.Core.Windows;

/// <summary>What the trainer knows about the foreground window.</summary>
/// <param name="Process">Executable file name, e.g. "mmc.exe".</param>
public sealed record WindowSnapshot(string Process, string WindowClass, string Title);

public static class WindowMatcher
{
    static readonly ConcurrentDictionary<string, Regex> Cache = new();

    /// <summary>True if every field set in <paramref name="match"/> fits the window.</summary>
    public static bool Matches(WindowMatch match, WindowSnapshot window)
    {
        if (match.Process is { } process
            && !process.Split('|').Any(p => string.Equals(p.Trim(), window.Process, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (match.WindowClass is { } cls && !string.Equals(cls, window.WindowClass, StringComparison.Ordinal))
            return false;
        if (match.TitleRegex is { } pattern)
        {
            var regex = Cache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
            if (!regex.IsMatch(window.Title)) return false;
        }
        return true;
    }
}
