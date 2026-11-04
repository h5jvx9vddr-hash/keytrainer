using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Training;
using KeyTrainer.Core.Windows;

namespace KeyTrainer.Tests;

/// <summary>A PC whose state the tests set directly.</summary>
sealed class FakeWorld : IWorld
{
    public WindowSnapshot? Window { get; set; }
    public HashSet<string> Processes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime> StartTimes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Paths { get; } = [];
    public DateTime? LastSessionLock { get; set; }

    public WindowSnapshot? Foreground() => Window;
    public bool IsProcessRunning(string name) => Processes.Contains(name);
    public DateTime? ProcessStartTime(string name) => StartTimes.TryGetValue(name, out var t) ? t : null;
    public bool PathExists(string path) => Paths.Contains(path);
}

public class TrainingSessionTests
{
    DateTime _now = new(2026, 11, 4, 10, 0, 0);
    readonly FakeWorld _world = new();

    static readonly Dictionary<string, WindowMatch> Contexts = new()
    {
        ["explorer"] = new WindowMatch { Process = "explorer.exe", WindowClass = "CabinetWClass", Label = "den Explorer" },
    };

    static readonly WindowSnapshot Explorer = new("explorer.exe", "CabinetWClass", "Downloads");
    static readonly WindowSnapshot Notepad = new("Notepad.exe", "Notepad", "Unbenannt - Editor");

    static Exercise Keys(string id, string[] seq, string? context = null, string[][]? alts = null, Dictionary<string, string>? explain = null) => new()
    {
        Id = id, Category = "Test", Prompt = "p " + id, Hints = ["Tipp 1"], Solution = "Lösung " + id,
        Mode = ExerciseMode.Keys, Context = context,
        Expect = new Expect { Sequence = [.. seq], Alternatives = alts?.Select(a => a.ToList()).ToList() ?? [] },
        ExplainWrong = explain ?? [],
    };

    static Exercise Goal(string id, Verify verify) => new()
    {
        Id = id, Category = "Test", Prompt = "p " + id, Hints = ["Tipp 1", "Tipp 2"], Solution = "Lösung " + id,
        Mode = ExerciseMode.Result, Verify = verify,
    };

    static ScenarioStep GoalStep(Verify verify) => new() { Prompt = "s", Hints = ["h"], Solution = "x", Mode = ExerciseMode.Result, Verify = verify };

    TrainingSession Session(params Exercise[] exercises) => Session(false, exercises);

    TrainingSession Session(bool showSolution, params Exercise[] exercises)
    {
        var s = new TrainingSession(exercises, Contexts, _world, showSolution, () => _now, new Random(1));
        s.Next();
        return s;
    }

    static InputToken C(string combo) => InputToken.FromCombo(Combo.Parse(combo));

    static InputToken T(string text) => InputToken.FromText(text);

    [Fact]
    public void Result_AnyWayCounts_WhenWindowAppears()
    {
        var s = Session(Goal("explorer", new Verify { Process = "explorer.exe", WindowClass = "CabinetWClass" }));
        _world.Window = Notepad;
        Assert.False(s.Poll());
        _now = _now.AddSeconds(3);
        _world.Window = Explorer;
        Assert.True(s.Poll());
        Assert.True(s.IsSolved);
        Assert.Equal(FeedbackKind.Correct, s.LastFeedback.Kind);
        Assert.True(s.Attempts[0].Solved);
    }

    [Fact]
    public void Mouse_VoidsTheExercise()
    {
        var s = Session(Goal("explorer", new Verify { WindowClass = "CabinetWClass" }));
        Assert.Equal(FeedbackKind.Mouse, s.OnMouse().Kind);
        _world.Window = Explorer;
        s.Poll();
        Assert.Equal(FeedbackKind.CorrectWithMouse, s.LastFeedback.Kind);
        Assert.False(s.Attempts[0].Solved);
        Assert.True(s.Attempts[0].MouseUsed);
    }

    [Fact]
    public void Keys_UnrelatedKeysAreIgnored_NotErrors()
    {
        var s = Session(Keys("tab", ["Strg+Tab"]));
        var fb = s.OnToken(C("Strg+T"), Notepad);
        Assert.Equal(FeedbackKind.Info, fb.Kind);
        Assert.Equal("Gedrückt: Strg + T", fb.Message);
        s.OnToken(C("Strg+1"), Notepad);
        Assert.Equal(FeedbackKind.Correct, s.OnToken(C("Strg+Tab"), Notepad).Kind);
    }

    [Fact]
    public void Keys_SequenceWithText()
    {
        var s = Session(Keys("ping", ["text:ping *", "Enter"]));
        s.OnToken(T("PING 8.8.8.8"), Notepad);
        Assert.Equal(FeedbackKind.Correct, s.OnToken(C("Enter"), Notepad).Kind);
    }

    [Fact]
    public void Keys_TypedTextIsNotEchoed()
    {
        var s = Session(Keys("a", ["Win+E"]));
        Assert.Equal("Text getippt", s.OnToken(T("geheim"), Notepad).Message);
    }

    [Fact]
    public void Keys_Alternative_IsAccepted()
    {
        var s = Session(Keys("ctx", ["Shift+F10"], alts: [["Menü"]]));
        Assert.Equal(FeedbackKind.Correct, s.OnToken(C("Menü"), Notepad).Kind);
    }

    [Fact]
    public void Context_IsCheckedLive()
    {
        var s = Session(Keys("adr", ["Strg+L"], context: "explorer"));
        _world.Window = Notepad;
        Assert.Equal(FeedbackKind.WrongContext, s.OnToken(C("Strg+L"), Notepad).Kind);
        // User switches to the Explorer: the message updates without a key press.
        _world.Window = Explorer;
        Assert.True(s.Poll());
        Assert.Equal(FeedbackKind.Info, s.LastFeedback.Kind);
        Assert.Contains("den Explorer", s.LastFeedback.Message);
        Assert.Equal(FeedbackKind.Correct, s.OnToken(C("Strg+L"), Explorer).Kind);
    }

    [Fact]
    public void ExplainWrong_IsShownAsInfo()
    {
        var s = Session(Keys("close", ["Alt+F4"], explain: new() { ["Strg + F4"] = "Nur der Tab." }));
        var fb = s.OnToken(C("Strg+F4"), Notepad);
        Assert.Equal(FeedbackKind.Info, fb.Kind);
        Assert.Equal("Nur der Tab.", fb.Message);
    }

    [Fact]
    public void Hints_StepUp_ByTimeAndRequest_EndWithFastestWay()
    {
        var s = Session(Goal("g", new Verify { WindowClass = "CabinetWClass" }));
        Assert.Null(s.HintText);
        _now += TrainingSession.AutoHintInterval;
        Assert.True(s.Poll());
        Assert.Equal("Tipp 1", s.HintText);
        s.ShowHint();
        Assert.Equal("Tipp 2", s.HintText);
        s.ShowHint();
        Assert.Equal("Schnellster Weg: Lösung g", s.HintText);
        s.ShowHint();
        Assert.Equal(s.MaxHintLevel, s.HintLevel);
    }

    [Fact]
    public void ShowSolution_ShowsFastestWayRightAway()
    {
        var s = Session(true, Goal("g", new Verify { WindowClass = "CabinetWClass" }));
        Assert.Equal("Schnellster Weg: Lösung g", s.HintText);
    }

    [Fact]
    public void Scenario_StepsInOrder_WithFolderChecks()
    {
        var folder = Path.Combine("dl", "KT-Test");
        var scenario = new Exercise
        {
            Id = "ordner", Category = "Explorer", Prompt = "Ordner", Hints = ["h"], Solution = "x", Mode = ExerciseMode.Scenario,
            Steps =
            [
                GoalStep(new Verify { WindowClass = "CabinetWClass", TitleRegex = "^Downloads" }),
                GoalStep(new Verify { PathExists = folder }),
                GoalStep(new Verify { PathMissing = folder }),
            ],
        };
        var s = Session(scenario);
        _world.Window = Explorer;
        s.Poll();
        Assert.Equal(1, s.StepIndex);
        Assert.Equal(FeedbackKind.StepDone, s.LastFeedback.Kind);
        // Step 3 must not count before step 2 happened.
        s.Poll();
        Assert.Equal(1, s.StepIndex);
        _world.Paths.Add(folder);
        s.Poll();
        Assert.Equal(2, s.StepIndex);
        _world.Paths.Remove(folder);
        s.Poll();
        Assert.True(s.IsSolved);
        Assert.Equal(3, s.Attempts[0].StepsDone);
    }

    [Fact]
    public void Verify_TitleChanged_ComparesAgainstFirstSeenTitle()
    {
        var s = Session(Goal("tab", new Verify { Process = "msedge.exe", TitleChanged = true }));
        _world.Window = Notepad;
        s.Poll();
        _world.Window = new("msedge.exe", "Chrome_WidgetWin_1", "Wikipedia – Microsoft Edge");
        s.Poll();
        Assert.False(s.IsSolved);
        _world.Window = new("msedge.exe", "Chrome_WidgetWin_1", "Bing – Microsoft Edge");
        s.Poll();
        Assert.True(s.IsSolved);
    }

    [Fact]
    public void Verify_TitleChanged_RemembersTitleAtStepStart()
    {
        // The window is already in front when the step begins and changes before the first poll.
        _world.Window = new("msedge.exe", "Chrome_WidgetWin_1", "Wikipedia – Microsoft Edge");
        var s = Session(Goal("tab", new Verify { Process = "msedge.exe", TitleChanged = true }));
        _world.Window = new("msedge.exe", "Chrome_WidgetWin_1", "Bing – Microsoft Edge");
        s.Poll();
        Assert.True(s.IsSolved);
    }

    [Fact]
    public void Verify_WindowState_ProcessGone_Restart_Lock()
    {
        var max = Session(Goal("max", new Verify { WindowClass = "CabinetWClass", WindowState = "maximized" }));
        _world.Window = Explorer;
        max.Poll();
        Assert.False(max.IsSolved);
        _world.Window = Explorer with { Placement = WindowPlacement.Maximized };
        max.Poll();
        Assert.True(max.IsSolved);

        _world.Processes.Add("Notepad.exe");
        var gone = Session(Goal("gone", new Verify { ProcessGone = "Notepad.exe" }));
        gone.Poll();
        Assert.False(gone.IsSolved);
        _world.Processes.Clear();
        gone.Poll();
        Assert.True(gone.IsSolved);

        _world.StartTimes["spoolsv.exe"] = _now.AddHours(-1);
        var restart = Session(Goal("restart", new Verify { ProcessRestarted = "spoolsv.exe" }));
        restart.Poll();
        Assert.False(restart.IsSolved);
        _world.StartTimes["spoolsv.exe"] = _now.AddSeconds(1);
        restart.Poll();
        Assert.True(restart.IsSolved);

        _world.LastSessionLock = _now.AddMinutes(-5);
        var lockEx = Session(Goal("lock", new Verify { SessionLock = true }));
        lockEx.Poll();
        Assert.False(lockEx.IsSolved);
        _world.LastSessionLock = _now;
        lockEx.Poll();
        Assert.True(lockEx.IsSolved);
    }

    [Fact]
    public void Next_RecordsSkip_AndCyclesThroughPool()
    {
        var s = Session(Keys("a", ["Win+E"]), Keys("b", ["Win+D"]));
        var first = s.Current!.Id;
        s.Next();
        Assert.NotEqual(first, s.Current!.Id);
        Assert.True(s.Attempts[0].Skipped);
        s.Next();
        s.Next();
        Assert.Equal(4, s.Number);
    }

    [Fact]
    public void PracticePageExercises_AreFilteredOut()
    {
        var practice = Goal("p", new Verify { PracticeTitle = "x" });
        var s = new TrainingSession([practice, Keys("a", ["Win+E"])], Contexts, _world);
        Assert.Equal(1, s.PoolSize);
    }

    [Fact]
    public void WindowMatcher_AlternativesAndRegex()
    {
        var m = new WindowMatch { Process = "mmc.exe | MMC2.exe", WindowClass = "WorkerW|Progman", TitleRegex = "^Dienste" };
        Assert.True(WindowMatcher.Matches(m, new("MMC.EXE", "Progman", "Dienste (Lokal)")));
        Assert.False(WindowMatcher.Matches(m, new("mmc.exe", "Progman", "Ereignisanzeige")));
        Assert.False(WindowMatcher.Matches(m, new("mmc.exe", "Notepad", "Dienste")));
    }

    [Fact]
    public void Software_ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("KT_TEST_ROOT", "/kt");
        var sw = new Software { Name = "X", AnyFile = ["%KT_TEST_ROOT%/a.msc", "/nope"] };
        Assert.True(sw.IsInstalled(p => p == "/kt/a.msc"));
        Assert.False(sw.IsInstalled(_ => false));
    }

    [Fact]
    public void ShippedCatalog_EveryExerciseRunsAsSession()
    {
        var file = ExerciseCatalog.Load(Path.Combine(AppContext.BaseDirectory, "exercises.json"));
        var s = new TrainingSession(file.Exercises, file.Contexts, _world, clock: () => _now);
        Assert.Equal(file.Exercises.Count, s.PoolSize);
        for (int i = 0; i < s.PoolSize; i++)
        {
            s.Next();
            s.Poll();
            s.OnToken(C("Win+E"), Explorer);
        }
    }
}
