using System.Runtime.InteropServices;
using System.Windows.Threading;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Logging;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer.Input;

/// <summary>
/// Observes keyboard input system-wide with a low-level hook (WH_KEYBOARD_LL). Raw Input is not enough:
/// shell shortcuts like Win+R never reach it. The hook only observes – every event is passed on
/// unchanged via CallNextHookEx, nothing is blocked, injected or written anywhere.
/// The hook is installed only while training or the detection test runs and is removed on exit.
/// </summary>
internal sealed class KeyboardSource : IDisposable
{
    // AltGr sends a fake left Ctrl with this scan code before the right Alt.
    const uint AltGrFakeCtrlScanCode = 0x21D;

    readonly ComboDetector _detector;
    readonly Dispatcher _dispatcher;
    readonly byte[] _keyState = new byte[256];
    readonly char[] _chars = new char[8];
    // Keep a reference: a collected delegate would crash the hook callback.
    readonly LowLevelProc _proc;
    IntPtr _hook;
    int _eventCount;

    public bool IsRunning => _hook != IntPtr.Zero;

    /// <summary>Raised after each processed key event (e.g. to restart a text idle timer).</summary>
    public event Action? KeyProcessed;

    public KeyboardSource(ComboDetector detector)
    {
        _detector = detector;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = HookProc;
    }

    public void Start()
    {
        if (IsRunning) return;
        _detector.Reset();
        _eventCount = 0;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            var ex = new InvalidOperationException($"Tastatur-Erfassung konnte nicht gestartet werden (Win32-Fehler {error}).");
            Log.Error($"SetWindowsHookEx(WH_KEYBOARD_LL) fehlgeschlagen, Win32-Fehler {error}", ex);
            throw ex;
        }
        Log.Info("Erfassung gestartet (Low-Level-Hook)");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        if (!UnhookWindowsHookEx(_hook))
            Log.Warn($"UnhookWindowsHookEx fehlgeschlagen, Win32-Fehler {Marshal.GetLastWin32Error()}");
        _hook = IntPtr.Zero;
        _detector.Flush();
        _detector.Reset();
        // Only counts, never key data.
        Log.Info($"Erfassung gestoppt ({_eventCount} Tastenereignisse)");
    }

    public void Dispose() => Stop();

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                // The callback must return fast (LowLevelHooksTimeout); process on the UI queue, in order.
                _dispatcher.BeginInvoke(DispatcherPriority.Send, () => Process(data));
            }
            catch (Exception ex)
            {
                Log.Error("Fehler im Tastatur-Hook", ex);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    void Process(KBDLLHOOKSTRUCT data)
    {
        if (!IsRunning) return;
        try
        {
            int vk = (int)data.VkCode;
            if (vk == Vk.LControl && data.ScanCode == AltGrFakeCtrlScanCode) return;

            bool isDown = (data.Flags & LLKHF_UP) == 0;
            string? text = null;
            if (isDown && !ComboDetector.IsModifier(vk))
            {
                uint scanCode = data.ScanCode | ((data.Flags & LLKHF_EXTENDED) != 0 ? 0xE000u : 0u);
                text = Translate(vk, scanCode);
            }

            _eventCount++;
            _detector.Process(new KeyEvent(vk, isDown, text));
            KeyProcessed?.Invoke();
        }
        catch (Exception ex)
        {
            // Keep input flowing; one bad event must not kill the capture.
            Log.Error("Fehler bei der Verarbeitung eines Tastenereignisses", ex);
        }
    }

    /// <summary>Characters the key produces in the foreground window's keyboard layout.</summary>
    internal string? Translate(int vk, uint scanCode)
    {
        Array.Clear(_keyState);
        // Our own thread's key state is not reliable for background input; use what the detector tracked.
        bool lShift = _detector.IsDown(Vk.LShift), rShift = _detector.IsDown(Vk.RShift);
        bool lCtrl = _detector.IsDown(Vk.LControl), rCtrl = _detector.IsDown(Vk.RControl);
        bool lAlt = _detector.IsDown(Vk.LMenu), rAlt = _detector.IsDown(Vk.RMenu);
        // AltGr = Ctrl + Alt for ToUnicodeEx; the fake Ctrl was dropped above.
        if (rAlt) lCtrl = true;
        SetDown(VK_SHIFT, lShift || rShift);
        SetDown(Vk.LShift, lShift);
        SetDown(Vk.RShift, rShift);
        SetDown(VK_CONTROL, lCtrl || rCtrl);
        SetDown(Vk.LControl, lCtrl);
        SetDown(Vk.RControl, rCtrl);
        SetDown(VK_MENU, lAlt || rAlt);
        SetDown(Vk.LMenu, lAlt);
        SetDown(Vk.RMenu, rAlt);
        _keyState[VK_CAPITAL] = (byte)(GetKeyState(VK_CAPITAL) & 1);

        uint thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var layout = GetKeyboardLayout(thread);
        int n = ToUnicodeEx((uint)vk, scanCode, _keyState, _chars, _chars.Length, TOUNICODE_NO_STATE_CHANGE, layout);
        // n < 0: dead key (^, ´) – ignored, the following character arrives on its own.
        return n > 0 ? new string(_chars, 0, n) : null;
    }

    void SetDown(int vk, bool down)
    {
        if (down) _keyState[vk] = 0x80;
    }
}
