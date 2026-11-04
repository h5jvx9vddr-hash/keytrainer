using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Training;
using KeyTrainer.Core.Windows;

namespace KeyTrainer.Tests;

public class TrainingSessionTests
{
    DateTime _now = new(2026, 11, 4, 10, 0, 0);

    static readonly Dictionary<string, WindowMatch> Contexts = new()
    {
        ["explorer"] = new WindowMatch { Process = "explorer.exe", WindowClass = "CabinetWClass", Label = "den Explorer" },
    };

    static readonly WindowSnapshot Explorer = new("explorer.exe", "CabinetWClass", "Downloads");
    static readonly WindowSnapshot Notepad = new("notepad.exe", "Notepad", "Unbenannt");

    static Exercise Ex(string id, string[] seq, string? context = null, string[][]? alts = null, Dictionary<string, string>? explain = null) => new()
    {
        Id = id, Category = "Test", Prompt = "p " + id, Hints = ["Tipp 1"], Solution = "Lösung " + id,
        Mode = ExerciseMode.Keys, Context = context,
        Expect = new Expect { Sequence = [.. seq], Alternatives = alts?.Select(a => a.ToList()).ToList() ?? [] },
        ExplainWrong = explain ?? [],
    };

    TrainingSession Session(TrainingMode mode, params Exercise[] exercises)
    {
        var s = new TrainingSession(exercises, mode, Contexts, () => _now, new Random(1));
        s.Next();
        return s;
    }

    static InputToken C(string combo) => InputToken.FromCombo(Combo.Parse(combo));

    static InputToken T(string text) => InputToken.FromText(text);

    [Fact]
    public void SingleCombo_Solves_AndRecordsDuration()
    {
        var s = Session(TrainingMode.Quiz, Ex("a", ["Win+E"]));
        _now = _now.AddSeconds(2.5);
        var fb = s.OnToken(C("Win+E"), Notepad);
        Assert.Equal(FeedbackKind.Correct, fb.Kind);
        Assert.Contains("2,5", fb.Message.Replace('.', ','));
        Assert.True(s.IsSolved);
        Assert.Single(s.Attempts);
        Assert.True(s.Attempts[0].Solved);
    }

    [Fact]
    public void Sequence_WithText_ShowsProgress_ThenSolves()
    {
        var s = Session(TrainingMode.Quiz, Ex("dienste", ["Win+R", "text:services.msc", "Enter"]));
        Assert.Equal(FeedbackKind.Progress, s.OnToken(C("Win+R"), Notepad).Kind);
        Assert.Equal(FeedbackKind.Progress, s.OnToken(T("SERVICES.msc"), Notepad).Kind);
        Assert.Equal(FeedbackKind.Correct, s.OnToken(C("Enter"), Notepad).Kind);
    }

    [Fact]
    public void WrongToken_RestartsSequence_AndCounts()
    {
        var s = Session(TrainingMode.Quiz, Ex("dienste", ["Win+R", "text:services.msc", "Enter"]));
        s.OnToken(C("Win+R"), Notepad);
        var fb = s.OnToken(T("servics.msc"), Notepad);
        Assert.Equal(FeedbackKind.Wrong, fb.Kind);
        Assert.Equal(1, s.WrongAttempts);
        // Enter alone does not solve after a reset.
        Assert.Equal(FeedbackKind.Wrong, s.OnToken(C("Enter"), Notepad).Kind);
        s.OnToken(C("Win+R"), Notepad);
        s.OnToken(T("services.msc"), Notepad);
        Assert.Equal(FeedbackKind.Correct, s.OnToken(C("Enter"), Notepad).Kind);
    }

    [Fact]
    public void Alternative_Sequence_IsAccepted()
    {
        var s = Session(TrainingMode.Quiz, Ex("ctx", ["Shift+F10"], alts: [["Menü"]]));
        Assert.Equal(FeedbackKind.Correct, s.OnToken(C("Menü"), Notepad).Kind);
    }

    [Fact]
    public void Context_Required_WrongWindowIsNotPenalized()
    {
        var s = Session(TrainingMode.Quiz, Ex("adr", ["Strg+L"], context: "explorer"));
        var fb = s.OnToken(C("Strg+L"), Notepad);
        Assert.Equal(FeedbackKind.WrongContext, fb.Kind);
        Assert.Contains("den Explorer", fb.Message);
        Assert.Equal(0, s.WrongAttempts);
        Assert.Equal(FeedbackKind.Correct, s.OnToken(C("Strg+L"), Explorer).Kind);
    }

    [Fact]
    public void ExplainWrong_IsUsed()
    {
        var s = Session(TrainingMode.Quiz, Ex("close", ["Alt+F4"], explain: new() { ["Strg + F4"] = "Nur der Tab." }));
        Assert.Equal("Nur der Tab.", s.OnToken(C("Strg+F4"), Notepad).Message);
        Assert.Contains("Strg + W", s.OnToken(C("Strg+W"), Notepad).Message);
    }

    [Fact]
    public void Quiz_Hints_StepUp_ByMistakesTimeAndRequest()
    {
        var s = Session(TrainingMode.Quiz, Ex("dienste", ["Win+R", "text:services.msc", "Enter"]));
        Assert.Equal(0, s.HintLevel);
        Assert.Null(s.HintText);

        s.OnToken(C("Strg+X"), Notepad);
        s.OnToken(C("Strg+Y"), Notepad);
        Assert.Equal(1, s.HintLevel);
        Assert.Equal("Tipp 1", s.HintText);

        _now += TrainingSession.AutoHintInterval;
        Assert.True(s.Tick());
        Assert.Equal("Beginnt mit: Win + R", s.HintText);

        s.ShowHint();
        Assert.Equal("Lösung: Lösung dienste", s.HintText);
        s.ShowHint();
        Assert.Equal(s.MaxHintLevel, s.HintLevel);
    }

    [Fact]
    public void Learn_ShowsSolutionImmediately()
    {
        var s = Session(TrainingMode.Learn, Ex("a", ["Win+E"]));
        Assert.Equal("Lösung: Lösung a", s.HintText);
        Assert.False(s.Tick());
    }

    [Fact]
    public void Next_RecordsSkip_AndCyclesThroughPool()
    {
        var s = Session(TrainingMode.Quiz, Ex("a", ["Win+E"]), Ex("b", ["Win+D"]));
        var first = s.Current!.Id;
        s.Next();
        Assert.NotEqual(first, s.Current!.Id);
        Assert.True(s.Attempts[0].Skipped);
        s.Next();
        s.Next();
        Assert.Equal(4, s.Number);
    }

    [Fact]
    public void ResultOnlyExercises_AreFilteredOut()
    {
        var result = new Exercise
        {
            Id = "r", Category = "T", Prompt = "p", Hints = ["h"], Solution = "s", Mode = ExerciseMode.Result,
            Verify = new Verify { SessionLock = true },
        };
        var s = new TrainingSession([result, Ex("a", ["Win+E"])], TrainingMode.Quiz, Contexts);
        Assert.Equal(1, s.PoolSize);
    }

    [Fact]
    public void SolvedExercise_IgnoresFurtherInput()
    {
        var s = Session(TrainingMode.Quiz, Ex("a", ["Win+E"]));
        s.OnToken(C("Win+E"), Notepad);
        Assert.Equal(FeedbackKind.None, s.OnToken(C("Win+D"), Notepad).Kind);
        Assert.Single(s.Attempts);
    }

    [Fact]
    public void WindowMatcher_ProcessAlternativesAndRegex()
    {
        var m = new WindowMatch { Process = "mmc.exe | MMC2.exe", TitleRegex = "^Dienste" };
        Assert.True(WindowMatcher.Matches(m, new("MMC.EXE", "MMCMainFrame", "Dienste (Lokal)")));
        Assert.False(WindowMatcher.Matches(m, new("mmc.exe", "MMCMainFrame", "Ereignisanzeige")));
        Assert.False(WindowMatcher.Matches(m, new("notepad.exe", "Notepad", "Dienste")));
    }

    [Fact]
    public void Requirements_ExpandEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("KT_TEST_ROOT", "/kt");
        var r = new Requirements { AnyFile = ["%KT_TEST_ROOT%/a.msc", "/nope"] };
        Assert.True(r.IsMet(p => p == "/kt/a.msc"));
        Assert.False(r.IsMet(_ => false));
    }

    [Fact]
    public void ShippedCatalog_StartsSession_WithMostExercises()
    {
        var file = ExerciseCatalog.Load(Path.Combine(AppContext.BaseDirectory, "exercises.json"));
        var s = new TrainingSession(file.Exercises, TrainingMode.Quiz, file.Contexts);
        Assert.True(s.PoolSize >= 120, $"nur {s.PoolSize}");
        s.Next();
        Assert.NotNull(s.Current);
    }
}
