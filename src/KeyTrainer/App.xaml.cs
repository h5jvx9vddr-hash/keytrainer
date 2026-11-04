using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Logging;
using KeyTrainer.Updates;
using static KeyTrainer.Native.NativeMethods;

namespace KeyTrainer;

public partial class App : Application
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyTrainer");

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Init(LogDirectory);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        LogEnvironment();
        base.OnStartup(e);

        if (e.Args.Contains("--selftest"))
        {
            Shutdown(SelfTest.Run());
            return;
        }

        var updatedFrom = Array.IndexOf(e.Args, "--updated-from");
        if (updatedFrom >= 0 && updatedFrom + 1 < e.Args.Length)
            Log.Info($"Nach Update gestartet (vorher {e.Args[updatedFrom + 1]})");
        _ = Updater.CleanupAsync();

        new MainWindow().Show();
    }

    /// <summary>Loads the exercise catalog embedded in the exe.</summary>
    public static ExerciseFile LoadCatalog()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("KeyTrainer.exercises.json")
                           ?? throw new InvalidOperationException("exercises.json fehlt in der exe.");
        using var reader = new StreamReader(stream);
        return ExerciseCatalog.Parse(reader.ReadToEnd());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info($"Beendet (Exit-Code {e.ApplicationExitCode})");
        base.OnExit(e);
    }

    static void LogEnvironment()
    {
        var version = Updater.CurrentVersion;
        bool elevated;
        using (var identity = WindowsIdentity.GetCurrent())
            elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

        Log.Info($"KeyTrainer {version} gestartet, PID {Environment.ProcessId}");
        Log.Info($"OS {Environment.OSVersion.Version} ({RuntimeInformation.OSDescription}), " +
                 $".NET {Environment.Version}, {RuntimeInformation.ProcessArchitecture}, Admin={elevated}");
        Log.Info($"Tastaturlayout 0x{GetKeyboardLayout(0).ToInt64():X8}, Kultur {System.Globalization.CultureInfo.CurrentCulture.Name}, " +
                 $"Monitore {GetSystemMetrics(SM_CMONITORS)}, Pfad {Environment.ProcessPath}");
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unbehandelter Fehler im UI-Thread", e.Exception);
        e.Handled = true;
        ShowError(e.Exception);
    }

    static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log.Error($"Unbehandelter Fehler (Programmende: {e.IsTerminating})", e.ExceptionObject as Exception);

    static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Unbeobachteter Task-Fehler", e.Exception);
        e.SetObserved();
    }

    static void ShowError(Exception ex)
    {
        var result = MessageBox.Show(
            $"Es ist ein Fehler aufgetreten:\n\n{ex.Message}\n\nDetails stehen im Log:\n{Log.CurrentFile}\n\nLog-Ordner öffnen?",
            "KeyTrainer – Fehler", MessageBoxButton.YesNo, MessageBoxImage.Error);
        if (result == MessageBoxResult.Yes) OpenLogFolder();
    }

    public static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LogDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Log-Ordner konnte nicht geöffnet werden", ex);
        }
    }
}
