using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Windows;

namespace KeyTrainer.Core.Training;

/// <summary>Read-only view of the PC state that result steps are checked against. Implemented per platform.</summary>
public interface IWorld
{
    WindowSnapshot? Foreground();

    bool IsProcessRunning(string name);

    DateTime? ProcessStartTime(string name);

    /// <summary>Path with environment variables already expanded.</summary>
    bool PathExists(string path);

    DateTime? LastSessionLock { get; }
}

/// <summary>State a step remembers from its start, e.g. the title to compare against.</summary>
public sealed class StepBaseline
{
    public DateTime Started { get; init; }

    public DateTime? ProcessStart { get; init; }

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
            ProcessStart = verify?.ProcessRestarted is { } name ? world.ProcessStartTime(name) : null,
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
        if (verify.ProcessGone is { } gone && world.IsProcessRunning(gone)) return false;
        if (verify.ProcessRestarted is { } restarted)
        {
            var start = world.ProcessStartTime(restarted);
            if (start is null || (baseline.ProcessStart is { } before && start <= before)) return false;
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
