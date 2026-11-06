# Übergabe: Claude Code in der Windows-VM

Diese Datei ist für die Claude-Code-Sitzung, die direkt in der Windows-11-VM läuft. Dort kann sie die App bauen, starten, automatisch durchspielen und Fehler beheben – das geht auf dem Linux-Server nicht.

## Worum es geht

KeyTrainer bringt einer Person bei, **Windows ganz ohne Maus zu steuern**. Aufgaben sind Ziele („Öffne den Ordner Downloads“), geprüft wird das Ergebnis am PC-Zustand; jeder Tastatur-Weg zählt, Mausbenutzung lässt die Aufgabe nicht zählen. Eine Leiste oben mittig zeigt Aufgabe, Schritt, Tipps und Rückmeldung.

Vorgaben des Nutzers (verbindlich):

- **Sehr intuitiv, nicht umständlich.** Ein Start-Knopf (Enter), sinnvolle Voreinstellungen, alles andere eingeklappt.
- Aufgaben müssen **von jedem Ausgangszustand** lösbar sein – nie „die markierte Datei“ o. Ä. voraussetzen.
- Fremde Tasten sind **nie Fehler**, nur neutrale Anzeige. Gleichwertige Wege (z. B. `Strg+1` statt `Strg+Tab`) zählen.
- **Alles loggen**, was bei Fehlern hilft – aber **nie Tasten oder getippten Text** (siehe `CLAUDE.md`).
- **Nicht als Virus erkannt werden**: kein Packer, keine Verschleierung, Netzwerk nur im Update-Knopf.
- Antworten an den Nutzer **auf Deutsch**. Er ist oft nicht am Rechner – Ergebnisse knapp und konkret melden.

Regeln für den Code: `CLAUDE.md`. Plan und Entscheidungsverlauf: `PLAN.md` (Abschnitt „Abweichungen“ am Ende gilt vorrangig).

## Einrichten (einmalig, PowerShell)

```powershell
winget install Git.Git
winget install Microsoft.DotNet.SDK.10
winget install GitHub.cli
irm https://claude.ai/install.ps1 | iex
# Terminal neu öffnen
gh auth login
gh repo clone h5jvx9vddr-hash/keytrainer C:\dev\keytrainer
cd C:\dev\keytrainer
git config user.name KeyTrainer
git config user.email "$(gh api user --jq .id)+$(gh api user --jq .login)@users.noreply.github.com"
claude
```

Vorher in der VM: **Snapshot** machen, **Edge einmal starten und die Ersteinrichtung abschließen** (sonst stört der Willkommensbildschirm die Browser-Tests).

## Arbeitsablauf

```powershell
dotnet test tests\KeyTrainer.Tests                      # Kernlogik + Katalogprüfung
dotnet publish src\KeyTrainer -c Release -o publish      # publish\KeyTrainer.exe
Start-Process publish\KeyTrainer.exe --selftest -Wait    # Hook, Hotkeys, Win+R per SendInput …
Start-Process publish\KeyTrainer.exe '--e2e' -Wait       # alle Kern-Aufgaben automatisch durchspielen
Start-Process publish\KeyTrainer.exe '--e2e explorer-ordner browser-' -Wait   # nur bestimmte (ID-Präfixe)
Start-Process publish\KeyTrainer.exe '--e2e --extras' -Wait                   # inkl. Kommandozeile, Terminal …
Start-Process publish\KeyTrainer.exe '--e2e --elevated' -Wait                 # inkl. Aufgaben, deren Ziel erhöht startet
Start-Process publish\KeyTrainer.exe '--e2e --elevated' -Verb RunAs -Wait     # als Administrator: auch Tasten in MMC und Task-Manager
```

- **Bericht:** `%APPDATA%\KeyTrainer\e2e\report.md`, pro Fehler ein Screenshot daneben (mit dem Read-Tool ansehen). Der Bericht nennt erwartete Prüfung, tatsächliches Vordergrundfenster (Prozess, Klasse, Titel, Platzierung) und die erkannten Eingaben.
- **Log:** `%APPDATA%\KeyTrainer\logs\keytrainer-JJJJ-MM-TT.log`.
- **Während `--e2e` läuft, nichts tippen** – der Test drückt echte Tasten. Er holt vor jeder Aufgabe sein eigenes Testfenster nach vorn und schließt danach alle neu geöffneten Fenster. Abbruch: `Strg + Alt + Shift + Q`. Immer mit `Start-Process … -Wait` starten, nie im Vordergrund des eigenen Terminals.
- Nicht automatisierbar und deshalb übersprungen (`noAuto`): echte UAC-Abfragen (`regedit`), PC sperren, `ipconfig /release`. Diese prüft der Nutzer von Hand.
- **Erhöhte Fenster:** Bei einem Admin-Konto mit Standard-UAC laufen MMC-Konsolen, Task-Manager, Systemeigenschaften und Adapter-Eigenschaften ohne Abfrage erhöht. Windows verwirft dann alle simulierten Eingaben (UIPI), auch Maus, `WM_CLOSE` und Fensterwechsel; der Hook sieht dort nichts. Der Test meldet solche Schritte als „teilweise“ und schließt die Fenster per `EndTask`. Nur der Lauf als Administrator spielt diese Schritte. `__COMPAT_LAYER=RunAsInvoker` senkt MMC ab (zum Ausprobieren), den Task-Manager nicht.
- **Der Test verwirft ungespeicherten Text** neu geöffneter Editor-Fenster – deshalb nur in der VM laufen lassen.
- **Tasten:** Kombinationen gehen mit Abständen raus, Zugriffstasten in XAML-Dialogen (`Alt+N` = „Nicht speichern“) ignorieren sonst den ganzen Block.

## Typische Korrekturen

- **Fenstertitel passt nicht** (deutsches Windows 11): `titleRegex` in `src/KeyTrainer/Assets/exercises.json` anpassen. Die Datei ist die Quelle – direkt bearbeiten.
- **Tastenfolge reicht nicht** (z. B. Snap-Assist, Ladezeiten): `auto` bzw. `wait:` des Schritts anpassen. `auto` dient nur dem Test, nicht dem Nutzer.
- **Kürzel stimmt nicht**: `solution`, `hints`, ggf. `expect` korrigieren und `unverified` entfernen, sobald es bestätigt ist.
- Nach jeder Katalogänderung: `dotnet test` (prüft Katalog, Abläufe, Beispiele ohne `*`).

## Fertig machen

1. `dotnet test` grün, `--selftest` OK, `--e2e` ohne Fehler (oder verbleibende Fehler begründet).
2. Commit mit aussagekräftiger Nachricht (deutsch), `git push`. Jeder grüne Push auf `main` wird per CI zu einem Release `v0.2.<n>`, das der Update-Knopf installiert.
3. Dem Nutzer melden: was behoben wurde, was offen bleibt, welche Punkte er selbst testen muss (UAC, Sperren, echte Maus).

## Offene Punkte (Stand 06.11.2026)

Letzter Lauf auf deutschem Windows 11 25H2 (`--e2e --elevated`, ohne Admin-Rechte): 61 OK, 0 Fehler, 5 begründet übersprungen/teilweise.

- `start-taskmanager-beenden` Schritt 3: Der Task-Manager erhöht sich immer selbst; Bedienweg im Lauf als Administrator klären (Listeneintrag heißt „Notepad.exe“, nicht „Editor“).
- `tool-druckwarteschlange` Schritt 2: in einer nicht erhöhten Konsole bestätigt (zweimal Tab, „Druckw“), im Lauf als Administrator nachprüfen.
- Maus-Klicks in erhöhten Fenstern erkennt der Trainer nicht, und Tasten-Schritte (`mode: keys`) in erhöhten Fenstern können nicht gelingen. Ergebnis-Schritte funktionieren.
- Übrige Aufgaben mit `"unverified": true` betreffen Extras (RDP, VM, Outlook, Teams, AD).
- Der Test lässt die Einstellungen-App offen; harmlos.
- Danach laut PLAN.md: Wiederholung (Leitner) + Statistik über Tage, Übungsseite für Vimium/Formulare (Phase 5), Code-Signatur (Phase 6).

## Start-Prompt (in Claude Code in der VM einfügen)

```text
Lies docs/VM-AGENT.md, CLAUDE.md und PLAN.md. Du arbeitest direkt in der Windows-VM.
Ziel: Alle Kern-Aufgaben des KeyTrainers funktionieren auf diesem deutschen Windows 11.
Baue die App, führe Selbsttest und den automatischen Test (--e2e) aus, lies Bericht,
Screenshots und Log, behebe die Ursachen (meist Fenstertitel oder Tastenfolgen im Katalog,
sonst Code) und wiederhole das, bis --e2e fehlerfrei ist oder nur noch begründete Fälle
übrig sind. Committe und pushe jeden funktionierenden Zwischenstand. Frag mich nur, wenn
du wirklich nicht weiterkommst. Am Ende: kurze Zusammenfassung auf Deutsch, was behoben ist
und was ich selbst von Hand testen muss.
```
