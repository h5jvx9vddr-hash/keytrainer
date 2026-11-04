using System.Runtime.InteropServices;
using System.Windows.Interop;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Logging;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer.Input;

internal enum TrainerCommand
{
    Hint = 1,
    Next = 2,
    Pause = 3,
    Collapse = 4,
}

/// <summary>
/// The trainer's own global shortcuts (Strg + Shift + F1–F4) via RegisterHotKey, never via the hook.
/// Not Strg + Alt: that is AltGr on German keyboards.
/// </summary>
internal sealed class TrainerHotkeys : IDisposable
{
    static readonly IntPtr HWND_MESSAGE = new(-3);

    public static readonly IReadOnlyDictionary<TrainerCommand, string> Combos = new Dictionary<TrainerCommand, string>
    {
        [TrainerCommand.Hint] = "Strg+Shift+F1",
        [TrainerCommand.Next] = "Strg+Shift+F2",
        [TrainerCommand.Pause] = "Strg+Shift+F3",
        [TrainerCommand.Collapse] = "Strg+Shift+F4",
    };

    /// <summary>Normal-form combos the training must ignore (the hook sees them too).</summary>
    public static IReadOnlySet<string> IgnoredTokens { get; } = Combos.Values.Select(Combo.Normalize).ToHashSet();

    readonly HwndSource _window;
    readonly List<int> _registered = [];

    public event Action<TrainerCommand>? Pressed;

    public TrainerHotkeys()
    {
        _window = new HwndSource(new HwndSourceParameters("KeyTrainerHotkeys") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        _window.AddHook(WndProc);
        foreach (var command in Enum.GetValues<TrainerCommand>())
        {
            uint vk = (uint)(Vk.F1 + (int)command - 1);
            if (RegisterHotKey(_window.Handle, (int)command, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, vk))
                _registered.Add((int)command);
            else
                Log.Warn($"Trainer-Kürzel {Combos[command]} nicht registriert (belegt?), Win32-Fehler {Marshal.GetLastWin32Error()}");
        }
        Log.Info($"Trainer-Kürzel registriert: {_registered.Count} von {Combos.Count}");
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && Enum.IsDefined(typeof(TrainerCommand), wParam.ToInt32()))
        {
            handled = true;
            Pressed?.Invoke((TrainerCommand)wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered) UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }
}
