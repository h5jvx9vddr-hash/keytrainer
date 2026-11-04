using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Logging;
using KeyTrainer.Core.Training;
using KeyTrainer.Core.Updates;
using KeyTrainer.Input;
using KeyTrainer.Updates;

namespace KeyTrainer;

/// <summary>Start screen: training setup, detection test, version and update.</summary>
public partial class MainWindow : Window
{
    const int MaxEntries = 200;
    const string AllCategories = "Alle Bereiche";

    // Typed text is emitted after this much idle time (or earlier by a special key).
    static readonly TimeSpan TextIdle = TimeSpan.FromMilliseconds(1500);

    // How long "Richtig" stays visible before the next exercise.
    static readonly TimeSpan NextDelay = TimeSpan.FromMilliseconds(1600);

    readonly ComboDetector _detector = new();
    readonly KeyboardSource _source;
    readonly DispatcherTimer _textTimer;
    readonly DispatcherTimer _tickTimer;
    readonly DispatcherTimer _nextTimer;
    readonly ObservableCollection<TokenRow> _rows = [];
    readonly ExerciseFile? _catalog;

    bool _debugCapture;
    TrainingSession? _session;
    OverlayWindow? _overlay;
    TrainerHotkeys? _hotkeys;
    bool _paused;

    ReleaseInfo? _pendingUpdate;
    bool _updating;

    public MainWindow()
    {
        InitializeComponent();
        TokenList.ItemsSource = _rows;
        VersionText.Text = $"Version {Updater.CurrentVersion.ToString(3)}";
        UpdateText.Text = "Lädt die neueste Version von GitHub.";

        _textTimer = new DispatcherTimer { Interval = TextIdle };
        _textTimer.Tick += (_, _) =>
        {
            _textTimer.Stop();
            _detector.Flush();
        };
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => OnTick();
        _nextTimer = new DispatcherTimer { Interval = NextDelay };
        _nextTimer.Tick += (_, _) =>
        {
            _nextTimer.Stop();
            NextExercise();
        };

        _detector.TokenDetected += OnToken;
        _source = new KeyboardSource(_detector);
        _source.KeyProcessed += () =>
        {
            _textTimer.Stop();
            if (_detector.HasPendingText) _textTimer.Start();
        };

        try
        {
            _catalog = App.LoadCatalog();
            Log.Info($"Katalog geladen: {_catalog.Exercises.Count} Übungen");
        }
        catch (Exception ex)
        {
            Log.Error("Übungskatalog konnte nicht geladen werden", ex);
            PoolText.Text = "Übungskatalog fehlerhaft – siehe Log.";
            TrainButton.IsEnabled = false;
        }
        if (_catalog is not null)
        {
            CategoryBox.Items.Add(AllCategories);
            foreach (var category in _catalog.Exercises.Select(e => e.Category).Distinct())
                CategoryBox.Items.Add(category);
            CategoryBox.SelectedIndex = 0;
        }

        // Win+L locks the session before the key-ups arrive; forget stuck keys.
        SystemEvents.SessionSwitch += OnSessionSwitch;
        UpdatePool();
        UpdateStatus();
    }

    // ---------------------------------------------------------------- training

    List<Exercise> SelectedExercises(out int missingSoftware)
    {
        missingSoftware = 0;
        if (_catalog is null) return [];
        var category = CategoryBox.SelectedItem as string;
        var result = new List<Exercise>();
        foreach (var ex in _catalog.Exercises)
        {
            if (category is not null && category != AllCategories && ex.Category != category) continue;
            if (!TrainingSession.IsSupported(ex)) continue;
            if (ex.Requires is { } req && !req.IsMet(File.Exists))
            {
                missingSoftware++;
                continue;
            }
            result.Add(ex);
        }
        return result;
    }

    void OnFilterChanged(object sender, SelectionChangedEventArgs e) => UpdatePool();

    void UpdatePool()
    {
        if (_catalog is null || PoolText is null) return;
        var list = SelectedExercises(out int missing);
        PoolText.Text = $"{list.Count} Übungen" + (missing > 0 ? $" · {missing} brauchen Software, die hier fehlt" : "");
        if (_session is null) TrainButton.IsEnabled = list.Count > 0;
    }

    void OnTrain(object sender, RoutedEventArgs e)
    {
        if (_session is null) StartTraining();
        else StopTraining();
    }

    void StartTraining()
    {
        var exercises = SelectedExercises(out _);
        if (_catalog is null || exercises.Count == 0) return;
        var mode = ModeBox.SelectedIndex == 0 ? TrainingMode.Learn : TrainingMode.Quiz;

        try
        {
            _session = new TrainingSession(exercises, mode, _catalog.Contexts);
            _session.AttemptFinished += OnAttemptFinished;
            _hotkeys = new TrainerHotkeys();
            _hotkeys.Pressed += OnHotkey;
            EnsureCapture();
        }
        catch (Exception ex)
        {
            Log.Error("Training konnte nicht gestartet werden", ex);
            StopTraining();
            ShowError(ex.Message);
            return;
        }

        Log.Info($"Training gestartet: {mode}, {exercises.Count} Übungen, Bereich {CategoryBox.SelectedItem}");
        _paused = false;
        _overlay = new OverlayWindow();
        _session.Next();
        _overlay.Render(_session, _paused);
        _overlay.Show();
        _tickTimer.Start();
        // Practise in the real programs, not in this window.
        WindowState = WindowState.Minimized;
        UpdateTrainingUi();
    }

    void StopTraining()
    {
        _nextTimer.Stop();
        _tickTimer.Stop();
        _overlay?.Close();
        _overlay = null;
        _hotkeys?.Dispose();
        _hotkeys = null;

        if (_session is { } session)
        {
            var solved = session.Attempts.Count(a => a.Solved);
            var noHint = session.Attempts.Count(a => a.Solved && a.HintLevel == 0);
            Log.Info($"Training beendet: {session.Attempts.Count} Übungen, {solved} gelöst, {noHint} ohne Hinweis");
            SessionText.Text = $"Letztes Training: {solved} von {session.Attempts.Count} gelöst, {noHint} davon ohne Hinweis.";
            SessionText.Visibility = Visibility.Visible;
        }
        _session = null;
        ReleaseCapture();
        UpdateTrainingUi();
        UpdatePool();
    }

    void OnAttemptFinished(AttemptRecord a) =>
        // Tracking: outcome only, never keys or typed text.
        Log.Info($"Übung {a.ExerciseId}: {(a.Solved ? "gelöst" : "übersprungen")} nach {a.Duration.TotalSeconds:0.0} s, " +
                 $"{a.WrongAttempts} Fehlversuche, Hinweisstufe {a.HintLevel}, Modus {a.Mode}");

    void NextExercise()
    {
        if (_session is null || _overlay is null) return;
        _session.Next();
        _overlay.Render(_session, _paused);
    }

    void OnHotkey(TrainerCommand command)
    {
        if (_session is null || _overlay is null) return;
        switch (command)
        {
            case TrainerCommand.Hint:
                _session.ShowHint();
                break;
            case TrainerCommand.Next:
                _nextTimer.Stop();
                _session.Next();
                break;
            case TrainerCommand.Pause:
                _paused = !_paused;
                // The hook runs only while an exercise is active.
                if (_paused) ReleaseCapture();
                else EnsureCapture();
                Log.Info(_paused ? "Training pausiert" : "Training fortgesetzt");
                break;
            case TrainerCommand.Collapse:
                _overlay.ToggleCollapsed();
                break;
        }
        _overlay.Render(_session, _paused);
        UpdateStatus();
    }

    void OnTick()
    {
        if (_session is null || _overlay is null || _paused) return;
        if (_session.Tick()) _overlay.Render(_session, _paused);
        else _overlay.RenderTimer(_session);
    }

    void UpdateTrainingUi()
    {
        bool active = _session is not null;
        TrainLabel.Text = active ? "Training beenden" : "Training starten";
        TrainIcon.Text = active ? "" : "";
        ModeBox.IsEnabled = CategoryBox.IsEnabled = !active;
        UpdateStatus();
    }

    // ---------------------------------------------------------------- input

    void EnsureCapture()
    {
        if (!_source.IsRunning) _source.Start();
    }

    void ReleaseCapture()
    {
        bool trainingNeedsIt = _session is not null && !_paused;
        if (!trainingNeedsIt && !_debugCapture && _source.IsRunning) _source.Stop();
    }

    void OnToken(InputToken token)
    {
        if (DebugArea.Visibility == Visibility.Visible) AddRow(token);

        if (_session is null || _overlay is null || _paused || _session.IsSolved) return;
        // The trainer's own shortcuts reach the hook as well.
        if (token.Kind == TokenKind.Combo && TrainerHotkeys.IgnoredTokens.Contains(token.Value)) return;

        var feedback = _session.OnToken(token, ForegroundWindow.Snapshot());
        _overlay.Render(_session, _paused);
        if (feedback.Kind == FeedbackKind.Correct) _nextTimer.Start();
    }

    void AddRow(InputToken token)
    {
        TokenRow row;
        try
        {
            row = new TokenRow(token, DateTime.Now);
        }
        catch (FormatException ex)
        {
            // Never lose an entry because of a display problem; show it as plain text instead.
            Log.Warn($"Anzeige einer Kombination fehlgeschlagen ({token.Kind})", ex);
            row = new TokenRow(InputToken.FromText(token.Value), DateTime.Now);
        }
        _rows.Insert(0, row);
        while (_rows.Count > MaxEntries) _rows.RemoveAt(_rows.Count - 1);
        UpdateStatus();
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Log.Info($"Sitzungswechsel: {e.Reason}");
        Dispatcher.InvokeAsync(_detector.Reset);
    }

    // ---------------------------------------------------------------- detection test

    void OnDebugExpanded(object sender, RoutedEventArgs e)
    {
        DebugArea.Visibility = DebugExpander.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (!DebugExpander.IsExpanded && _debugCapture)
        {
            _debugCapture = false;
            ReleaseCapture();
        }
        UpdateStatus();
    }

    void OnToggle(object sender, RoutedEventArgs e)
    {
        try
        {
            _debugCapture = !_debugCapture;
            if (_debugCapture) EnsureCapture();
            else ReleaseCapture();
        }
        catch (InvalidOperationException ex)
        {
            // Already logged by the source.
            _debugCapture = false;
            ShowError(ex.Message);
        }
        UpdateStatus();
    }

    void OnClear(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        UpdateStatus();
    }

    void OnOpenLog(object sender, RoutedEventArgs e) => App.OpenLogFolder();

    // ---------------------------------------------------------------- update

    async void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        UpdateButton.IsEnabled = false;
        try
        {
            if (_pendingUpdate is null)
            {
                UpdateText.Text = "Suche nach Updates …";
                _pendingUpdate = await Updater.CheckAsync();
                if (_pendingUpdate is null)
                {
                    UpdateText.Text = $"Du hast die neueste Version ({DateTime.Now:HH:mm} geprüft).";
                    return;
                }
            }

            var info = _pendingUpdate;
            UpdateText.Text = $"Version {info.Version.ToString(3)} verfügbar ({info.ExeSize / 1048576} MB).";
            UpdateLabel.Text = "Jetzt installieren";
            var answer = MessageBox.Show(this,
                $"Version {info.Version.ToString(3)} herunterladen und installieren?\n\nDer Trainer startet danach automatisch neu.",
                "KeyTrainer – Update", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            if (_session is not null) StopTraining();
            _debugCapture = false;
            ReleaseCapture();
            UpdateProgress.Value = 0;
            UpdateProgress.Visibility = Visibility.Visible;
            var progress = new Progress<double>(p =>
            {
                UpdateProgress.Value = p;
                UpdateText.Text = $"Lade Version {info.Version.ToString(3)} … {p:P0}";
            });
            await Updater.InstallAsync(info, progress);
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warn("Update: keine Verbindung zu GitHub", ex);
            UpdateText.Text = "Keine Verbindung zu GitHub. Internet prüfen und erneut versuchen.";
        }
        catch (Exception ex)
        {
            Log.Error("Update fehlgeschlagen", ex);
            UpdateText.Text = "Update fehlgeschlagen.";
            ShowError(ex.Message);
        }
        finally
        {
            _updating = false;
            UpdateButton.IsEnabled = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateStatus();
        }
    }

    // ---------------------------------------------------------------- common

    void UpdateStatus()
    {
        bool running = _source.IsRunning;
        StartLabel.Text = _debugCapture ? "Erfassung stoppen" : "Erfassung starten";
        StartIcon.Text = _debugCapture ? "" : "";
        StatusText.Text = running ? "Erfassung läuft" : "Erfassung aus";
        StatusDot.Fill = Brush(running ? "SystemFillColorSuccessBrush" : "TextFillColorDisabledBrush",
                               running ? Brushes.SeaGreen : Brushes.Gray);
        CountText.Text = _rows.Count == 1 ? "1 Eingabe" : $"{_rows.Count} Eingaben";
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_updating) UpdateLabel.Text = _pendingUpdate is null ? "Nach Updates suchen" : "Jetzt installieren";
    }

    Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    void ShowError(string message)
    {
        MessageBox.Show(this, $"{message}\n\nDetails im Log:\n{Log.CurrentFile}", "KeyTrainer",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_session is not null) StopTraining();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _textTimer.Stop();
        _source.Dispose();
        base.OnClosing(e);
    }
}
