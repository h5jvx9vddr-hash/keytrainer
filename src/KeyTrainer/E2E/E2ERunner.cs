using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Logging;
using KeyTrainer.Core.Training;
using KeyTrainer.Core.Windows;
using KeyTrainer.Input;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer.E2E;

/// <summary>
/// "KeyTrainer.exe --e2e [id-präfix …] [--extras] [--elevated]": plays every exercise with real key presses (its <c>auto</c> actions)
/// and checks that the trainer recognises each step. Writes report.md plus a screenshot per failure to
/// %APPDATA%\KeyTrainer\e2e. Exit code 0 = no failures. Hands off mouse and keyboard while it runs;
/// Strg + Alt + Shift + Q aborts. Started without admin rights, steps inside elevated windows (MMC consoles, Task-Manager)
/// cannot be played; --elevated also runs exercises whose target opens elevated.
/// </summary>
internal sealed class E2ERunner
{
    static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan KeysTimeout = TimeSpan.FromSeconds(2);
    const int ActionGapMs = 120;

    static readonly JsonSerializerOptions ReportJson = new(ExerciseCatalog.JsonOptions)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // How to get into a context when a keys step starts elsewhere.
    static readonly Dictionary<string, string[]> Openers = new()
    {
        ["explorer"] = ["Win+E", "wait:1800"],
        ["editor"] = ["Win", "wait:1200", "text:editor", "wait:1200", "Enter", "wait:2500"],
        ["browser"] = ["Win", "wait:1200", "text:edge", "wait:1200", "Enter", "wait:5000"],
        ["terminal"] = ["Win+X", "wait:800", "text:i", "wait:3000"],
        ["windowsTerminal"] = ["Win+X", "wait:800", "text:i", "wait:3000"],
        ["powershell"] = ["Win+X", "wait:800", "text:i", "wait:3000"],
        ["cmd"] = ["Win+R", "wait:800", "text:cmd", "Enter", "wait:3000"],
    };

    readonly ExerciseFile _catalog;
    readonly string[] _filters;
    readonly bool _extras;
    readonly bool _elevated;
    readonly string _outDir;
    readonly WindowsWorld _world = new();
    readonly ComboDetector _detector = new();
    readonly List<string> _stepTokens = [];
    readonly StringBuilder _report = new();
    Window? _sandbox;
    TrainingSession? _session;
    bool _aborted;

    E2ERunner(ExerciseFile catalog, string[] filters, bool extras, bool elevated)
    {
        _catalog = catalog;
        _filters = filters;
        _extras = extras;
        _elevated = elevated;
        _outDir = Path.Combine(App.DataDirectory, "e2e");
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var filters = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var runner = new E2ERunner(App.LoadCatalog(), filters, args.Contains("--extras"), args.Contains("--elevated"));
        try
        {
            return await runner.RunAllAsync();
        }
        catch (Exception ex)
        {
            Log.Error("E2E: abgebrochen", ex);
            return 2;
        }
    }

    async Task<int> RunAllAsync()
    {
        if (Directory.Exists(_outDir)) Directory.Delete(_outDir, recursive: true);
        Directory.CreateDirectory(_outDir);
        Log.Info($"E2E: Start, Filter [{string.Join(", ", _filters)}], Extras {_extras}, Erhöht {_elevated}, als Admin {Desktop.IsElevated((uint)Environment.ProcessId)}, Bericht {_outDir}");

        _sandbox = CreateSandbox();
        _sandbox.Show();
        using var keyboard = new KeyboardSource(_detector);
        using var mouse = new MouseSource();
        using var hotkeys = new AbortHotkey(() => _aborted = true);
        _detector.TokenDetected += OnToken;
        keyboard.Start();

        SetSandboxText("Automatischer Test startet in 5 Sekunden.\nBitte Maus und Tastatur nicht benutzen.\nAbbrechen: Strg + Alt + Shift + Q");
        await Task.Delay(5000);

        var rows = new List<(string Id, string Result, string Detail)>();
        rows.Add(await MouseCheckAsync(mouse));

        var exercises = _catalog.Exercises.Where(Selected).ToList();
        int index = 0;
        foreach (var ex in exercises)
        {
            if (_aborted) break;
            index++;
            SetSandboxText($"Test {index}/{exercises.Count}: {ex.Id}\n{ex.Prompt}\n\nAbbrechen: Strg + Alt + Shift + Q");
            rows.Add(await RunExerciseAsync(ex));
        }
        keyboard.Stop();
        _sandbox.Close();

        int ok = rows.Count(r => r.Result == "OK"), failed = rows.Count(r => r.Result.StartsWith("FEHLER", StringComparison.Ordinal));
        WriteReport(rows, ok, failed);
        Log.Info($"E2E: fertig – {ok} OK, {failed} Fehler, {rows.Count - ok - failed} übersprungen{(_aborted ? ", ABGEBROCHEN" : "")}");
        return failed > 0 || _aborted ? 1 : 0;
    }

    bool Selected(Exercise ex)
    {
        if (_filters.Length > 0) return _filters.Any(f => ex.Id.StartsWith(f, StringComparison.OrdinalIgnoreCase));
        return _extras || !_catalog.ExtraCategories.Contains(ex.Category);
    }

    async Task<(string, string, string)> MouseCheckAsync(MouseSource mouse)
    {
        bool seen = false;
        mouse.Used += () => seen = true;
        mouse.Start();
        var center = _sandbox!.PointToScreen(new Point(_sandbox.ActualWidth / 2, _sandbox.ActualHeight / 2));
        InputInjector.LeftClick((int)center.X, (int)center.Y);
        await Task.Delay(800);
        mouse.Stop();
        return seen ? ("(maus)", "OK", "Klick erkannt") : ("(maus)", "FEHLER", "Maus-Klick wurde nicht erkannt");
    }

    async Task<(string, string, string)> RunExerciseAsync(Exercise ex)
    {
        if (ex.NoAuto) return (ex.Id, "übersprungen", "noAuto (Sperre, Netztrennung oder echte UAC-Abfrage – von Hand testen)");
        if (ex.Elevated && !_elevated) return (ex.Id, "übersprungen", "öffnet ein erhöhtes Fenster (nur mit --elevated)");
        if (!TrainingSession.IsSupported(ex)) return (ex.Id, "übersprungen", "Übungsseite (Phase 5)");
        if (ex.Requires is { } req && _catalog.Software.TryGetValue(req, out var sw) && !sw.IsInstalled(p => File.Exists(p) || Directory.Exists(p)))
            return (ex.Id, "übersprungen", $"Software fehlt: {sw.Name}");

        await ActivateSandboxAsync();
        if (ForegroundBlocker() is { } blocker)
        {
            // An elevated window (UIPI) swallows all synthetic input: every following test would fail misleadingly.
            Log.Warn($"E2E: abgebrochen, Vordergrund blockiert: {blocker}");
            _aborted = true;
            return (ex.Id, "FEHLER", $"Abbruch: {blocker} lässt sich nicht verlassen (vermutlich mit Admin-Rechten). Bitte von Hand schließen und Test neu starten.");
        }
        var before = Desktop.VisibleWindows();
        var started = DateTime.Now;
        _session = new TrainingSession([ex], _catalog.Contexts, _world);
        _session.Next();
        var steps = ex.AllSteps;
        string result = "OK", detail = "";

        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (_aborted) return (ex.Id, "abgebrochen", "");
                var step = steps[i];
                if (step.Auto.Count == 0)
                {
                    result = i == 0 ? "übersprungen" : $"teilweise ({i}/{steps.Count})";
                    detail = $"Schritt {i + 1} hat keinen automatischen Ablauf";
                    break;
                }
                if (i > 0 && Desktop.InputBlocked())
                {
                    result = $"teilweise ({i}/{steps.Count})";
                    detail = $"Schritt {i + 1} spielt in einem erhöhten Fenster – Test als Administrator starten";
                    break;
                }
                _stepTokens.Clear();
                if (step.Mode == ExerciseMode.Keys && step.Context is { } ctx && !InContext(ctx) && Openers.TryGetValue(ctx, out var opener))
                    await PlayAsync(opener);
                await PlayAsync(step.Auto);
                _detector.Flush();

                if (!await WaitForStepAsync(i, step.Mode == ExerciseMode.Keys ? KeysTimeout : ResultTimeout))
                {
                    result = $"FEHLER Schritt {i + 1}/{steps.Count}";
                    detail = Diagnose(step, i, ex.Id);
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"E2E: {ex.Id} – Ausnahme", e);
            result = "FEHLER";
            detail = "Ausnahme: " + e.Message;
        }
        finally
        {
            await ActivateSandboxAsync();
            await Desktop.CleanupAsync(before, started);
        }
        Log.Info($"E2E: {ex.Id} – {result}{(detail.Length > 0 ? " – " + detail : "")}");
        return (ex.Id, result, detail);
    }

    async Task PlayAsync(IEnumerable<string> actions)
    {
        foreach (var action in actions)
        {
            if (_aborted) return;
            if (action.StartsWith("wait:", StringComparison.Ordinal)) await Task.Delay(int.Parse(action[5..]));
            else if (action.StartsWith(InputToken.TextPrefix, StringComparison.Ordinal)) InputInjector.Text(action[InputToken.TextPrefix.Length..]);
            else await InputInjector.ComboAsync(action);
            // Let the hook callbacks and the detector run.
            await Task.Delay(ActionGapMs);
        }
    }

    async Task<bool> WaitForStepAsync(int index, TimeSpan timeout)
    {
        var end = DateTime.Now + timeout;
        while (DateTime.Now < end)
        {
            _session!.Poll();
            if (_session.IsSolved || _session.StepIndex > index) return true;
            await Task.Delay(200);
        }
        return false;
    }

    void OnToken(InputToken token)
    {
        _stepTokens.Add(token.ToString());
        _session?.OnToken(token, ForegroundWindow.Snapshot());
    }

    bool InContext(string name) =>
        _catalog.Contexts.TryGetValue(name, out var ctx) && ForegroundWindow.Snapshot() is { } fg && WindowMatcher.Matches(ctx, fg);

    string Diagnose(TaskDefinition step, int index, string id)
    {
        var fg = ForegroundWindow.Snapshot();
        var shot = $"{id}-schritt{index + 1}.png";
        bool saved = Desktop.Screenshot(Path.Combine(_outDir, shot));
        var expected = step.Mode == ExerciseMode.Keys
            ? "Tasten " + string.Join(" | ", step.Expect!.AllSequences.Select(s => string.Join(", ", s)))
            : "verify " + JsonSerializer.Serialize(step.Verify, ReportJson);
        // Synthetic key presses only – nothing a person typed.
        return $"Erwartet: {expected}<br>Vordergrund: {(fg is null ? "keins" : $"`{fg.Process}` Klasse `{fg.WindowClass}` Titel „{fg.Title}“ {fg.Placement}{(Desktop.InputBlocked() ? " (erhöht, Eingaben blockiert)" : "")}")}"
               + $"<br>Erkannte Eingaben: {(_stepTokens.Count == 0 ? "keine" : string.Join(", ", _stepTokens))}"
               + $"<br>Rückmeldung: {_session?.LastFeedback.Message}"
               + (saved ? $"<br>Screenshot: `{shot}`" : "");
    }

    void WriteReport(List<(string Id, string Result, string Detail)> rows, int ok, int failed)
    {
        _report.AppendLine($"# KeyTrainer E2E-Bericht {DateTime.Now:yyyy-MM-dd HH:mm}");
        _report.AppendLine();
        _report.AppendLine($"Version {Updates.Updater.CurrentVersion.ToString(3)}, Windows {Environment.OSVersion.Version}, als Admin: {(Desktop.IsElevated((uint)Environment.ProcessId) ? "ja" : "nein")}, " +
                           $"Layout 0x{GetKeyboardLayout(0).ToInt64():X8}{(_aborted ? ", **abgebrochen**" : "")}");
        _report.AppendLine();
        _report.AppendLine($"**{ok} OK · {failed} Fehler · {rows.Count - ok - failed} übersprungen/teilweise**");
        _report.AppendLine();
        _report.AppendLine("| Aufgabe | Ergebnis | Details |");
        _report.AppendLine("|---|---|---|");
        foreach (var (id, result, detail) in rows.OrderBy(r => r.Result == "OK").ThenBy(r => r.Id))
            _report.AppendLine($"| {id} | {result} | {detail.Replace("|", "\\|")} |");
        File.WriteAllText(Path.Combine(_outDir, "report.md"), _report.ToString(), Encoding.UTF8);
    }

    string? ForegroundBlocker()
    {
        if (GetForegroundWindow() == new WindowInteropHelper(_sandbox!).Handle) return null;
        return ForegroundWindow.Snapshot() is { } fg ? $"`{fg.Process}` „{fg.Title}“" : "eine UAC-Abfrage oder Sperre";
    }

    async Task ActivateSandboxAsync()
    {
        _sandbox!.WindowState = WindowState.Normal;
        _sandbox.Activate();
        SetForegroundWindow(new WindowInteropHelper(_sandbox).Handle);
        await Task.Delay(400);
    }

    Window CreateSandbox() => new()
    {
        Title = "KeyTrainer – automatischer Test",
        Width = 520, Height = 220,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Topmost = false,
        Content = new TextBlock { Margin = new Thickness(20), FontSize = 16, TextWrapping = TextWrapping.Wrap, Name = "Status" },
        Background = Brushes.White,
    };

    void SetSandboxText(string text) => ((TextBlock)_sandbox!.Content).Text = text;

    /// <summary>Strg + Alt + Shift + Q stops the run.</summary>
    sealed class AbortHotkey : IDisposable
    {
        const int Id = 99;
        readonly HwndSource _window = new(new HwndSourceParameters("KeyTrainerE2E") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });

        public AbortHotkey(Action onAbort)
        {
            _window.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == WM_HOTKEY && w.ToInt32() == Id)
                {
                    Log.Warn("E2E: Abbruch per Strg + Alt + Shift + Q");
                    onAbort();
                    handled = true;
                }
                return IntPtr.Zero;
            });
            RegisterHotKey(_window.Handle, Id, MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, 'Q');
        }

        public void Dispose()
        {
            UnregisterHotKey(_window.Handle, Id);
            _window.Dispose();
        }
    }
}
