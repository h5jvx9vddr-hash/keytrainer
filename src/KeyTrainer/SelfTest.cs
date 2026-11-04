using KeyTrainer.Core.Input;
using KeyTrainer.Core.Logging;
using KeyTrainer.Input;

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
            using var source = new RawKeyboardSource(detector);
            source.Start();
            source.Stop();
            Log.Info("Selbsttest: Raw Input registriert und wieder entfernt");

            // Many calls, so buffer overruns in the ToUnicodeEx interop corrupt the heap and crash here, not on the user's PC.
            for (int i = 0; i < 2000; i++)
                Expect(source.Translate('A', 0x1E), "a", "A ohne Shift", failures, log: i == 0);

            detector.Process(new KeyEvent(Vk.LShift, true));
            Expect(source.Translate('A', 0x1E), "A", "A mit Shift", failures, log: true);
            detector.Process(new KeyEvent(Vk.LShift, false));

            Expect(source.Translate(Vk.Return, 0x1C), "\r", "Enter", failures, log: true);
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

    static void Expect(string? actual, string expected, string what, List<string> failures, bool log)
    {
        if (actual != expected) failures.Add($"{what}: erwartet \"{Escape(expected)}\", erhalten \"{Escape(actual)}\"");
        else if (log) Log.Info($"Selbsttest: {what} ok");
    }

    static string Escape(string? s) => s is null ? "(null)" : s.Replace("\r", "\\r");
}
