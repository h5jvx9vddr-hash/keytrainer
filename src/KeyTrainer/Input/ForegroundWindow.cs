using System.IO;
using KeyTrainer.Core.Windows;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer.Input;

internal static class ForegroundWindow
{
    /// <summary>Process, class and title of the foreground window; null if there is none.</summary>
    public static WindowSnapshot? Snapshot()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        var cls = ClassOf(hwnd);
        var title = TitleOf(hwnd);
        var owner = hwnd;
        // Store apps (Rechner, Einstellungen) are hosted by ApplicationFrameHost; the real process owns the core window.
        if (cls == "ApplicationFrameWindow")
        {
            var core = FindWindowEx(hwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
            if (core != IntPtr.Zero) owner = core;
        }
        return new WindowSnapshot(ProcessNameOf(owner), cls, title);
    }

    static string ClassOf(IntPtr hwnd)
    {
        var buffer = new char[256];
        int n = GetClassName(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(n, 0));
    }

    static string TitleOf(IntPtr hwnd)
    {
        var buffer = new char[512];
        int n = GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(n, 0));
    }

    static string ProcessNameOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        // Limited query rights also work for elevated processes.
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return "";
        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? Path.GetFileName(new string(buffer, 0, (int)size)) : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
