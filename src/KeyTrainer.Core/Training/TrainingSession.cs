using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Windows;

namespace KeyTrainer.Core.Training;

public enum FeedbackKind
{
    None,
    /// <summary>Neutral echo of what was pressed, or an explanation of a mix-up.</summary>
    Info,
    StepDone,
    Correct,
    /// <summary>Goal reached, but the mouse was used.</summary>
    CorrectWithMouse,
    WrongContext,
    Mouse,
}

public sealed record Feedback(FeedbackKind Kind, string Message)
{
    public static readonly Feedback None = new(FeedbackKind.None, "");
}

/// <summary>Outcome of one exercise; kept for statistics. Never contains typed text or keys.</summary>
public sealed record AttemptRecord(
    string ExerciseId, DateTime Started, TimeSpan Duration,
    bool Solved, bool Skipped, bool MouseUsed, int HintLevel, int StepsDone, int StepCount);

/// <summary>
/// Runs exercises one after another. Result steps are checked against the PC state (any keyboard way counts);
/// keys steps against the token stream, where unrelated keys are simply ignored. Using the mouse voids the exercise.
/// Platform-free: the caller supplies tokens, mouse events, the clock and an <see cref="IWorld"/>.
/// </summary>
public sealed class TrainingSession
{
    /// <summary>One more hint level per this much time without progress.</summary>
    public static readonly TimeSpan AutoHintInterval = TimeSpan.FromSeconds(20);

    readonly List<Exercise> _pool;
    readonly IReadOnlyDictionary<string, WindowMatch> _contexts;
    readonly IWorld _world;
    readonly Func<DateTime> _clock;
    readonly Random _random;
    readonly List<AttemptRecord> _attempts = [];
    readonly Queue<Exercise> _queue = new();

    List<List<ExpectToken>> _sequences = [];
    int[] _positions = [];
    StepBaseline _baseline = new();
    DateTime _started;
    DateTime _lastHintAt;
    bool _wasInContext = true;

    public TrainingSession(
        IEnumerable<Exercise> exercises, IReadOnlyDictionary<string, WindowMatch> contexts, IWorld world,
        bool showSolution = false, Func<DateTime>? clock = null, Random? random = null)
    {
        _pool = exercises.Where(IsSupported).ToList();
        if (_pool.Count == 0) throw new ArgumentException("Keine passenden Übungen.", nameof(exercises));
        _contexts = contexts;
        _world = world;
        ShowSolution = showSolution;
        _clock = clock ?? (() => DateTime.Now);
        _random = random ?? new Random();
    }

    /// <summary>Learning mode: the fastest way is shown right away.</summary>
    public bool ShowSolution { get; }

    public Exercise? Current { get; private set; }

    public TaskDefinition? Step => Current?.AllSteps[StepIndex];

    public int StepIndex { get; private set; }

    public int StepCount => Current?.AllSteps.Count ?? 0;

    public int Number { get; private set; }

    public int PoolSize => _pool.Count;

    public int HintLevel { get; private set; }

    public int MaxHintLevel => Step is null ? 0 : Step.Hints.Count + 1;

    public bool MouseUsed { get; private set; }

    public bool IsSolved { get; private set; }

    public Feedback LastFeedback { get; private set; } = Feedback.None;

    public IReadOnlyList<AttemptRecord> Attempts => _attempts;

    public TimeSpan Elapsed => Current is null ? TimeSpan.Zero : _clock() - _started;

    /// <summary>Context the current keys step needs, or null.</summary>
    public WindowMatch? RequiredContext =>
        Step is { Mode: ExerciseMode.Keys, Context: { } name } && _contexts.TryGetValue(name, out var ctx) ? ctx : null;

    /// <summary>Raised when an exercise finished (solved or skipped).</summary>
    public event Action<AttemptRecord>? AttemptFinished;

    /// <summary>Practice-page exercises need the page from phase 5.</summary>
    public static bool IsSupported(Exercise e) =>
        e.AllSteps.Count > 0 && e.AllSteps.All(s => s.Verify?.PracticeTitle is null);

    public void Next()
    {
        if (Current is not null && !IsSolved) Finish(solved: false, skipped: true);
        if (_queue.Count == 0) Refill();

        Current = _queue.Dequeue();
        Number++;
        _started = _clock();
        MouseUsed = false;
        IsSolved = false;
        BeginStep(0);
    }

    public void ShowHint()
    {
        if (Step is null || HintLevel >= MaxHintLevel) return;
        HintLevel++;
        _lastHintAt = _clock();
    }

    /// <summary>Text of the current hint level, or null if no hint is shown yet.</summary>
    public string? HintText
    {
        get
        {
            if (Step is null || HintLevel == 0) return null;
            return HintLevel <= Step.Hints.Count ? Step.Hints[HintLevel - 1] : "Schnellster Weg: " + Step.Solution;
        }
    }

    /// <summary>Call a few times per second: checks result steps, live context and timed hints. True if anything changed.</summary>
    public bool Poll()
    {
        if (Step is not { } step || IsSolved) return false;
        bool changed = false;

        if (step.Mode == ExerciseMode.Result && step.Verify is { } verify && Verifier.IsMet(verify, _world, _baseline))
            return CompleteStep();

        if (RequiredContext is { } ctx)
        {
            bool inContext = _world.Foreground() is { } fg && WindowMatcher.Matches(ctx, fg);
            if (inContext != _wasInContext)
            {
                _wasInContext = inContext;
                LastFeedback = inContext
                    ? new Feedback(FeedbackKind.Info, $"Gut – du bist in {ctx.Label}.")
                    : new Feedback(FeedbackKind.WrongContext, $"Wechsle zuerst in {ctx.Label}.");
                changed = true;
            }
        }

        if (!ShowSolution && HintLevel < MaxHintLevel && _clock() - _lastHintAt >= AutoHintInterval)
        {
            ShowHint();
            changed = true;
        }
        return changed;
    }

    public Feedback OnToken(InputToken token, WindowSnapshot? foreground)
    {
        if (Step is not { } step || IsSolved) return Feedback.None;

        if (step.Mode == ExerciseMode.Keys)
        {
            if (RequiredContext is { } ctx && (foreground is null || !WindowMatcher.Matches(ctx, foreground)))
            {
                _wasInContext = false;
                return LastFeedback = new Feedback(FeedbackKind.WrongContext, $"Wechsle zuerst in {ctx.Label}.");
            }
            _wasInContext = true;

            for (int i = 0; i < _sequences.Count; i++)
            {
                // Unrelated keys are ignored; the expected ones only have to come in order.
                if (_sequences[i][_positions[i]].Matches(token)) _positions[i]++;
                if (_positions[i] == _sequences[i].Count) return CompleteStepFeedback();
            }
        }

        if (token.Kind == TokenKind.Combo
            && step.ExplainWrong.FirstOrDefault(kv => Combo.Normalize(kv.Key) == token.Value) is { Value: { } why })
            return LastFeedback = new Feedback(FeedbackKind.Info, why);

        // Typed text is not echoed (it can be anything), keys are.
        var echo = token.Kind == TokenKind.Text ? "Text getippt" : "Gedrückt: " + token.Value.Replace("+", " + ");
        if (LastFeedback.Kind is FeedbackKind.Mouse or FeedbackKind.WrongContext) return LastFeedback;
        return LastFeedback = new Feedback(FeedbackKind.Info, echo);
    }

    /// <summary>A mouse click or wheel turn happened during the exercise.</summary>
    public Feedback OnMouse()
    {
        if (Current is null || IsSolved || MouseUsed) return Feedback.None;
        MouseUsed = true;
        return LastFeedback = new Feedback(FeedbackKind.Mouse, "Maus benutzt – diese Aufgabe zählt nicht. Nur die Tastatur!");
    }

    Feedback CompleteStepFeedback()
    {
        CompleteStep();
        return LastFeedback;
    }

    bool CompleteStep()
    {
        if (StepIndex + 1 < StepCount)
        {
            BeginStep(StepIndex + 1);
            LastFeedback = new Feedback(FeedbackKind.StepDone, $"Schritt {StepIndex} geschafft – weiter mit Schritt {StepIndex + 1}.");
            return true;
        }

        IsSolved = true;
        var record = Finish(solved: true, skipped: false);
        var seconds = $"{record.Duration.TotalSeconds:0.0} s";
        LastFeedback = MouseUsed
            ? new Feedback(FeedbackKind.CorrectWithMouse, $"Ziel erreicht ({seconds}) – aber mit Maus, zählt nicht.")
            : new Feedback(FeedbackKind.Correct, $"Geschafft – {seconds}, nur mit Tastatur.");
        return true;
    }

    void BeginStep(int index)
    {
        StepIndex = index;
        var step = Current!.AllSteps[index];
        _sequences = step.Expect?.AllSequences.Select(s => s.Select(ExpectToken.Parse).ToList()).ToList() ?? [];
        _positions = new int[_sequences.Count];
        _baseline = Verifier.Begin(step.Verify, _world, _clock());
        _lastHintAt = _clock();
        _wasInContext = true;
        HintLevel = ShowSolution ? MaxHintLevel : 0;
        LastFeedback = Feedback.None;
    }

    AttemptRecord Finish(bool solved, bool skipped)
    {
        var record = new AttemptRecord(Current!.Id, _started, _clock() - _started, solved && !MouseUsed, skipped, MouseUsed,
            HintLevel, solved ? StepCount : StepIndex, StepCount);
        _attempts.Add(record);
        AttemptFinished?.Invoke(record);
        return record;
    }

    void Refill()
    {
        // Easy first, random within a level; avoid repeating the last exercise right away.
        var order = _pool.OrderBy(e => e.Level).ThenBy(_ => _random.Next()).ToList();
        if (order.Count > 1 && order[0] == Current) (order[0], order[1]) = (order[1], order[0]);
        foreach (var e in order) _queue.Enqueue(e);
    }
}
