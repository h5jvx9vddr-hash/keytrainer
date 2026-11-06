using System.Runtime.InteropServices;
using System.Windows.Threading;
using KeyTrainer.Core.Input;
using KeyTrainer.Core.Logging;
using KeyTrainer.Core.Training;
using KeyTrainer.Input;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer;

/// <summary>
/// "KeyTrainer.exe --selftest": exercises the native code paths without user interaction, for CI.
/// Exit code 0 = ok. Details go to the log.
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        try
        {
            var catalog = App.LoadCatalog();
            Log.Info($"Selbsttest: Katalog mit {catalog.Exercises.Count} Übungen geladen");

            var detector = new ComboDetector();
            using var source = new KeyboardSource(detector);
            source.Start();
            source.Stop();
            Log.Info("Selbsttest: Tastatur-Hook installiert und wieder entfernt");

            using (var hotkeys = new TrainerHotkeys())
                Log.Info("Selbsttest: Trainer-Kürzel registriert und wieder freigegeben");

            var foreground = ForegroundWindow.Snapshot();
            Log.Info($"Selbsttest: Vordergrundfenster gelesen ({(foreground is null ? "keins" : "Prozess " + foreground.Process)})");

            using (var mouse = new MouseSource())
            {
                mouse.Start();
                if (!mouse.IsRunning) failures.Add("Maus-Hook nicht installiert");
                mouse.Stop();
                Log.Info("Selbsttest: Maus-Hook installiert und wieder entfernt");
            }

            // Every exercise once against the real PC state: verifiers must not throw on Windows.
            var world = new WindowsWorld();
            var session = new TrainingSession(catalog.Exercises, catalog.Contexts, world);
            for (int i = 0; i < session.PoolSize; i++)
            {
                session.Next();
                for (int s = 0; s < session.StepCount && session.Step is not null; s++) session.Poll();
            }
            Log.Info($"Selbsttest: alle {session.PoolSize} Aufgaben einmal gestellt und geprüft");
            Log.Info($"Selbsttest: spoolsv.exe-Prozesse sichtbar: {world.ProcessIds("spoolsv.exe").Count}");

            // Many calls, so buffer overruns in the ToUnicodeEx interop corrupt the heap and crash here, not on the user's PC.
            for (int i = 0; i < 2000; i++)
                Expect(source.Translate('A', 0x1E), "a", "A ohne Shift", failures, log: i == 0);

            detector.Process(new KeyEvent(Vk.LShift, true));
            Expect(source.Translate('A', 0x1E), "A", "A mit Shift", failures, log: true);
            detector.Process(new KeyEvent(Vk.LShift, false));

            Expect(source.Translate(Vk.Return, 0x1C), "\r", "Enter", failures, log: true);

            CheckShellShortcut(failures);
        }
        catch (Exception ex)
        {
            Log.Error("Selbsttest: Ausnahme", ex);
            failures.Add(ex.Message);
        }

        if (failures.Count == 0)
        {
            Log.Info("Selbsttest: OK");
            return 0;
        }
        Log.Error("Selbsttest: FEHLGESCHLAGEN – " + string.Join("; ", failures));
        return 1;
    }

    /// <summary>
    /// Win+R is swallowed by the shell before Raw Input sees it (bug in 0.2.5). Send it for real and
    /// check the hook reports exactly "Win+R"; Esc closes the Run dialog again.
    /// </summary>
    static void CheckShellShortcut(List<string> failures)
    {
        var detector = new ComboDetector();
        var tokens = new List<string>();
        detector.TokenDetected += t => tokens.Add(t.ToString());
        using var source = new KeyboardSource(detector);
        source.Start();

        uint sent = SendKeys(Vk.LWin, 'R');
        Pump(TimeSpan.FromSeconds(1.5));
        SendKeys(Vk.Escape);
        Pump(TimeSpan.FromSeconds(0.5));
        source.Stop();

        if (sent == 0)
        {
            Log.Warn($"Selbsttest: SendInput nicht möglich (Win32-Fehler {Marshal.GetLastWin32Error()}), Win+R-Test übersprungen");
            return;
        }
        // Synthetic keys only – nothing typed by a person.
        Log.Info($"Selbsttest: Hook meldete [{string.Join(", ", tokens)}]");
        if (!tokens.Contains("Win+R")) failures.Add($"Win+R nicht erkannt, erhalten: [{string.Join(", ", tokens)}]");
        else if (tokens.Contains("Win")) failures.Add("Win+R wurde zusätzlich als einzelnes Win gemeldet");
        else Log.Info("Selbsttest: Win+R über den Hook erkannt");
    }

    /// <summary>Presses the keys in order and releases them in reverse order.</summary>
    static uint SendKeys(params int[] keys)
    {
        var inputs = keys.Select(k => Key(k, up: false)).Concat(keys.Reverse().Select(k => Key(k, up: true))).ToArray();
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    static INPUT Key(int vk, bool up) => new()
    {
        Type = INPUT_KEYBOARD,
        U = new InputUnion { Keyboard = new KEYBDINPUT { Vk = (ushort)vk, Flags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    /// <summary>Runs the message loop for a while so hook callbacks and queued work are delivered.</summary>
    static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    static void Expect(string? actual, string expected, string what, List<string> failures, bool log)
    {
        if (actual != expected) failures.Add($"{what}: erwartet \"{Escape(expected)}\", erhalten \"{Escape(actual)}\"");
        else if (log) Log.Info($"Selbsttest: {what} ok");
    }

    static string Escape(string? s) => s is null ? "(null)" : s.Replace("\r", "\\r");
}
