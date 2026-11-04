using System.Text;
using KeyTrainer.Core.Updates;

namespace KeyTrainer.Tests;

public class ReleaseInfoTests
{
    const string Json = """
        {
          "tag_name": "v0.2.17",
          "assets": [
            { "name": "KeyTrainer.exe", "size": 140000000,
              "browser_download_url": "https://github.com/o/r/releases/download/v0.2.17/KeyTrainer.exe" },
            { "name": "KeyTrainer.exe.sha256", "size": 80,
              "browser_download_url": "https://github.com/o/r/releases/download/v0.2.17/KeyTrainer.exe.sha256" }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsVersionAndAssets()
    {
        var info = ReleaseInfo.Parse(Json);
        Assert.Equal(new Version(0, 2, 17, 0), info.Version);
        Assert.EndsWith("/KeyTrainer.exe", info.ExeUrl.AbsoluteUri);
        Assert.NotNull(info.ChecksumUrl);
        Assert.Equal(140000000, info.ExeSize);
    }

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("0.2.16", true)]
    [InlineData("0.2.17", false)]
    [InlineData("0.2.17.0", false)]
    [InlineData("0.3.0", false)]
    public void IsNewerThan(string current, bool expected)
    {
        Assert.Equal(expected, ReleaseInfo.Parse(Json).IsNewerThan(Version.Parse(current)));
    }

    [Fact]
    public void Parse_RejectsReleaseWithoutExe()
    {
        Assert.Throws<FormatException>(() => ReleaseInfo.Parse("""{ "tag_name": "v1.0.0", "assets": [] }"""));
    }

    [Fact]
    public void Parse_RejectsNonHttps()
    {
        var json = Json.Replace("https://github.com/o/r/releases/download/v0.2.17/KeyTrainer.exe\"", "http://evil/KeyTrainer.exe\"");
        Assert.Throws<FormatException>(() => ReleaseInfo.Parse(json));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3.0")]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("v0.2.5-beta", "0.2.5.0")]
    [InlineData("0.2.5+abc", "0.2.5.0")]
    public void ParseVersion(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), ReleaseInfo.ParseVersion(tag));
    }

    [Fact]
    public void Checksum_ParsesSha256SumFormat_AndMatchesHash()
    {
        var data = new MemoryStream(Encoding.UTF8.GetBytes("hallo"));
        var hash = ReleaseInfo.Sha256Of(data);
        Assert.Equal("d3751d33f9cd5049c4af2b462735457e4d3baf130bcbb87f389e349fbaeb20b9", hash);
        Assert.Equal(hash, ReleaseInfo.ParseChecksum(hash.ToUpperInvariant() + "  KeyTrainer.exe\n"));
        Assert.Throws<FormatException>(() => ReleaseInfo.ParseChecksum("kaputt"));
    }
}
