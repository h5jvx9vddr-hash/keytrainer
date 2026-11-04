using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using KeyTrainer.Core.Logging;
using KeyTrainer.Core.Updates;

namespace KeyTrainer.Updates;

/// <summary>
/// Checks GitHub Releases for a newer KeyTrainer.exe and replaces the running exe on request.
/// Only network access of the app; runs solely when the user clicks the update button.
/// </summary>
internal static class Updater
{
    const string LatestReleaseUrl = "https://api.github.com/repos/h5jvx9vddr-hash/keytrainer/releases/latest";

    static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion { get; } =
        ReleaseInfo.Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0));

    static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad unbekannt.");

    static string OldPath => ExePath + ".old";

    static string NewPath => ExePath + ".new";

    /// <summary>Returns the newer release, or null if this version is current.</summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        Log.Info($"Update-Prüfung: aktuelle Version {CurrentVersion}");
        using var response = await Http.GetAsync(LatestReleaseUrl, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Log.Warn("Update-Prüfung: noch kein Release vorhanden");
            return null;
        }
        response.EnsureSuccessStatusCode();
        var info = ReleaseInfo.Parse(await response.Content.ReadAsStringAsync(ct));
        Log.Info($"Update-Prüfung: neueste Version {info.Version} ({info.Tag})");
        return info.IsNewerThan(CurrentVersion) ? info : null;
    }

    /// <summary>Downloads, verifies and swaps in the new exe, then starts it. The caller shuts down afterwards.</summary>
    public static async Task InstallAsync(ReleaseInfo info, IProgress<double> progress, CancellationToken ct = default)
    {
        if (info.ChecksumUrl is null) throw new InvalidOperationException($"Release {info.Tag} hat keine Prüfsumme – Update abgebrochen.");

        Log.Info($"Update: lade {info.ExeUrl} nach {NewPath}");
        try
        {
            await DownloadAsync(info, progress, ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"Der Ordner {Path.GetDirectoryName(ExePath)} ist nicht beschreibbar. Lege die KeyTrainer.exe in einen eigenen Ordner, z. B. unter Dokumente.", ex);
        }

        var expected = ReleaseInfo.ParseChecksum(await Http.GetStringAsync(info.ChecksumUrl, ct));
        string actual;
        await using (var file = File.OpenRead(NewPath))
            actual = ReleaseInfo.Sha256Of(file);
        if (actual != expected)
        {
            TryDelete(NewPath);
            throw new InvalidOperationException($"Prüfsumme stimmt nicht (erwartet {expected}, erhalten {actual}) – Update abgebrochen.");
        }
        Log.Info($"Update: Prüfsumme ok ({actual})");

        // A running exe cannot be overwritten, but it can be renamed.
        TryDelete(OldPath);
        File.Move(ExePath, OldPath);
        try
        {
            File.Move(NewPath, ExePath);
        }
        catch
        {
            File.Move(OldPath, ExePath);
            throw;
        }

        Log.Info($"Update: {CurrentVersion} → {info.Version} installiert, starte neu");
        Process.Start(new ProcessStartInfo(ExePath, $"--updated-from {CurrentVersion}") { UseShellExecute = false });
    }

    /// <summary>Removes the previous exe left by an update; the old process may need a moment to exit.</summary>
    public static async Task CleanupAsync()
    {
        for (int attempt = 0; attempt < 20 && File.Exists(OldPath); attempt++)
        {
            try
            {
                File.Delete(OldPath);
                Log.Info("Update: alte Version entfernt");
                return;
            }
            catch (IOException) { await Task.Delay(500); }
            catch (UnauthorizedAccessException) { await Task.Delay(500); }
        }
        if (File.Exists(OldPath)) Log.Warn($"Update: {OldPath} konnte nicht gelöscht werden");
        TryDelete(NewPath);
    }

    static async Task DownloadAsync(ReleaseInfo info, IProgress<double> progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(info.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? info.ExeSize;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(NewPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress.Report((double)done / total);
        }
        Log.Info($"Update: {done / 1048576} MB geladen");
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"Konnte {path} nicht löschen", ex);
        }
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"KeyTrainer/{typeof(Updater).Assembly.GetName().Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
