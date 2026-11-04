using System.Runtime.InteropServices;
using KeyTrainer.Core.Input;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer.E2E;

/// <summary>Presses keys and types text via SendInput, like a person would. Only used by the end-to-end test.</summary>
internal static class InputInjector
{
    static readonly HashSet<int> ExtendedKeys =
    [
        Vk.Left, Vk.Up, Vk.Right, Vk.Down, Vk.Home, Vk.End, Vk.Prior, Vk.Next, Vk.Insert, Vk.Delete,
        Vk.RMenu, Vk.RControl, Vk.LWin, Vk.RWin, Vk.Apps, Vk.Divide, Vk.NumLock,
    ];

    /// <summary>Presses a combo in any spelling ("Strg+Shift+T", "Win").</summary>
    public static void Combo(string text)
    {
        var combo = Core.Input.Combo.Parse(text);
        var keys = new List<int>();
        if (combo.Mods.HasFlag(Mods.Win)) keys.Add(Vk.LWin);
        if (combo.Mods.HasFlag(Mods.Ctrl)) keys.Add(Vk.LControl);
        if (combo.Mods.HasFlag(Mods.Alt)) keys.Add(Vk.LMenu);
        if (combo.Mods.HasFlag(Mods.Shift)) keys.Add(Vk.LShift);
        if (combo.Key.Length > 0)
            keys.Add(KeyNames.VkOf(combo.Key) ?? throw new FormatException($"Taste \"{combo.Key}\" kann nicht gesendet werden."));
        Send(keys);
    }

    /// <summary>Types text with real key strokes of the foreground keyboard layout (so the hook sees normal keys).</summary>
    public static void Text(string text)
    {
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        foreach (var ch in text)
        {
            short scan = VkKeyScanEx(ch, layout);
            if (scan == -1) throw new FormatException($"Zeichen \"{ch}\" ist auf diesem Tastaturlayout nicht tippbar.");
            int vk = scan & 0xFF;
            int shiftState = (scan >> 8) & 0xFF;
            var keys = new List<int>();
            // AltGr = Ctrl + right Alt; the detector treats it as text, not as a Strg+Alt combo.
            if ((shiftState & 6) == 6) keys.AddRange([Vk.LControl, Vk.RMenu]);
            else
            {
                if ((shiftState & 2) != 0) keys.Add(Vk.LControl);
                if ((shiftState & 4) != 0) keys.Add(Vk.LMenu);
            }
            if ((shiftState & 1) != 0) keys.Add(Vk.LShift);
            keys.Add(vk);
            Send(keys);
        }
    }

    public static void LeftClick(int x, int y)
    {
        SetCursorPos(x, y);
        var inputs = new[]
        {
            new INPUT { Type = INPUT_MOUSE, U = new InputUnion { Mouse = new MOUSEINPUT { Flags = MOUSEEVENTF_LEFTDOWN } } },
            new INPUT { Type = INPUT_MOUSE, U = new InputUnion { Mouse = new MOUSEINPUT { Flags = MOUSEEVENTF_LEFTUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Presses the keys in order and releases them in reverse order.</summary>
    static void Send(List<int> keys)
    {
        var inputs = keys.Select(k => Key(k, up: false)).Concat(Enumerable.Reverse(keys).Select(k => Key(k, up: true))).ToArray();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            throw new InvalidOperationException($"SendInput fehlgeschlagen (Win32-Fehler {Marshal.GetLastWin32Error()}).");
    }

    static INPUT Key(int vk, bool up)
    {
        uint flags = (up ? KEYEVENTF_KEYUP : 0) | (ExtendedKeys.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
        return new INPUT
        {
            Type = INPUT_KEYBOARD,
            U = new InputUnion { Keyboard = new KEYBDINPUT { Vk = (ushort)vk, Scan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC), Flags = flags } },
        };
    }
}
