using System.Text.Json.Serialization;

namespace KeyTrainer.Core.Exercises;

public enum ExerciseMode
{
    /// <summary>Only the detected key sequence counts.</summary>
    Keys,
    /// <summary>Key sequence plus a matching window afterwards.</summary>
    KeysAndResult,
    /// <summary>Only the result counts, the way there is free (also for elevated windows, where keys are invisible).</summary>
    Result,
    /// <summary>Multi-step task; see <see cref="Exercise.Steps"/>.</summary>
    Scenario,
}

/// <summary>Root of exercises.json.</summary>
public sealed class ExerciseFile
{
    public int Version { get; init; } = 1;

    /// <summary>Named window matches that exercises reference via <c>context</c>.</summary>
    public Dictionary<string, WindowMatch> Contexts { get; init; } = [];

    public List<Exercise> Exercises { get; init; } = [];
}

/// <summary>Common fields of an exercise and a scenario step.</summary>
public class TaskDefinition
{
    public string Prompt { get; init; } = "";

    /// <summary>Hint texts in increasing detail; after them the first key and then the full solution are shown.</summary>
    public List<string> Hints { get; init; } = [];

    /// <summary>Human-readable solution, e.g. "Win + R, services.msc, Enter".</summary>
    public string Solution { get; init; } = "";

    public ExerciseMode Mode { get; init; } = ExerciseMode.Keys;

    public Expect? Expect { get; init; }

    public Verify? Verify { get; init; }

    /// <summary>Name of a context from <see cref="ExerciseFile.Contexts"/> that must be in the foreground.</summary>
    public string? Context { get; init; }

    /// <summary>Explanations for typical wrong combos, keyed by normal form ("Strg+T").</summary>
    public Dictionary<string, string> ExplainWrong { get; init; } = [];
}

public sealed class Exercise : TaskDefinition
{
    public string Id { get; init; } = "";

    public string Category { get; init; } = "";

    public int Level { get; init; } = 1;

    /// <summary>A UAC prompt appears or the target runs elevated; keys inside it are not visible to the trainer.</summary>
    public bool Elevated { get; init; }

    /// <summary>Shortcut or window title not yet confirmed on a real German Windows 11.</summary>
    public bool Unverified { get; init; }

    public Requirements? Requires { get; init; }

    public List<ScenarioStep> Steps { get; init; } = [];
}

public sealed class ScenarioStep : TaskDefinition;

public sealed class Expect
{
    /// <summary>Tokens in order: combos in any spelling ("Strg+Shift+T") or "text:..." with optional "*" wildcards.</summary>
    public List<string> Sequence { get; init; } = [];

    /// <summary>Other accepted sequences (e.g. Shift+F10 or the menu key).</summary>
    public List<List<string>> Alternatives { get; init; } = [];

    [JsonIgnore]
    public IEnumerable<List<string>> AllSequences => Alternatives.Prepend(Sequence);
}

public class WindowMatch
{
    /// <summary>Process name(s), alternatives separated by "|", compared case-insensitively.</summary>
    public string? Process { get; init; }

    public string? WindowClass { get; init; }

    public string? TitleRegex { get; init; }
}

public sealed class Verify : WindowMatch
{
    /// <summary>The practice page sets its title to "KT-OK-&lt;value&gt;" on success.</summary>
    public string? PracticeTitle { get; init; }

    public bool SessionLock { get; init; }

    /// <summary>Process whose start time must change, e.g. "spoolsv.exe" after restarting the spooler.</summary>
    public string? ProcessRestarted { get; init; }

    /// <summary>Name of the UI Automation element that must be selected in the foreground window.</summary>
    public string? UiaSelected { get; init; }

    public int TimeoutMs { get; init; } = 5000;
}

public sealed class Requirements
{
    /// <summary>At least one of these files must exist (environment variables are expanded).</summary>
    public List<string> AnyFile { get; init; } = [];

    /// <summary>Shown when the requirement is missing, e.g. "RSAT installieren".</summary>
    public string? Note { get; init; }
}
