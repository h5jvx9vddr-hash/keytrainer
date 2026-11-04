using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KeyTrainer.Core.Exercises;

public sealed class CatalogException(IReadOnlyList<string> errors)
    : Exception("Übungskatalog ungültig:\n" + string.Join("\n", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Loads and validates exercises.json. Every problem is reported at once, with the exercise id.</summary>
public static class ExerciseCatalog
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static ExerciseFile Load(string path) => Parse(File.ReadAllText(path));

    public static ExerciseFile Parse(string json)
    {
        ExerciseFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ExerciseFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CatalogException([$"JSON-Fehler (Zeile {ex.LineNumber + 1}): {ex.Message}"]);
        }
        if (file is null) throw new CatalogException(["Datei ist leer."]);

        var errors = Validate(file);
        if (errors.Count > 0) throw new CatalogException(errors);
        return file;
    }

    public static List<string> Validate(ExerciseFile file)
    {
        var errors = new List<string>();
        foreach (var (name, context) in file.Contexts)
            ValidateWindow($"context \"{name}\"", context, errors, requireAny: true);

        foreach (var (id, sw) in file.Software)
        {
            if (string.IsNullOrWhiteSpace(sw.Name)) errors.Add($"software \"{id}\": name fehlt");
            if (sw.AnyFile.Count == 0) errors.Add($"software \"{id}\": anyFile fehlt");
            if (!sw.CanInstall && sw.Note is null) errors.Add($"software \"{id}\": weder Installationsweg noch note");
            if (sw.Url is { } url && !url.StartsWith("https://", StringComparison.Ordinal)) errors.Add($"software \"{id}\": url muss https sein");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ex in file.Exercises)
        {
            var where = string.IsNullOrWhiteSpace(ex.Id) ? "(ohne id)" : ex.Id;
            if (string.IsNullOrWhiteSpace(ex.Id)) errors.Add($"{where}: id fehlt");
            else if (!ids.Add(ex.Id)) errors.Add($"{where}: id doppelt");
            if (string.IsNullOrWhiteSpace(ex.Category)) errors.Add($"{where}: category fehlt");
            if (ex.Level is < 1 or > 3) errors.Add($"{where}: level muss 1–3 sein");
            if (ex.Requires is { } req && !file.Software.ContainsKey(req)) errors.Add($"{where}: unbekannte Software \"{req}\"");

            if (ex.Mode == ExerciseMode.Scenario)
            {
                if (ex.Steps.Count < 2) errors.Add($"{where}: Szenario braucht mindestens 2 steps");
                if (string.IsNullOrWhiteSpace(ex.Prompt)) errors.Add($"{where}: prompt fehlt");
                for (int i = 0; i < ex.Steps.Count; i++)
                {
                    if (ex.Steps[i].Mode == ExerciseMode.Scenario) errors.Add($"{where} Schritt {i + 1}: Szenario im Szenario");
                    ValidateTask($"{where} Schritt {i + 1}", ex.Steps[i], file, errors);
                }
            }
            else
            {
                if (ex.Steps.Count > 0) errors.Add($"{where}: steps nur bei mode \"scenario\"");
                ValidateTask(where, ex, file, errors);
            }
        }
        foreach (var extra in file.ExtraCategories.Where(c => file.Exercises.All(e => e.Category != c)))
            errors.Add($"extraCategories: \"{extra}\" kommt in keiner Übung vor");
        return errors;
    }

    static void ValidateTask(string where, TaskDefinition task, ExerciseFile file, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(task.Prompt)) errors.Add($"{where}: prompt fehlt");
        if (string.IsNullOrWhiteSpace(task.Solution)) errors.Add($"{where}: solution fehlt");
        if (task.Hints.Count == 0 || task.Hints.Any(string.IsNullOrWhiteSpace)) errors.Add($"{where}: mindestens ein hint nötig");

        if (task.Mode == ExerciseMode.Keys && (task.Expect is null || task.Expect.Sequence.Count == 0))
            errors.Add($"{where}: mode keys braucht expect.sequence");
        if (task.Expect is not null)
        {
            foreach (var sequence in task.Expect.AllSequences)
            {
                if (sequence.Count == 0) errors.Add($"{where}: leere Sequenz");
                foreach (var token in sequence)
                {
                    try { ExpectToken.Parse(token); }
                    catch (FormatException fe) { errors.Add($"{where}: {fe.Message}"); }
                }
            }
        }

        if (task.Mode == ExerciseMode.Result && task.Verify is null) errors.Add($"{where}: mode result braucht verify");
        if (task.Verify is { } verify)
        {
            bool hasCheck = verify.HasWindowCondition || verify.PracticeTitle is not null || verify.SessionLock
                            || verify.ProcessRestarted is not null || verify.ProcessGone is not null
                            || verify.PathExists is not null || verify.PathMissing is not null;
            if (!hasCheck) errors.Add($"{where}: verify ohne Prüfung");
            if (verify.WindowState is { } state && !Enum.TryParse<Windows.WindowPlacement>(state, true, out _))
                errors.Add($"{where}: verify.windowState \"{state}\" unbekannt");
            ValidateWindow(where, verify, errors, requireAny: false);
        }
        if (task.Context is not null && task.Mode != ExerciseMode.Keys) errors.Add($"{where}: context nur bei mode keys");
        // Shown to the user; "*" is a pattern character, the solution needs a concrete example.
        if (task.Solution.Contains('*')) errors.Add($"{where}: solution enthält \"*\" – konkretes Beispiel angeben");

        if (task.Context is not null && !file.Contexts.ContainsKey(task.Context))
            errors.Add($"{where}: unbekannter context \"{task.Context}\"");

        foreach (var key in task.ExplainWrong.Keys)
        {
            try { Input.Combo.Parse(key); }
            catch (FormatException fe) { errors.Add($"{where}: explainWrong: {fe.Message}"); }
        }
    }

    static void ValidateWindow(string where, WindowMatch match, List<string> errors, bool requireAny)
    {
        if (requireAny && match.Process is null && match.WindowClass is null && match.TitleRegex is null)
            errors.Add($"{where}: braucht process, windowClass oder titleRegex");
        if (match.Process is { } process && process.Split('|').Any(p => p.Trim().Length == 0))
            errors.Add($"{where}: leerer Prozessname in \"{process}\"");
        if (match.TitleRegex is { } regex)
        {
            try { _ = new Regex(regex); }
            catch (ArgumentException ae) { errors.Add($"{where}: titleRegex ungültig: {ae.Message}"); }
        }
    }
}
