using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Windows;

namespace KeyTrainer.Core.Training;

public enum TrainingMode
{
    /// <summary>Solution is shown right away; for new shortcuts.</summary>
    Learn,
    /// <summary>Only the task; hints come on request, after mistakes or after a while.</summary>
    Quiz,
}

public enum FeedbackKind
{
    None,
    Correct,
    Progress,
    Wrong,
    WrongContext,
}

public sealed record Feedback(FeedbackKind Kind, string Message)
{
    public static readonly Feedback None = new(FeedbackKind.None, "");
}

/// <summary>Outcome of one exercise; kept for statistics. Never contains typed text or keys.</summary>
public sealed record AttemptRecord(
    string ExerciseId, TrainingMode Mode, DateTime Started, TimeSpan Duration,
    bool Solved, bool Skipped, int WrongAttempts, int HintLevel);

/// <summary>
/// Runs exercises one after another and checks the token stream against the expected sequences.
/// Platform-free: the caller supplies tokens, the foreground window and the clock.
/// </summary>
public sealed class TrainingSession
{
    /// <summary>Quiz: one more hint level per this much time without solving.</summary>
    public static readonly TimeSpan AutoHintInterval = TimeSpan.FromSeconds(20);

    /// <summary>Quiz: one more hint level per this many wrong attempts.</summary>
    public const int WrongAttemptsPerHint = 2;

    readonly List<Exercise> _pool;
    readonly IReadOnlyDictionary<string, WindowMatch> _contexts;
    readonly Func<DateTime> _clock;
    readonly Random _random;
    readonly List<AttemptRecord> _attempts = [];
    readonly Queue<Exercise> _queue = new();

    List<List<ExpectToken>> _sequences = [];
    int[] _positions = [];
    DateTime _started;
    DateTime _lastHintAt;

    public TrainingSession(
        IEnumerable<Exercise> exercises, TrainingMode mode, IReadOnlyDictionary<string, WindowMatch> contexts,
        Func<DateTime>? clock = null, Random? random = null)
    {
        _pool = exercises.Where(IsSupported).ToList();
        if (_pool.Count == 0) throw new ArgumentException("Keine passenden Übungen.", nameof(exercises));
        Mode = mode;
        _contexts = contexts;
        _clock = clock ?? (() => DateTime.Now);
        _random = random ?? new Random();
    }

    public TrainingMode Mode { get; }

    public Exercise? Current { get; private set; }

    public int Number { get; private set; }

    public int PoolSize => _pool.Count;

    public int HintLevel { get; private set; }

    public int MaxHintLevel => Current is null ? 0 : Current.Hints.Count + 2;

    public int WrongAttempts { get; private set; }

    public bool IsSolved { get; private set; }

    public Feedback LastFeedback { get; private set; } = Feedback.None;

    public IReadOnlyList<AttemptRecord> Attempts => _attempts;

    public TimeSpan Elapsed => Current is null ? TimeSpan.Zero : _clock() - _started;

    /// <summary>Context the current exercise needs, or null.</summary>
    public WindowMatch? RequiredContext =>
        Current?.Context is { } name && _contexts.TryGetValue(name, out var ctx) ? ctx : null;

    /// <summary>Raised when an exercise finished (solved or skipped).</summary>
    public event Action<AttemptRecord>? AttemptFinished;

    /// <summary>Exercises this phase can check: key-based ones (keysAndResult is checked by its keys only for now).</summary>
    public static bool IsSupported(Exercise e) =>
        e.Mode is ExerciseMode.Keys or ExerciseMode.KeysAndResult && e.Expect is { Sequence.Count: > 0 };

    /// <summary>Moves to the next exercise. Unfinished current exercise is recorded as skipped.</summary>
    public void Next()
    {
        if (Current is not null && !IsSolved) Finish(solved: false, skipped: true);
        if (_queue.Count == 0) Refill();

        Current = _queue.Dequeue();
        Number++;
        _sequences = Current.Expect!.AllSequences.Select(s => s.Select(ExpectToken.Parse).ToList()).ToList();
        _positions = new int[_sequences.Count];
        _started = _lastHintAt = _clock();
        WrongAttempts = 0;
        IsSolved = false;
        HintLevel = Mode == TrainingMode.Learn ? MaxHintLevel : 0;
        LastFeedback = Feedback.None;
    }

    public void ShowHint()
    {
        if (Current is null || HintLevel >= MaxHintLevel) return;
        HintLevel++;
        _lastHintAt = _clock();
    }

    /// <summary>Text of the current hint level, or null if no hint is shown yet.</summary>
    public string? HintText
    {
        get
        {
            if (Current is null || HintLevel == 0) return null;
            if (HintLevel <= Current.Hints.Count) return Current.Hints[HintLevel - 1];
            if (HintLevel == Current.Hints.Count + 1) return "Beginnt mit: " + Display(_sequences[0][0]);
            return "Lösung: " + Current.Solution;
        }
    }

    /// <summary>Call about once per second; raises the hint level in quiz mode after a while.</summary>
    public bool Tick()
    {
        if (Current is null || IsSolved || Mode != TrainingMode.Quiz || HintLevel >= MaxHintLevel) return false;
        if (_clock() - _lastHintAt < AutoHintInterval) return false;
        ShowHint();
        return true;
    }

    public Feedback OnToken(InputToken token, WindowSnapshot? foreground)
    {
        if (Current is null || IsSolved) return Feedback.None;

        if (RequiredContext is { } ctx && (foreground is null || !WindowMatcher.Matches(ctx, foreground)))
            return LastFeedback = new Feedback(FeedbackKind.WrongContext, $"Wechsle zuerst in {ctx.Label ?? Current.Context}.");

        bool advanced = false;
        for (int i = 0; i < _sequences.Count; i++)
        {
            var seq = _sequences[i];
            if (seq[_positions[i]].Matches(token))
            {
                _positions[i]++;
                advanced = true;
            }
            else
            {
                // A wrong token restarts the sequence; it may itself be a fresh start.
                _positions[i] = seq[0].Matches(token) ? 1 : 0;
                advanced |= _positions[i] > 0;
            }

            if (_positions[i] == seq.Count)
            {
                IsSolved = true;
                var record = Finish(solved: true, skipped: false);
                return LastFeedback = new Feedback(FeedbackKind.Correct, $"Richtig – {record.Duration.TotalSeconds:0.0} s");
            }
        }

        if (advanced)
        {
            int best = _positions.Max();
            int total = _sequences[Array.IndexOf(_positions, best)].Count;
            return LastFeedback = new Feedback(FeedbackKind.Progress, $"Gut – Schritt {best} von {total}");
        }

        WrongAttempts++;
        if (Mode == TrainingMode.Quiz && WrongAttempts % WrongAttemptsPerHint == 0) ShowHint();
        return LastFeedback = new Feedback(FeedbackKind.Wrong, ExplainWrong(token));
    }

    string ExplainWrong(InputToken token)
    {
        if (token.Kind == TokenKind.Combo && Current!.ExplainWrong.FirstOrDefault(kv => Combo.Normalize(kv.Key) == token.Value) is { Value: { } why })
            return why;
        return token.Kind == TokenKind.Text
            ? $"„{token.Value}“ passt nicht."
            : $"{token.Value.Replace("+", " + ")} war nicht gesucht.";
    }

    AttemptRecord Finish(bool solved, bool skipped)
    {
        var record = new AttemptRecord(Current!.Id, Mode, _started, _clock() - _started, solved, skipped, WrongAttempts, HintLevel);
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

    static string Display(ExpectToken token)
    {
        if (token.Kind == TokenKind.Combo) return token.Value.Replace("+", " + ");
        var text = token.Value.Replace("*", "…");
        return text.Length > 4 ? $"„{text[..3]}…“ tippen" : $"„{text}“ tippen";
    }
}
