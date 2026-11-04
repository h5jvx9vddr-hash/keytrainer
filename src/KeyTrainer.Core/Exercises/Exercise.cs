using System.Text.Json.Serialization;

namespace KeyTrainer.Core.Exercises;

public enum ExerciseMode
{
    /// <summary>A key sequence counts; for things that leave no visible trace (text editing, virtual desktops).</summary>
    Keys,
    /// <summary>The goal counts, any keyboard way is fine (window open, folder created, PC locked …).</summary>
    Result,
    /// <summary>Several steps in a row; see <see cref="Exercise.Steps"/>.</summary>
    Scenario,
}

/// <summary>Root of exercises.json.</summary>
public sealed class ExerciseFile
{
    public int Version { get; init; } = 2;

    /// <summary>Named window matches that exercises reference via <c>context</c>.</summary>
    public Dictionary<string, WindowMatch> Contexts { get; init; } = [];

    /// <summary>Categories that are not part of the default "ohne Maus" training (command line, Office …).</summary>
    public List<string> ExtraCategories { get; init; } = [];

    /// <summary>Software some exercises need, keyed by id; referenced from <see cref="Exercise.Requires"/>.</summary>
    public Dictionary<string, Software> Software { get; init; } = [];

    public List<Exercise> Exercises { get; init; } = [];
}

/// <summary>Common fields of an exercise and a scenario step.</summary>
public class TaskDefinition
{
    public string Prompt { get; init; } = "";

    /// <summary>Tips in increasing detail; after them the trainer shows <see cref="Solution"/> as fastest way.</summary>
    public List<string> Hints { get; init; } = [];

    /// <summary>Fastest keyboard way with a concrete example, e.g. "Win + R, services.msc, Enter".</summary>
    public string Solution { get; init; } = "";

    public ExerciseMode Mode { get; init; } = ExerciseMode.Result;

    public Expect? Expect { get; init; }

    public Verify? Verify { get; init; }

    /// <summary>Keys steps only: name of a context from <see cref="ExerciseFile.Contexts"/> that must be in front.</summary>
    public string? Context { get; init; }

    /// <summary>Explanations for typical mix-ups, keyed by combo ("Strg+T").</summary>
    public Dictionary<string, string> ExplainWrong { get; init; } = [];

    /// <summary>
    /// For the automatic end-to-end test only: what to press to solve this step ("Win+R", "text:services.msc",
    /// "Enter", "wait:800"). Empty = the test cannot run this step on its own.
    /// </summary>
    public List<string> Auto { get; init; } = [];
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

    /// <summary>The end-to-end test must not run it (e.g. it would cut the network).</summary>
    public bool NoAuto { get; init; }

    /// <summary>Id of an entry in <see cref="ExerciseFile.Software"/>.</summary>
    public string? Requires { get; init; }

    public List<ScenarioStep> Steps { get; init; } = [];

    /// <summary>The steps to work through; a plain exercise is its own single step.</summary>
    [JsonIgnore]
    public IReadOnlyList<TaskDefinition> AllSteps => Mode == ExerciseMode.Scenario ? Steps : [this];
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
    /// <summary>Display name for messages like "Wechsle zuerst in den Explorer".</summary>
    public string? Label { get; init; }

    /// <summary>Process name(s), alternatives separated by "|", compared case-insensitively.</summary>
    public string? Process { get; init; }

    /// <summary>Window class(es), alternatives separated by "|".</summary>
    public string? WindowClass { get; init; }

    public string? TitleRegex { get; init; }
}

/// <summary>Goal of a result step. Every condition that is set must hold at the same time.</summary>
public sealed class Verify : WindowMatch
{
    /// <summary>Foreground window must be "maximized", "leftHalf" or "rightHalf".</summary>
    public string? WindowState { get; init; }

    /// <summary>Foreground window (matching the filters above) must show a different title than when the step began.</summary>
    public bool TitleChanged { get; init; }

    public bool SessionLock { get; init; }

    /// <summary>No process with this name may be running.</summary>
    public string? ProcessGone { get; init; }

    /// <summary>Process whose start time must change, e.g. "spoolsv.exe" after restarting the spooler.</summary>
    public string? ProcessRestarted { get; init; }

    /// <summary>File or folder that must exist (environment variables are expanded).</summary>
    public string? PathExists { get; init; }

    /// <summary>File or folder that must no longer exist.</summary>
    public string? PathMissing { get; init; }

    /// <summary>The practice page sets its title to "KT-OK-&lt;value&gt;" on success (phase 5).</summary>
    public string? PracticeTitle { get; init; }

    [JsonIgnore]
    public bool HasWindowCondition =>
        Process is not null || WindowClass is not null || TitleRegex is not null || WindowState is not null || TitleChanged;
}

/// <summary>A program or Windows feature some exercises need, and how to get it.</summary>
public sealed class Software
{
    public string Name { get; init; } = "";

    /// <summary>Installed if at least one of these files or folders exists (environment variables are expanded).</summary>
    public List<string> AnyFile { get; init; } = [];

    /// <summary>winget package id, e.g. "Microsoft.Teams".</summary>
    public string? Winget { get; init; }

    /// <summary>Windows capability for DISM, e.g. "Rsat.Dns.Tools~~~~0.0.1.0" (needs admin rights).</summary>
    public string? Capability { get; init; }

    /// <summary>Web page to install from (browser extensions).</summary>
    public string? Url { get; init; }

    /// <summary>Shown when it cannot be installed from here, e.g. "Nur in Windows Pro".</summary>
    public string? Note { get; init; }

    public bool IsInstalled(Func<string, bool> exists) =>
        AnyFile.Any(f => exists(Environment.ExpandEnvironmentVariables(f)));

    [JsonIgnore]
    public bool CanInstall => Winget is not null || Capability is not null || Url is not null;
}
