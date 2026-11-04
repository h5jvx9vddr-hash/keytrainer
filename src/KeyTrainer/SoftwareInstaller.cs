using System.ComponentModel;
using System.Diagnostics;
using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Logging;

namespace KeyTrainer;

/// <summary>
/// Installs software exercises need – only when the user clicks "Installieren". Uses the official
/// Windows tools in a visible window: winget, or DISM (with UAC prompt) for Windows features like RSAT.
/// </summary>
internal static class SoftwareInstaller
{
    /// <summary>Starts the installation and waits for it. Returns false if it could not start or failed.</summary>
    public static async Task<bool> InstallAsync(Software software)
    {
        ProcessStartInfo info;
        if (software.Winget is { } id)
        {
            // Store ids (9N…, XP…) come from the msstore source, everything else from winget's own.
            var source = id.Length == 12 && !id.Contains('.') ? "msstore" : "winget";
            info = new ProcessStartInfo("winget.exe",
                $"install --id {id} --exact --source {source} --accept-source-agreements --accept-package-agreements")
            { UseShellExecute = true };
        }
        else if (software.Capability is { } capability)
        {
            info = new ProcessStartInfo("dism.exe", $"/Online /Add-Capability /CapabilityName:{capability}")
            { UseShellExecute = true, Verb = "runas" };
        }
        else if (software.Url is { } url)
        {
            Log.Info($"Software: öffne {url} für {software.Name}");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        else
        {
            return false;
        }

        Log.Info($"Software: installiere {software.Name} mit {info.FileName} {info.Arguments}");
        try
        {
            using var process = Process.Start(info);
            if (process is null) return false;
            await process.WaitForExitAsync();
            // DISM: 3010 = success, restart required.
            bool ok = process.ExitCode is 0 or 3010;
            Log.Info($"Software: {software.Name} beendet mit Code {process.ExitCode}");
            return ok;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Info($"Software: Admin-Abfrage für {software.Name} abgebrochen");
            return false;
        }
        catch (Win32Exception ex)
        {
            Log.Error($"Software: {info.FileName} konnte nicht gestartet werden", ex);
            throw new InvalidOperationException(software.Winget is not null
                ? "winget wurde nicht gefunden. Installiere den „App-Installer“ aus dem Microsoft Store."
                : $"{info.FileName} konnte nicht gestartet werden: {ex.Message}", ex);
        }
    }

    public static string ActionLabel(Software software) =>
        software.Url is not null && software.Winget is null && software.Capability is null ? "Webseite öffnen" : "Installieren";
}
