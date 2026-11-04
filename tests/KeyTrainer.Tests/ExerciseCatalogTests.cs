using KeyTrainer.Core.Exercises;
using KeyTrainer.Core.Input;

namespace KeyTrainer.Tests;

public class ExerciseCatalogTests
{
    static readonly string CatalogPath = Path.Combine(AppContext.BaseDirectory, "exercises.json");

    [Fact]
    public void ShippedCatalog_IsValid()
    {
        var file = ExerciseCatalog.Load(CatalogPath);
        Assert.True(file.Exercises.Count >= 150, $"nur {file.Exercises.Count} Übungen");
    }

    [Fact]
    public void ShippedCatalog_CoversAllCategories()
    {
        var categories = ExerciseCatalog.Load(CatalogPath).Exercises.Select(e => e.Category).ToHashSet();
        string[] expected = ["Basis", "Fenster", "Text", "Explorer", "Admin", "Konsolen", "Server", "Kommandozeile",
                             "Terminal", "Fernwartung", "Linux", "Büro", "Browser", "Szenario"];
        Assert.Empty(expected.Except(categories));
    }

    [Fact]
    public void ShippedCatalog_HasNoCtrlAltDelete()
    {
        // Strg+Alt+Entf cannot be observed (secure attention sequence), see PLAN.md.
        var tokens = ExerciseCatalog.Load(CatalogPath).Exercises
            .SelectMany(e => e.Steps.Cast<TaskDefinition>().Prepend(e))
            .SelectMany(t => t.Expect?.AllSequences ?? [])
            .SelectMany(s => s);
        Assert.DoesNotContain(tokens, t => !t.StartsWith("text:") && Combo.Normalize(t) == "Strg+Alt+Entf");
    }

    [Fact]
    public void Validation_ReportsAllErrorsWithId()
    {
        const string json = """
            {
              "contexts": { "x": { } },
              "exercises": [
                { "id": "a", "category": "C", "prompt": "p", "hints": ["h"], "solution": "s", "mode": "keys",
                  "expect": { "sequence": ["Strg+Foo"] }, "context": "nope" },
                { "id": "a", "category": "C", "prompt": "p", "hints": [], "solution": "s", "mode": "result",
                  "verify": { "titleRegex": "(" } }
              ]
            }
            """;
        var ex = Assert.Throws<CatalogException>(() => ExerciseCatalog.Parse(json));
        Assert.Contains(ex.Errors, e => e.Contains("context \"x\""));
        Assert.Contains(ex.Errors, e => e.StartsWith("a:") && e.Contains("Foo"));
        Assert.Contains(ex.Errors, e => e.Contains("unbekannter context"));
        Assert.Contains(ex.Errors, e => e.Contains("id doppelt"));
        Assert.Contains(ex.Errors, e => e.Contains("hint"));
        Assert.Contains(ex.Errors, e => e.Contains("titleRegex ungültig"));
    }

    [Fact]
    public void UnknownMode_IsJsonError()
    {
        var ex = Assert.Throws<CatalogException>(() => ExerciseCatalog.Parse("""{ "exercises": [ { "mode": "magic" } ] }"""));
        Assert.Contains("JSON-Fehler", ex.Errors[0]);
    }

    [Theory]
    [InlineData("text:ipconfig /all", "IPCONFIG   /all", true)]
    [InlineData("text:ping *", "ping 8.8.8.8", true)]
    [InlineData("text:ping *", "pong 8.8.8.8", false)]
    [InlineData("text:ssh *@*", "ssh admin@server01", true)]
    [InlineData("text:ssh *@*", "ssh server01", false)]
    [InlineData("text:sudo !!", "sudo !!", true)]
    [InlineData("text:cd C:\\Win", "cd c:\\win", true)]
    public void TextPattern_Matches(string expected, string typed, bool match)
    {
        Assert.Equal(match, ExpectToken.Parse(expected).Matches(InputToken.FromText(typed)));
    }

    [Fact]
    public void ComboPattern_MatchesNormalForm_NotText()
    {
        var token = ExpectToken.Parse("shift + strg + t");
        Assert.True(token.Matches(InputToken.FromCombo(Combo.Parse("Strg+Shift+T"))));
        Assert.False(token.Matches(InputToken.FromText("Strg+Shift+T")));
    }
}
