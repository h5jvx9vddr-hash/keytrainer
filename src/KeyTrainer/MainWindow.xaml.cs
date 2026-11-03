using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Logging;
using KeyTrainer.Input;

namespace KeyTrainer;

/// <summary>Phase 1 debug window: lists every detected combination and typed text.</summary>
public partial class MainWindow : Window
{
    const int MaxEntries = 200;

    // Typed text is emitted after this much idle time (or earlier by a special key).
    static readonly TimeSpan TextIdle = TimeSpan.FromMilliseconds(1500);

    readonly ComboDetector _detector = new();
    readonly RawKeyboardSource _source;
    readonly DispatcherTimer _textTimer;

    public MainWindow()
    {
        InitializeComponent();

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
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Log.Info($"Sitzungswechsel: {e.Reason}");
        Dispatcher.InvokeAsync(_detector.Reset);
    }

    void OnToken(InputToken token)
    {
        TokenList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {token}");
        while (TokenList.Items.Count > MaxEntries) TokenList.Items.RemoveAt(TokenList.Items.Count - 1);
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
            MessageBox.Show(this, $"{ex.Message}\n\nDetails im Log:\n{Log.CurrentFile}", "KeyTrainer",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        StartButton.Content = _source.IsRunning ? "Erfassung stoppen" : "Erfassung starten";
        StatusText.Text = _source.IsRunning ? "Erfassung läuft" : "Erfassung aus";
    }

    void OnClear(object sender, RoutedEventArgs e) => TokenList.Items.Clear();

    void OnOpenLog(object sender, RoutedEventArgs e) => App.OpenLogFolder();

    protected override void OnClosing(CancelEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _textTimer.Stop();
        _source.Dispose();
        base.OnClosing(e);
    }
}
