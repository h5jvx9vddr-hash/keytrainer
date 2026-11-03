using KeyTrainer.Core.Logging;

namespace KeyTrainer.Tests;

// Log is static; keep its tests in one non-parallel collection.
[Collection("Log")]
public class LogTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "kt-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void WritesDailyFile_WithLevelAndException()
    {
        var now = new DateTime(2026, 11, 3, 14, 5, 6);
        Log.Init(_dir, () => now);

        Log.Info("Start");
        Log.Error("Kaputt", new InvalidOperationException("boom"));

        var text = File.ReadAllText(Path.Combine(_dir, "keytrainer-2026-11-03.log"));
        Assert.Contains("2026-11-03 14:05:06.000 INFO ", text);
        Assert.Contains("ERROR", text);
        Assert.Contains("Kaputt", text);
        Assert.Contains("System.InvalidOperationException: boom", text);
    }

    [Fact]
    public void Init_DeletesFilesOlderThanRetention()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "keytrainer-2026-10-01.log");
        var recent = Path.Combine(_dir, "keytrainer-2026-10-25.log");
        var foreign = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(old, "x");
        File.WriteAllText(recent, "x");
        File.WriteAllText(foreign, "x");

        Log.Init(_dir, () => new DateTime(2026, 11, 3));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public void UnwritableDirectory_DoesNotThrow()
    {
        var file = Path.Combine(Path.GetTempPath(), "kt-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "not a directory");
        try
        {
            Log.Init(file);
            Log.Error("still fine");
        }
        finally
        {
            File.Delete(file);
        }
    }
}
