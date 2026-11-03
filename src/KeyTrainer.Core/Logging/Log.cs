using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KeyTrainer.Core.Logging;

public enum LogLevel
{
    Info,
    Warn,
    Error,
}

/// <summary>
/// Minimal file logger: one file per day ("keytrainer-2026-11-03.log"), old files are deleted
/// after <see cref="RetentionDays"/>. Never throws. Must never receive key data or typed text.
/// </summary>
public static class Log
{
    public const int RetentionDays = 14;

    static readonly object Gate = new();
    static string? _directory;
    static Func<DateTime> _now = () => DateTime.Now;

    public static string? Directory => _directory;

    /// <summary>Path of today's log file, or null before <see cref="Init"/>.</summary>
    public static string? CurrentFile => _directory is null ? null : FileFor(_now());

    public static void Init(string directory, Func<DateTime>? clock = null)
    {
        lock (Gate)
        {
            _directory = directory;
            if (clock is not null) _now = clock;
        }
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            DeleteOld();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"KeyTrainer log init failed: {ex}");
        }
    }

    public static void Info(string message) => Write(LogLevel.Info, message, null);

    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warn, message, ex);

    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    public static void Write(LogLevel level, string message, Exception? ex)
    {
        var now = _now();
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(level.ToString().ToUpperInvariant().PadRight(5))
            .Append(" [").Append(Environment.CurrentManagedThreadId).Append("] ")
            .Append(message);
        if (ex is not null) line.AppendLine().Append(ex);
        var text = line.ToString();

        Debug.WriteLine(text);
        if (_directory is null) return;
        try
        {
            lock (Gate) File.AppendAllText(FileFor(now), text + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            Debug.WriteLine($"KeyTrainer log write failed: {writeEx.Message}");
        }
    }

    static string FileFor(DateTime day) =>
        Path.Combine(_directory!, $"keytrainer-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");

    static void DeleteOld()
    {
        var cutoff = _now().Date.AddDays(-RetentionDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory!, "keytrainer-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["keytrainer-".Length..];
            if (DateTime.TryParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day < cutoff)
                File.Delete(file);
        }
    }
}
