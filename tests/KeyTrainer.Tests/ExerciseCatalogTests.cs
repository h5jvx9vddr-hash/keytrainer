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
        Assert.True(file.Exercises.Count >= 120, $"nur {file.Exercises.Count} Übungen");
        var core = file.Exercises.Count(e => !file.ExtraCategories.Contains(e.Category));
        Assert.True(core >= 60, $"nur {core} Übungen im Kern-Training");
    }

    [Fact]
    public void ShippedCatalog_CoversAllCategories()
    {
        var categories = ExerciseCatalog.Load(CatalogPath).Exercises.Select(e => e.Category).ToHashSet();
        string[] expected = ["Start & Programme", "Fenster", "Text", "Explorer", "Browser", "Windows-Werkzeuge",
                             "Kommandozeile", "Terminal", "Linux", "Fernwartung", "Büro", "Server"];
        Assert.Empty(expected.Except(categories));
    }

    [Fact]
    public void ShippedCatalog_HasNoCtrlAltDelete()
    {
        // Strg+Alt+Entf cannot be observed (secure attention sequence), see PLAN.md.
        var tokens = ExerciseCatalog.Load(CatalogPath).Exercises
            .SelectMany(e => e.AllSteps)
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
              "software": { "sw": { "name": "SW", "anyFile": ["a"] } },
              "exercises": [
                { "id": "a", "category": "C", "prompt": "p", "hints": ["h"], "solution": "s", "mode": "keys",
                  "expect": { "sequence": ["Strg+Foo"] }, "context": "nope" },
                { "id": "a", "category": "C", "prompt": "p", "hints": [], "solution": "ping *", "mode": "result",
                  "verify": { "titleRegex": "(", "windowState": "huge" }, "requires": "unknown" }
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
        Assert.Contains(ex.Errors, e => e.Contains("windowState"));
        Assert.Contains(ex.Errors, e => e.Contains("konkretes Beispiel"));
        Assert.Contains(ex.Errors, e => e.Contains("unbekannte Software"));
        Assert.Contains(ex.Errors, e => e.Contains("software \"sw\""));
    }

    [Fact]
    public void ShippedCatalog_SoftwareIsReferencedAndInstallable()
    {
        var file = ExerciseCatalog.Load(CatalogPath);
        var used = file.Exercises.Select(e => e.Requires).OfType<string>().ToHashSet();
        Assert.Empty(file.Software.Keys.Except(used));
        Assert.All(file.Software.Values, sw => Assert.True(sw.CanInstall || sw.Note is not null, sw.Name));
    }

    [Fact]
    public void ShippedCatalog_AutoActionsAreSendable()
    {
        var file = ExerciseCatalog.Load(CatalogPath);
        var actions = file.Exercises.SelectMany(e => e.AllSteps).SelectMany(s => s.Auto)
            .Where(a => !a.StartsWith("wait:") && !a.StartsWith("text:"));
        foreach (var action in actions)
        {
            var combo = Combo.Parse(action);
            Assert.True(combo.Key.Length == 0 || KeyNames.VkOf(combo.Key) is not null, $"Taste in \"{action}\" nicht sendbar");
        }
    }

    [Fact]
    public void ShippedCatalog_CoreStepsHaveAutoActions()
    {
        // Steps the end-to-end test cannot play on its own must stay rare.
        var file = ExerciseCatalog.Load(CatalogPath);
        var missing = file.Exercises.Where(e => !e.NoAuto && !e.Elevated)
            .SelectMany(e => e.AllSteps.Select((s, i) => (e.Id, i, s)))
            .Where(x => x.s.Auto.Count == 0).Select(x => $"{x.Id}#{x.i}").ToList();
        Assert.True(missing.Count <= 3, string.Join(", ", missing));
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
