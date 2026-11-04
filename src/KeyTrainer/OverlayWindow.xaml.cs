using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using KeyTrainer.Core.Training;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer;

/// <summary>
/// Task bar at the top centre of the screen. Never takes focus (WS_EX_NOACTIVATE), otherwise
/// the practised shortcuts would go to the overlay instead of the target program.
/// </summary>
public partial class OverlayWindow : Window
{
    const int WM_MOUSEACTIVATE = 0x0021;
    const int MA_NOACTIVATE = 3;

    static readonly Brush CorrectBg = Frozen(0x1F, 0x6F, 0x3F);
    static readonly Brush WrongBg = Frozen(0x8A, 0x2A, 0x2A);
    static readonly Brush InfoBg = Frozen(0x33, 0x3A, 0x48);
    static readonly Brush ContextBg = Frozen(0x6A, 0x55, 0x1E);

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeNonActivating();
        SizeChanged += (_, _) => PlaceTopCenter();
    }

    public bool IsCollapsed { get; private set; }

    public void ToggleCollapsed()
    {
        IsCollapsed = !IsCollapsed;
        var detail = IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        MetaRow.Visibility = detail;
        LegendText.Visibility = detail;
        HintRow.Margin = IsCollapsed ? new Thickness(0, 4, 0, 0) : new Thickness(0, 8, 0, 0);
        PromptText.FontSize = IsCollapsed ? 15 : 20;
        PromptText.Margin = IsCollapsed ? new Thickness(0) : new Thickness(0, 8, 0, 0);
        Card.Padding = IsCollapsed ? new Thickness(16, 8, 16, 8) : new Thickness(20, 14, 20, 14);
    }

    public void Render(TrainingSession session, bool paused)
    {
        var ex = session.Current;
        var step = session.Step;
        if (ex is null || step is null) return;

        CategoryText.Text = ex.Category;
        MetaText.Text = $"Aufgabe {session.Number}  ·  Stufe {ex.Level}"
                        + (session.MouseUsed ? "  ·  Maus benutzt" : "  ·  ohne Maus")
                        + (ex.Unverified ? "  ·  ungeprüft" : "");
        PromptText.Text = paused ? "Pausiert – Strg + Alt + Shift + P zum Fortsetzen" : ex.Prompt;

        bool scenario = session.StepCount > 1;
        StepRow.Visibility = !paused && scenario ? Visibility.Visible : Visibility.Collapsed;
        StepBadge.Text = $"Schritt {session.StepIndex + 1}/{session.StepCount}";
        StepText.Text = step.Prompt;

        var hint = session.HintText;
        HintRow.Visibility = !paused && hint is not null ? Visibility.Visible : Visibility.Collapsed;
        HintText.Text = hint ?? "";

        var fb = session.LastFeedback;
        if (paused || fb.Kind == FeedbackKind.None)
        {
            FeedbackBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            FeedbackBox.Visibility = Visibility.Visible;
            FeedbackBox.Background = fb.Kind switch
            {
                FeedbackKind.Correct or FeedbackKind.StepDone => CorrectBg,
                FeedbackKind.Mouse or FeedbackKind.CorrectWithMouse => WrongBg,
                FeedbackKind.WrongContext => ContextBg,
                _ => InfoBg,
            };
            FeedbackText.Text = fb.Message;
        }
        RenderTimer(session);
    }

    public void RenderTimer(TrainingSession session)
    {
        var t = session.Elapsed;
        TimerText.Text = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    void PlaceTopCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top;
    }

    void MakeNonActivating()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST));
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        PlaceTopCenter();
    }

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Clicking the overlay must not activate it either.
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
