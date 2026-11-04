using System.Runtime.InteropServices;
using System.Windows.Interop;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Logging;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer.Input;

/// <summary>
/// Observes keyboard input system-wide via Raw Input (RIDEV_INPUTSINK) on a hidden
/// message-only window. Read-only: keys are never blocked, injected or written anywhere.
/// Registration ends with Stop() or automatically when the process exits.
/// </summary>
internal sealed class RawKeyboardSource : IDisposable
{
    static readonly IntPtr HWND_MESSAGE = new(-3);
    static readonly uint HeaderSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

    readonly HwndSource _window;
    readonly IntPtr _buffer = Marshal.AllocHGlobal(256);
    readonly byte[] _keyState = new byte[256];
    readonly char[] _chars = new char[8];
    readonly ComboDetector _detector;
    int _readErrors;
    int _eventCount;

    public bool IsRunning { get; private set; }

    /// <summary>Raised after each processed key event (e.g. to restart a text idle timer).</summary>
    public event Action? KeyProcessed;

    public RawKeyboardSource(ComboDetector detector)
    {
        _detector = detector;
        _window = new HwndSource(new HwndSourceParameters("KeyTrainerInput")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);
    }

    public void Start()
    {
        if (IsRunning) return;
        _detector.Reset();
        _readErrors = 0;
        _eventCount = 0;
        Register(RIDEV_INPUTSINK, _window.Handle);
        IsRunning = true;
        Log.Info($"Erfassung gestartet (Raw Input, Fenster 0x{_window.Handle.ToInt64():X})");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        Register(RIDEV_REMOVE, IntPtr.Zero);
        IsRunning = false;
        _detector.Flush();
        _detector.Reset();
        // Only counts, never key data.
        Log.Info($"Erfassung gestoppt ({_eventCount} Tastenereignisse, {_readErrors} Lesefehler)");
    }

    public void Dispose()
    {
        if (IsRunning) Stop();
        _window.RemoveHook(WndProc);
        _window.Dispose();
        Marshal.FreeHGlobal(_buffer);
    }

    static void Register(uint flags, IntPtr target)
    {
        var device = new RAWINPUTDEVICE
        {
            UsagePage = HID_USAGE_PAGE_GENERIC,
            Usage = HID_USAGE_GENERIC_KEYBOARD,
            Flags = flags,
            Target = target,
        };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            var error = Marshal.GetLastWin32Error();
            var ex = new InvalidOperationException(
                $"Tastatur-Erfassung konnte nicht {(flags == RIDEV_REMOVE ? "beendet" : "gestartet")} werden (Win32-Fehler {error}).");
            Log.Error($"RegisterRawInputDevices flags=0x{flags:X} fehlgeschlagen, Win32-Fehler {error}", ex);
            throw ex;
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT && IsRunning)
        {
            try
            {
                if (TryRead(lParam, out var kb) && ToKeyEvent(kb) is { } keyEvent)
                {
                    _eventCount++;
                    _detector.Process(keyEvent);
                    KeyProcessed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                // Keep input flowing; one bad event must not kill the capture.
                Log.Error("Fehler bei der Verarbeitung eines Tastenereignisses", ex);
            }
        }
        // WM_INPUT must still reach DefWindowProc for cleanup, so never mark it handled.
        return IntPtr.Zero;
    }

    bool TryRead(IntPtr lParam, out RAWKEYBOARD kb)
    {
        kb = default;
        uint size = 256;
        uint read = GetRawInputData(lParam, RID_INPUT, _buffer, ref size, HeaderSize);
        if (read == uint.MaxValue || read < HeaderSize)
        {
            // Log the first failure and then every 100th to avoid flooding.
            if (_readErrors++ % 100 == 0)
                Log.Warn($"GetRawInputData fehlgeschlagen (Rückgabe {read}, Win32-Fehler {Marshal.GetLastWin32Error()}, bisher {_readErrors}x)");
            return false;
        }
        var header = Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer);
        if (header.Type != RIM_TYPEKEYBOARD) return false;
        kb = Marshal.PtrToStructure<RAWKEYBOARD>(_buffer + (int)HeaderSize);
        return true;
    }

    KeyEvent? ToKeyEvent(RAWKEYBOARD kb)
    {
        int vk = kb.VKey;
        // 0xFF marks fake keys that are part of escape sequences.
        if (vk is 0 or 0xFF) return null;

        bool e0 = (kb.Flags & RI_KEY_E0) != 0;
        bool isDown = (kb.Flags & RI_KEY_BREAK) == 0;

        vk = vk switch
        {
            VK_SHIFT => kb.MakeCode == 0x36 ? Vk.RShift : Vk.LShift,
            VK_CONTROL => e0 ? Vk.RControl : Vk.LControl,
            VK_MENU => e0 ? Vk.RMenu : Vk.LMenu,
            _ => vk,
        };

        string? text = null;
        if (isDown && !ComboDetector.IsModifier(vk))
        {
            uint scanCode = kb.MakeCode | (e0 ? 0xE000u : 0u);
            text = Translate(vk, scanCode);
        }
        return new KeyEvent(vk, isDown, text);
    }

    /// <summary>Characters the key produces in the foreground window's keyboard layout.</summary>
    internal string? Translate(int vk, uint scanCode)
    {
        Array.Clear(_keyState);
        // Our own thread's key state is not reliable for background input; use what the detector tracked.
        bool shift = _detector.IsDown(Vk.LShift) || _detector.IsDown(Vk.RShift);
        bool ctrl = _detector.IsDown(Vk.LControl) || _detector.IsDown(Vk.RControl);
        bool alt = _detector.IsDown(Vk.LMenu) || _detector.IsDown(Vk.RMenu);
        SetDown(VK_SHIFT, shift);
        SetDown(Vk.LShift, _detector.IsDown(Vk.LShift));
        SetDown(Vk.RShift, _detector.IsDown(Vk.RShift));
        SetDown(VK_CONTROL, ctrl);
        SetDown(Vk.LControl, _detector.IsDown(Vk.LControl));
        SetDown(Vk.RControl, _detector.IsDown(Vk.RControl));
        SetDown(VK_MENU, alt);
        SetDown(Vk.LMenu, _detector.IsDown(Vk.LMenu));
        SetDown(Vk.RMenu, _detector.IsDown(Vk.RMenu));
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
