using System.Security.Cryptography;
using System.Text.Json;

namespace KeyTrainer.Core.Updates;

/// <summary>The latest release as reported by the GitHub API.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, Uri ExeUrl, Uri? ChecksumUrl, long ExeSize)
{
    public const string ExeAssetName = "KeyTrainer.exe";
    public const string ChecksumAssetName = "KeyTrainer.exe.sha256";

    /// <summary>Parses the JSON of <c>GET /repos/{owner}/{repo}/releases/latest</c>. Throws <see cref="FormatException"/>.</summary>
    public static ReleaseInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag)) throw new FormatException("Release ohne tag_name.");
        var version = ParseVersion(tag) ?? throw new FormatException($"Tag \"{tag}\" ist keine Version.");

        Uri? exe = null, checksum = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (url is null) continue;
                if (string.Equals(name, ExeAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    exe = new Uri(url);
                    size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                }
                else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    checksum = new Uri(url);
                }
            }
        }
        if (exe is null) throw new FormatException($"Release {tag} enthält keine {ExeAssetName}.");
        if (exe.Scheme != Uri.UriSchemeHttps) throw new FormatException("Download-Adresse ist nicht HTTPS.");
        return new ReleaseInfo(version, tag, exe, checksum, size);
    }

    /// <summary>"v0.2.17" or "0.2.17" → 0.2.17; null if not a version.</summary>
    public static Version? ParseVersion(string text)
    {
        var s = text.Trim().TrimStart('v', 'V');
        // Drop build metadata / pre-release suffixes ("0.2.1+abc", "0.2.1-beta").
        var cut = s.IndexOfAny(['+', '-']);
        if (cut >= 0) s = s[..cut];
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    /// <summary>Treats 0.2 and 0.2.0.0 as equal by filling missing parts with 0.</summary>
    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public bool IsNewerThan(Version current) => Version > Normalize(current);

    /// <summary>Reads the hash from a "sha256sum" style line ("&lt;hex&gt;  KeyTrainer.exe").</summary>
    public static string ParseChecksum(string text)
    {
        var hex = text.Trim().Split([' ', '\t', '*'], 2)[0].Trim();
        if (hex.Length != 64 || !hex.All(Uri.IsHexDigit)) throw new FormatException("Prüfsumme ungültig.");
        return hex.ToLowerInvariant();
    }

    public static string Sha256Of(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));
}
