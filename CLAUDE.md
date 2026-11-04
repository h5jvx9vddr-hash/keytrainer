# KeyTrainer

Windows-Tastaturtrainer in C# / .NET 10 / WPF. Vollständiger Plan: PLAN.md (Abschnitt „Abweichungen“ am Ende gilt vorrangig).
Offene Punkte: Todo-App (`todo ls -p keytrainer`), nicht hier.

## Aufbau
- `src/KeyTrainer.Core` – plattformneutrale Logik (net10.0), unter Linux testbar.
- `src/KeyTrainer` – WPF-App (net10.0-windows), baut unter Linux per `EnableWindowsTargeting`, läuft nur unter Windows.
- `tests/KeyTrainer.Tests` – xUnit-Tests für Core.
- `.github/workflows/build.yml` – Windows-Build, Tests, Selbsttest, Defender-Scan, Release.
- `src/KeyTrainer/Assets/exercises.json` – Übungskatalog (in die exe eingebettet, Test `ExerciseCatalogTests` prüft ihn).

## Arbeitsweise
- Immer nur die aktuelle Phase aus PLAN.md umsetzen.
- Nach jeder Phase: `dotnet build` und `dotnet test`, Ergebnis zusammenfassen, manuelle Testliste ausgeben, auf Freigabe warten.
- Eingabeerfassung und Overlay sind nicht automatisch testbar: immer manuelle Testschritte nennen.

## Regeln
- Tastatureingaben niemals in Dateien oder Logs schreiben; getippter Text nur als „passt/passt nicht“.
- Logging über `KeyTrainer.Core.Logging.Log` (Tagesdatei in `%APPDATA%\KeyTrainer\logs`, 14 Tage). Jeder Fehler wird mit Exception und Win32-Fehlercode geloggt; unbehandelte Fehler fängt `App.xaml.cs`. Ins Log nur Zustände, Zähler, Fehler – nie Tasten.
- Eingabeerfassung per `WH_KEYBOARD_LL` (`Input/KeyboardSource.cs`) – nur beobachten, immer `CallNextHookEx`, nie blockieren. Aktiv nur während Training (nicht pausiert) oder Erkennungstest. Raw Input reicht nicht: Win+R & Co. kommen dort nie an.
- Trainer-Kürzel über RegisterHotKey, nie über die Eingabeerfassung; keine `Strg+Alt`-Kürzel (AltGr).
- Alle Win32-Aufrufe gesammelt in `src/KeyTrainer/Native/NativeMethods.cs`.
- Übungen nur in `Assets/exercises.json`, nie im Code.
- UI-Texte auf Deutsch, Code und Kommentare auf Englisch.
- Das Overlay darf nie den Fokus übernehmen (WS_EX_NOACTIVATE).
- Keine Packer, keine Obfuscation, kein Autostart (Virenscanner). Netzwerk nur im Update-Knopf (`Updates/Updater.cs`): HTTPS zur GitHub-API, nur auf Klick, SHA-256-Prüfung Pflicht.
- Jeder grüne Push auf `main` wird per CI ein Release `v0.2.<run>` – das installiert der Update-Knopf. Vorher läuft `KeyTrainer.exe --selftest` auf Windows.
