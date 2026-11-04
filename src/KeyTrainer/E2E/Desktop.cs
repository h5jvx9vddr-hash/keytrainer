using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using KeyTrainer.Core.Logging;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer.E2E;

/// <summary>Window bookkeeping and screenshots for the end-to-end test.</summary>
internal static class Desktop
{
    // Shell windows that must never be closed.
    static readonly HashSet<string> Protected = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    // Programs the test may end hard if closing the window did not work (only if started during the exercise).
    static readonly HashSet<string> Killable = new(StringComparer.OrdinalIgnoreCase)
    {
        "notepad", "calculatorapp", "mmc", "taskmgr", "msinfo32", "mstsc", "systemsettings", "perfmon", "resmon",
    };

    public static HashSet<IntPtr> VisibleWindows()
    {
        var result = new HashSet<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd)) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Closes windows that appeared since <paramref name="before"/>, then ends leftover test programs.</summary>
    public static async Task CleanupAsync(HashSet<IntPtr> before, DateTime since)
    {
        uint ownPid = (uint)Environment.ProcessId;
        for (int round = 0; round < 3; round++)
        {
            var fresh = VisibleWindows().Except(before).Where(h =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                return pid != ownPid && !Protected.Contains(ClassName(h));
            }).ToList();
            if (fresh.Count == 0) break;
            foreach (var hwnd in fresh) PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(1200);
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (Killable.Contains(process.ProcessName) && process.StartTime >= since)
                {
                    Log.Info($"E2E: beende übrig gebliebenes {process.ProcessName}");
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // Access denied for elevated/system processes – not ours anyway.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public static string ClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        int n = GetClassName(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(n, 0));
    }

    /// <summary>Saves a PNG of all monitors; returns false if the capture failed.</summary>
    public static bool Screenshot(string path)
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN), h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        IntPtr screen = GetDC(IntPtr.Zero), mem = IntPtr.Zero, bitmap = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, w, h);
            var old = SelectObject(mem, bitmap);
            BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY | CAPTUREBLT);
            SelectObject(mem, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var file = File.Create(path);
            encoder.Save(file);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"E2E: Screenshot fehlgeschlagen ({path})", ex);
            return false;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
