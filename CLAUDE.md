# KeyTrainer

Windows-Tastaturtrainer in C# / .NET 10 / WPF. Vollständiger Plan: PLAN.md (Abschnitt „Abweichungen“ am Ende gilt vorrangig).
Offene Punkte: Todo-App (`todo ls -p keytrainer`), nicht hier.

## Aufbau
- `src/KeyTrainer.Core` – plattformneutrale Logik (net10.0), unter Linux testbar.
- `src/KeyTrainer` – WPF-App (net10.0-windows), baut unter Linux per `EnableWindowsTargeting`, läuft nur unter Windows.
- `tests/KeyTrainer.Tests` – xUnit-Tests für Core.
- `.github/workflows/build.yml` – Windows-Build, Tests, Single-File-.exe als Artifact.

## Arbeitsweise
- Immer nur die aktuelle Phase aus PLAN.md umsetzen.
- Nach jeder Phase: `dotnet build` und `dotnet test`, Ergebnis zusammenfassen, manuelle Testliste ausgeben, auf Freigabe warten.
- Eingabeerfassung und Overlay sind nicht automatisch testbar: immer manuelle Testschritte nennen.

## Regeln
- Tastatureingaben niemals in Dateien oder Logs schreiben; getippter Text nur als „passt/passt nicht“.
- Logging über `KeyTrainer.Core.Logging.Log` (Tagesdatei in `%APPDATA%\KeyTrainer\logs`, 14 Tage). Jeder Fehler wird mit Exception und Win32-Fehlercode geloggt; unbehandelte Fehler fängt `App.xaml.cs`. Ins Log nur Zustände, Zähler, Fehler – nie Tasten.
- Eingabeerfassung (Raw Input) nur während einer laufenden Übung aktiv.
- Trainer-Kürzel über RegisterHotKey, nie über die Eingabeerfassung; keine `Strg+Alt`-Kürzel (AltGr).
- Alle Win32-Aufrufe gesammelt in `src/KeyTrainer/Native/NativeMethods.cs`.
- Übungen nur in `Assets/exercises.json`, nie im Code.
- UI-Texte auf Deutsch, Code und Kommentare auf Englisch.
- Das Overlay darf nie den Fokus übernehmen (WS_EX_NOACTIVATE).
- Keine Packer, keine Obfuscation, kein Netzwerk, kein Autostart (Virenscanner).
