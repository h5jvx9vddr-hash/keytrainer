using System.Runtime.InteropServices;
using System.Windows.Threading;
using KeyTrainer.Core.Logging;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer.Input;

/// <summary>
/// Notices mouse clicks and wheel turns during an exercise (WH_MOUSE_LL). Only counts – no positions
/// are kept, nothing is blocked, every event is passed on. Active only while training runs.
/// </summary>
internal sealed class MouseSource : IDisposable
{
    readonly Dispatcher _dispatcher;
    // Keep a reference: a collected delegate would crash the hook callback.
    readonly LowLevelProc _proc;
    IntPtr _hook;
    int _count;

    public event Action? Used;

    public bool IsRunning => _hook != IntPtr.Zero;

    public MouseSource()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = HookProc;
    }

    public void Start()
    {
        if (IsRunning) return;
        _count = 0;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            // Training still works without it; only the mouse check is missing.
            Log.Warn($"Maus-Erkennung nicht verfügbar, Win32-Fehler {Marshal.GetLastWin32Error()}");
        else
            Log.Info("Maus-Erkennung gestartet");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        Log.Info($"Maus-Erkennung gestoppt ({_count} Klicks/Scrolls)");
    }

    public void Dispose() => Stop();

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN
                or WM_XBUTTONDOWN or WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
        {
            _count++;
            _dispatcher.BeginInvoke(() => Used?.Invoke());
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
