using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using KeyTrainer.Core.Logging;
using KeyTrainer.Core.Training;
using KeyTrainer.Core.Windows;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer.Input;

/// <summary>Reads the real PC state for result checks. Never changes anything.</summary>
internal sealed class WindowsWorld : IWorld
{
    public DateTime? LastSessionLock { get; set; }

    public WindowSnapshot? Foreground() => ForegroundWindow.Snapshot();

    public int WindowCount(string process)
    {
        var pids = Processes(process).Select(p => (uint)p.Id).ToHashSet();
        int count = 0;
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pids.Contains(pid)) count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }

    public IReadOnlySet<int> ProcessIds(string name) => Processes(name).Select(p => p.Id).ToHashSet();

    public bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    static Process[] Processes(string name) => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name));

    /// <summary>Maximized, or snapped to the left/right half of its monitor's work area.</summary>
    public static WindowPlacement PlacementOf(IntPtr hwnd)
    {
        if (IsZoomed(hwnd)) return WindowPlacement.Maximized;
        // Visible frame without the invisible resize borders Windows 10/11 add.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) != 0) return WindowPlacement.Normal;
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info)) return WindowPlacement.Normal;

        var w = info.Work;
        int workWidth = w.Right - w.Left, workHeight = w.Bottom - w.Top;
        int tolerance = Math.Max(16, workWidth / 50);
        bool fullHeight = Math.Abs(r.Top - w.Top) <= tolerance && Math.Abs(r.Bottom - w.Bottom) <= tolerance;
        bool halfWidth = Math.Abs((r.Right - r.Left) - workWidth / 2) <= tolerance;
        if (!fullHeight || !halfWidth) return WindowPlacement.Normal;
        if (Math.Abs(r.Left - w.Left) <= tolerance) return WindowPlacement.LeftHalf;
        if (Math.Abs(r.Right - w.Right) <= tolerance) return WindowPlacement.RightHalf;
        return WindowPlacement.Normal;
    }
}
