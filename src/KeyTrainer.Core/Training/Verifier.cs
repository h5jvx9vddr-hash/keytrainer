using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Windows;

namespace KeyTrainer.Core.Training;

/// <summary>Read-only view of the PC state that result steps are checked against. Implemented per platform.</summary>
public interface IWorld
{
    WindowSnapshot? Foreground();

    /// <summary>Visible top-level windows of all processes with this name.</summary>
    int WindowCount(string process);

    /// <summary>Ids of all running processes with this name. Needs no rights, unlike start times of system services.</summary>
    IReadOnlySet<int> ProcessIds(string name);

    /// <summary>Path with environment variables already expanded.</summary>
    bool PathExists(string path);

    DateTime? LastSessionLock { get; }
}

/// <summary>State a step remembers from its start, e.g. the title to compare against.</summary>
public sealed class StepBaseline
{
    public DateTime Started { get; init; }

    public IReadOnlySet<int> ProcessIds { get; init; } = new HashSet<int>();

    /// <summary>Most windows seen during the step; programs like Notepad restore further windows after the first.</summary>
    public int WindowCount { get; set; }

    /// <summary>Title of the first matching foreground window seen during the step.</summary>
    public string? Title { get; set; }
}

public static class Verifier
{
    public static StepBaseline Begin(Verify? verify, IWorld world, DateTime now)
    {
        var baseline = new StepBaseline
        {
            Started = now,
            ProcessIds = verify?.ProcessRestarted is { } name ? world.ProcessIds(name) : new HashSet<int>(),
            WindowCount = verify?.WindowClosed is { } process ? world.WindowCount(process) : 0,
        };
        // Remember the title right away: a fast key press may change it before the next poll.
        if (verify is { TitleChanged: true } && world.Foreground() is { } window && WindowMatcher.Matches(verify, window))
            baseline.Title = window.Title;
        return baseline;
    }

    /// <summary>True once every condition of <paramref name="verify"/> holds.</summary>
    public static bool IsMet(Verify verify, IWorld world, StepBaseline baseline)
    {
        if (verify.SessionLock && !(world.LastSessionLock is { } locked && locked >= baseline.Started)) return false;
        if (verify.WindowClosed is { } closed)
        {
            int count = world.WindowCount(closed);
            if (count > 0 && count >= baseline.WindowCount)
            {
                baseline.WindowCount = count;
                return false;
            }
        }
        if (verify.ProcessRestarted is { } restarted)
        {
            // A restarted service runs under a new process id.
            var ids = world.ProcessIds(restarted);
            if (ids.Count == 0 || ids.All(baseline.ProcessIds.Contains)) return false;
        }
        if (verify.PathExists is { } exists && !world.PathExists(Environment.ExpandEnvironmentVariables(exists))) return false;
        if (verify.PathMissing is { } missing && world.PathExists(Environment.ExpandEnvironmentVariables(missing))) return false;

        if (!verify.HasWindowCondition) return true;
        var window = world.Foreground();
        if (window is null || !WindowMatcher.Matches(verify, window)) return false;
        if (verify.WindowState is { } state
            && !(Enum.TryParse<WindowPlacement>(state, ignoreCase: true, out var wanted) && wanted == window.Placement))
            return false;
        if (verify.TitleChanged)
        {
            if (baseline.Title is null)
            {
                baseline.Title = window.Title;
                return false;
            }
            return window.Title != baseline.Title;
        }
        return true;
    }
}
