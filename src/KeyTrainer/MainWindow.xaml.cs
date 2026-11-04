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

/// <summary>Start screen: one start button, optional areas, missing software, detection test, update.</summary>
public partial class MainWindow : Window
{
    const int MaxEntries = 200;

    // Typed text is emitted after this much idle time (or earlier by a special key).
    static readonly TimeSpan TextIdle = TimeSpan.FromMilliseconds(1500);

    // How long "Geschafft" stays visible before the next exercise.
    static readonly TimeSpan NextDelay = TimeSpan.FromMilliseconds(1800);

    readonly ComboDetector _detector = new();
    readonly KeyboardSource _keyboard;
    readonly MouseSource _mouse = new();
    readonly WindowsWorld _world = new();
    readonly DispatcherTimer _textTimer;
    readonly DispatcherTimer _pollTimer;
    readonly DispatcherTimer _nextTimer;
    readonly ObservableCollection<TokenRow> _rows = [];
    readonly ExerciseFile? _catalog;
    readonly Dictionary<string, CheckBox> _areaBoxes = [];

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
        // Result checks, live context and the timer in the overlay.
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _pollTimer.Tick += (_, _) => OnPoll();
        _nextTimer = new DispatcherTimer { Interval = NextDelay };
        _nextTimer.Tick += (_, _) =>
        {
            _nextTimer.Stop();
            NextExercise();
        };

        _detector.TokenDetected += OnToken;
        _keyboard = new KeyboardSource(_detector);
        _keyboard.KeyProcessed += () =>
        {
            _textTimer.Stop();
            if (_detector.HasPendingText) _textTimer.Start();
        };
        _mouse.Used += OnMouseUsed;

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

        BuildAreas();
        BuildSoftware();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        UpdatePool();
        UpdateStatus();
        Loaded += (_, _) => TrainButton.Focus();
    }

    // ---------------------------------------------------------------- areas and software

    void BuildAreas()
    {
        if (_catalog is null) return;
        foreach (var group in _catalog.Exercises.GroupBy(e => e.Category))
        {
            bool extra = _catalog.ExtraCategories.Contains(group.Key);
            var box = new CheckBox
            {
                Content = $"{group.Key} ({group.Count()})" + (extra ? " · Extra" : ""),
                IsChecked = !extra,
                Margin = new Thickness(0, 0, 18, 6),
            };
            box.Checked += (_, _) => UpdatePool();
            box.Unchecked += (_, _) => UpdatePool();
            _areaBoxes[group.Key] = box;
            AreaPanel.Children.Add(box);
        }
    }

    bool IsInstalled(Software sw) => sw.IsInstalled(p => File.Exists(p) || Directory.Exists(p));

    void BuildSoftware()
    {
        SoftwarePanel.Children.Clear();
        if (_catalog is null) return;
        var missing = _catalog.Software.Where(kv => !IsInstalled(kv.Value)).ToList();
        SoftwareExpander.Header = missing.Count == 0 ? "Fehlende Software – alles da" : $"Fehlende Software ({missing.Count})";

        if (missing.Count == 0)
        {
            SoftwarePanel.Children.Add(new TextBlock { Text = "Alles installiert, was die Übungen brauchen.", Style = (Style)FindResource("Secondary") });
            return;
        }
        foreach (var (id, sw) in missing)
        {
            int count = _catalog.Exercises.Count(e => e.Requires == id);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            if (sw.CanInstall)
            {
                var button = new Button { Content = SoftwareInstaller.ActionLabel(sw), Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
                button.Click += async (_, _) => await InstallAsync(sw, button);
                DockPanel.SetDock(button, Dock.Right);
                row.Children.Add(button);
            }
            var text = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            text.Children.Add(new TextBlock { Text = sw.Name, FontWeight = FontWeights.SemiBold });
            var detail = $"für {count} Aufgabe{(count == 1 ? "" : "n")}"
                         + (sw.Capability is not null ? " · Windows-Feature, braucht Admin-Rechte" : "")
                         + (sw.Note is not null ? " · " + sw.Note : "");
            text.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Secondary") });
            row.Children.Add(text);
            SoftwarePanel.Children.Add(row);
        }
    }

    async Task InstallAsync(Software sw, Button button)
    {
        button.IsEnabled = false;
        button.Content = "Läuft …";
        try
        {
            bool ok = await SoftwareInstaller.InstallAsync(sw);
            if (!ok) MessageBox.Show(this, $"{sw.Name} wurde nicht installiert. Details im Log.", "KeyTrainer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
        }
        BuildSoftware();
        UpdatePool();
    }

    void OnRecheckSoftware(object sender, RoutedEventArgs e)
    {
        BuildSoftware();
        UpdatePool();
    }

    List<Exercise> SelectedExercises(out int missingSoftware)
    {
        missingSoftware = 0;
        if (_catalog is null) return [];
        var result = new List<Exercise>();
        foreach (var ex in _catalog.Exercises)
        {
            if (!_areaBoxes.TryGetValue(ex.Category, out var box) || box.IsChecked != true) continue;
            if (!TrainingSession.IsSupported(ex)) continue;
            if (ex.Requires is { } id && _catalog.Software.TryGetValue(id, out var sw) && !IsInstalled(sw))
            {
                missingSoftware++;
                continue;
            }
            result.Add(ex);
        }
        return result;
    }

    void UpdatePool()
    {
        if (_catalog is null || PoolText is null) return;
        var list = SelectedExercises(out int missing);
        int areas = _areaBoxes.Values.Count(b => b.IsChecked == true);
        PoolText.Text = $"{list.Count} Aufgaben aus {areas} Bereichen"
                        + (missing > 0 ? $" · {missing} weitere brauchen Software (siehe unten)" : "")
                        + " · Enter startet";
        if (_session is null) TrainButton.IsEnabled = list.Count > 0;
    }

    // ---------------------------------------------------------------- training

    void OnTrain(object sender, RoutedEventArgs e)
    {
        if (_session is null) StartTraining();
        else StopTraining();
    }

    void StartTraining()
    {
        var exercises = SelectedExercises(out _);
        if (_catalog is null || exercises.Count == 0) return;

        try
        {
            _session = new TrainingSession(exercises, _catalog.Contexts, _world, ShowSolutionBox.IsChecked == true);
            _session.AttemptFinished += OnAttemptFinished;
            _hotkeys = new TrainerHotkeys();
            _hotkeys.Pressed += OnHotkey;
            StartCapture();
        }
        catch (Exception ex)
        {
            Log.Error("Training konnte nicht gestartet werden", ex);
            StopTraining();
            ShowError(ex.Message);
            return;
        }

        Log.Info($"Training gestartet: {exercises.Count} Aufgaben, Lösung sofort: {_session.ShowSolution}");
        _paused = false;
        _overlay = new OverlayWindow();
        _session.Next();
        _overlay.Render(_session, _paused);
        _overlay.Show();
        _pollTimer.Start();
        // Practise in the real programs, not in this window.
        WindowState = WindowState.Minimized;
        UpdateTrainingUi();
    }

    void StopTraining()
    {
        _nextTimer.Stop();
        _pollTimer.Stop();
        _overlay?.Close();
        _overlay = null;
        _hotkeys?.Dispose();
        _hotkeys = null;

        if (_session is { } session)
        {
            var done = session.Attempts.Where(a => !a.Skipped).ToList();
            int clean = done.Count(a => a.Solved);
            int withMouse = done.Count(a => a.MouseUsed);
            Log.Info($"Training beendet: {session.Attempts.Count} Aufgaben, {clean} ohne Maus geschafft, {withMouse} mit Maus, " +
                     $"{session.Attempts.Count(a => a.Skipped)} übersprungen");
            SessionText.Text = session.Attempts.Count == 0
                ? ""
                : $"Letztes Training: {clean} von {session.Attempts.Count} Aufgaben ohne Maus geschafft" +
                  (withMouse > 0 ? $", {withMouse}× Maus benutzt." : ".");
            SessionText.Visibility = session.Attempts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        _session = null;
        StopCapture();
        UpdateTrainingUi();
        UpdatePool();
    }

    void OnAttemptFinished(AttemptRecord a) =>
        // Tracking: outcome only, never keys or typed text.
        Log.Info($"Aufgabe {a.ExerciseId}: {(a.Skipped ? "übersprungen" : a.Solved ? "geschafft" : "mit Maus")} nach " +
                 $"{a.Duration.TotalSeconds:0.0} s, Schritte {a.StepsDone}/{a.StepCount}, Hinweisstufe {a.HintLevel}, Maus {a.MouseUsed}");

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
                // Input is observed only while an exercise is active.
                if (_paused) StopCapture();
                else StartCapture();
                Log.Info(_paused ? "Training pausiert" : "Training fortgesetzt");
                break;
            case TrainerCommand.Collapse:
                _overlay.ToggleCollapsed();
                break;
        }
        _overlay.Render(_session, _paused);
        UpdateStatus();
    }

    void OnPoll()
    {
        if (_session is null || _overlay is null || _paused) return;
        bool wasSolved = _session.IsSolved;
        if (_session.Poll()) _overlay.Render(_session, _paused);
        else _overlay.RenderTimer(_session);
        if (!wasSolved && _session.IsSolved) _nextTimer.Start();
    }

    void OnMouseUsed()
    {
        if (_session is null || _overlay is null || _paused) return;
        if (_session.OnMouse().Kind != FeedbackKind.None) _overlay.Render(_session, _paused);
    }

    void UpdateTrainingUi()
    {
        bool active = _session is not null;
        TrainLabel.Text = active ? "Training beenden" : "Training starten";
        TrainIcon.Text = active ? "" : "";
        ShowSolutionBox.IsEnabled = AreasExpander.IsEnabled = !active;
        UpdateStatus();
    }

    // ---------------------------------------------------------------- input

    void StartCapture()
    {
        if (!_keyboard.IsRunning) _keyboard.Start();
        if (_session is not null && !_paused) _mouse.Start();
    }

    void StopCapture()
    {
        bool trainingNeedsIt = _session is not null && !_paused;
        if (!trainingNeedsIt) _mouse.Stop();
        if (!trainingNeedsIt && !_debugCapture) _keyboard.Stop();
    }

    void OnToken(InputToken token)
    {
        if (DebugExpander.IsExpanded) AddRow(token);

        if (_session is null || _overlay is null || _paused || _session.IsSolved) return;
        // The trainer's own shortcuts reach the hook as well.
        if (token.Kind == TokenKind.Combo && TrainerHotkeys.IgnoredTokens.Contains(token.Value)) return;

        _session.OnToken(token, ForegroundWindow.Snapshot());
        _overlay.Render(_session, _paused);
        if (_session.IsSolved) _nextTimer.Start();
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
        Dispatcher.InvokeAsync(() =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock) _world.LastSessionLock = DateTime.Now;
            // Win+L locks the session before the key-ups arrive; forget stuck keys.
            _detector.Reset();
        });
    }

    // ---------------------------------------------------------------- detection test

    void OnDebugExpanded(object sender, RoutedEventArgs e)
    {
        if (!DebugExpander.IsExpanded && _debugCapture)
        {
            _debugCapture = false;
            StopCapture();
        }
        UpdateStatus();
    }

    void OnToggle(object sender, RoutedEventArgs e)
    {
        try
        {
            _debugCapture = !_debugCapture;
            if (_debugCapture) StartCapture();
            else StopCapture();
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
            StopCapture();
            UpdateProgress.Value = 0;
            UpdateProgress.Visibility = Visibility.Visible;
            var progress = new Progress<double>(p =>
            {
                UpdateProgress.Value = p;
                UpdateText.Text = p < 1
                    ? $"Lade Version {info.Version.ToString(3)} … {p:P0}"
                    : "Prüfe den Download und starte neu …";
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
        bool running = _keyboard.IsRunning;
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
        _keyboard.Dispose();
        _mouse.Dispose();
        base.OnClosing(e);
    }
}
