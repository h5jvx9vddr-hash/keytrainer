using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Logging;
using KeyTrainer.Core.Updates;
using KeyTrainer.Input;
using KeyTrainer.Updates;

namespace KeyTrainer;

/// <summary>Debug window: lists every detected combination and typed text, plus version and update.</summary>
public partial class MainWindow : Window
{
    const int MaxEntries = 200;

    // Typed text is emitted after this much idle time (or earlier by a special key).
    static readonly TimeSpan TextIdle = TimeSpan.FromMilliseconds(1500);

    readonly ComboDetector _detector = new();
    readonly RawKeyboardSource _source;
    readonly DispatcherTimer _textTimer;
    readonly ObservableCollection<TokenRow> _rows = [];
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

        _detector.TokenDetected += OnToken;
        _source = new RawKeyboardSource(_detector);
        _source.KeyProcessed += () =>
        {
            _textTimer.Stop();
            if (_detector.HasPendingText) _textTimer.Start();
        };

        // Win+L locks the session before the key-ups arrive; forget stuck keys.
        SystemEvents.SessionSwitch += OnSessionSwitch;
        UpdateStatus();
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Log.Info($"Sitzungswechsel: {e.Reason}");
        Dispatcher.InvokeAsync(_detector.Reset);
    }

    void OnToken(InputToken token)
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

    void OnToggle(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_source.IsRunning) _source.Stop();
            else _source.Start();
        }
        catch (InvalidOperationException ex)
        {
            // Already logged by the source.
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

            if (_source.IsRunning) _source.Stop();
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

    void UpdateStatus()
    {
        bool running = _source.IsRunning;
        StartLabel.Text = running ? "Erfassung stoppen" : "Erfassung starten";
        StartIcon.Text = running ? "" : "";
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
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _textTimer.Stop();
        _source.Dispose();
        base.OnClosing(e);
    }
}
