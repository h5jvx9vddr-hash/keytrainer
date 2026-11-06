# Tastatur-Trainer für Windows – Plan für Claude Code

Nov 3, 2026 · @Jakob

## Ziel & Überblick

Eine kleine Windows-App, die Tastaturbedienung mit echten Aufgaben trainiert: Sie stellt eine Aufgabe, erkennt die gedrückten Kürzel systemweit und prüft am Ergebnis, ob die Aufgabe wirklich erledigt wurde.

- **Zielgruppe:** IT-Dienstleister, der Windows administriert, viel im Browser arbeitet und Web-Apps wie TANSS oder NAS-Oberflächen nutzt.
- **Kernidee:** Nicht nur „Welches Kürzel öffnet den Explorer?“ abfragen, sondern „Öffne den Explorer“ verlangen und prüfen, ob ein Explorer-Fenster aufgeht.
- **Lernprinzip:** Wenige neue Kürzel pro Tag, Wiederholung nach Leitner-System, Mausnutzung wird gezählt und sichtbar gemacht.
- **Umfang V1:** Lokal, offline, eine Person, keine Installation nötig (eine portable .exe).

## Technik-Entscheidungen

Empfehlung: **C# mit .NET 8 und WPF**, veröffentlicht als einzelne, eigenständige .exe. Das gibt den saubersten Zugriff auf die Windows-APIs, die der Trainer braucht.

| Option | Vorteile | Nachteile |
| --- | --- | --- |
| **C# / .NET 8 / WPF** (empfohlen) | Tastatur-Hook und Fensterprüfung direkt per Win32/P/Invoke und UI Automation; eine .exe ohne Laufzeit-Installation; Overlay-Fenster ohne Fokusklau gut machbar | Etwas mehr Code als Python |
| Python (pynput, pywin32, PySide6) | Schnell anpassbar, Übungen leicht erweiterbar | PyInstaller-Pakete werden oft von Virenscannern beanstandet; Hook-Verhalten weniger kontrollierbar |
| AutoHotkey v2 | Kürzel abfangen ist trivial | Für Oberfläche, Statistik und Wiederholungslogik unhandlich |

Wichtige Bausteine:

- **Tastatur erkennen:** globaler Low-Level-Hook (`SetWindowsHookEx` mit `WH_KEYBOARD_LL`).
- **Maus zählen:** globaler Low-Level-Maus-Hook (`WH_MOUSE_LL`), nur zählen, nichts blockieren.
- **Ergebnis prüfen:** Vordergrundfenster per `GetForegroundWindow`, dazu Fensterklasse, Titel und Prozessname; bei Bedarf UI Automation für Elemente im Fenster.
- **Sperren erkennen:** `SystemEvents.SessionSwitch` für `Win + L`.
- **Daten:** Übungen als JSON-Datei, Fortschritt als JSON unter `%APPDATA%\KeyTrainer\`.

## Funktionen

Der Trainer läuft als schmales Overlay am Bildschirmrand, damit du in den echten Programmen übst und nicht in einer Übungsoberfläche.

### Übungsmodi

1. **Lernen:** Aufgabe und Lösung werden angezeigt, du führst sie aus. Für neue Kürzel.
2. **Quiz:** Nur die Aufgabe, Lösung erst nach Fehlversuch oder auf Tastendruck. Mit Zeitmessung.
3. **Drill (Wiederholung):** Leitner-System mit 5 Fächern. Richtig = ein Fach höher, falsch = zurück in Fach 1. Fällig sind Fach 1 täglich, Fach 2 alle 2 Tage, Fach 3 alle 4, Fach 4 alle 8, Fach 5 alle 16.
4. **Szenario:** Mehrschrittige Praxisaufgaben, z. B. „Öffne die Dienste, suche die Druckwarteschlange, öffne ihre Eigenschaften“. Jeder Schritt wird einzeln geprüft.

### Overlay

- Immer im Vordergrund, klein, am rechten Rand.
- **Darf nie den Fokus übernehmen** (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`), sonst gehen Tastenkürzel ans Overlay statt ans Zielprogramm.
- Zeigt: aktuelle Aufgabe, ggf. Lösung, Ergebnis (richtig/falsch), Mausklicks dieser Aufgabe, Tagesziel.
- Steuerung des Trainers selbst nur über eigene globale Kürzel, z. B. `Strg + Alt + Shift + N` (nächste Aufgabe), `… + H` (Hinweis), `… + P` (Pause).

### Prüfung einer Aufgabe

Jede Übung hat eine erwartete Eingabe und optional eine Ergebnisprüfung:

- **Nur Kürzel:** Die erkannte Tastenkombination stimmt (z. B. `Strg + Shift + T` im Browser).
- **Kürzel + Ergebnis:** Zusätzlich muss innerhalb von z. B. 5 Sekunden ein passendes Fenster im Vordergrund sein (Prozess, Fensterklasse oder Titel per Regex). Beispiel: `services.msc` → Prozess `mmc.exe`, Titel enthält „Dienste“.
- **Nur Ergebnis:** Der Weg ist egal, Hauptsache das Ziel ist erreicht. Gut für Szenarien.
- Wurde die Maus während der Aufgabe benutzt, zählt die Aufgabe als „mit Maus“ und steigt nicht im Leitner-System.

### Browser-Übungsseite

Für Vimium, `Tab`-Navigation und `Strg + F` liefert der Trainer eine lokale HTML-Übungsseite mit typischen Web-App-Elementen (Ticketliste, Formular, Menüs, NAS-ähnliches Seitenmenü). Erreicht man das Zielelement, setzt die Seite ihren Titel auf `KT-OK-<Übungs-ID>`. Der Trainer liest den Fenstertitel des Browsers und erkennt so den Erfolg, ohne Browser-Erweiterung.

### Fortschritt & Motivation

- Tagesziel (z. B. 20 Aufgaben), Serie in Tagen.
- „Mausfrei-Quote“ pro Tag und Kategorie.
- Statistik: schwächste Kürzel, durchschnittliche Reaktionszeit.
- Freischalten: neue Kürzel kommen erst, wenn genug alte in Fach 3 oder höher sind (max. 5 neue pro Tag).

## Übungskatalog

Startumfang: rund 50 Übungen in 6 Kategorien. Claude Code erzeugt daraus `exercises.json`; neue Übungen kommen später nur über die JSON-Datei dazu. Fenstertitel gelten für ein deutsches Windows 11 und müssen als Regex konfigurierbar sein.

| Kategorie | Aufgabe | Lösung | Prüfung |
| --- | --- | --- | --- |
| Basis | Startmenü öffnen und Rechner starten | `Win`, „rechner“, `Enter` | Prozess `CalculatorApp.exe` |
| Basis | Zum vorherigen Fenster wechseln | `Alt + Tab` | Kürzel |
| Basis | Aktives Fenster schließen | `Alt + F4` | Kürzel |
| Basis | Kontextmenü öffnen | `Shift + F10` | Kürzel |
| Basis | Desktop anzeigen | `Win + D` | Kürzel |
| Basis | Programm auf Taskleistenplatz 1 öffnen | `Win + 1` | Kürzel |
| Fenster | Fenster links andocken | `Win + Pfeil links` | Kürzel |
| Fenster | Fenster maximieren | `Win + Pfeil hoch` | Kürzel |
| Fenster | Fenster auf anderen Monitor verschieben | `Win + Shift + Pfeil` | Kürzel |
| Fenster | Neuen virtuellen Desktop anlegen | `Win + Strg + D` | Kürzel |
| Fenster | Zwischen Desktops wechseln | `Win + Strg + Pfeil` | Kürzel |
| Fenster | Aufgabenansicht öffnen | `Win + Tab` | Kürzel |
| Text | Wortweise springen | `Strg + Pfeil` | Kürzel |
| Text | Wort links löschen | `Strg + Rücktaste` | Kürzel |
| Text | Bis Zeilenende markieren | `Shift + Ende` | Kürzel |
| Text | Zwischenablage-Verlauf öffnen | `Win + V` | Kürzel |
| Text | Screenshot-Ausschnitt | `Win + Shift + S` | Kürzel |
| Explorer | Explorer öffnen | `Win + E` | Fensterklasse `CabinetWClass` |
| Explorer | In die Adressleiste springen | `Strg + L` | Kürzel, Explorer im Vordergrund |
| Explorer | Einen Ordner nach oben | `Alt + Pfeil hoch` | Kürzel + Titeländerung |
| Explorer | Datei umbenennen | `F2` | Kürzel |
| Explorer | Eigenschaften öffnen | `Alt + Enter` | Fenster mit Titel „Eigenschaften“ |
| Explorer | Neuen Ordner anlegen | `Strg + Shift + N` | Kürzel |
| Admin | Ausführen-Dialog öffnen | `Win + R` | Klasse `#32770`, Titel „Ausführen“ |
| Admin | Dienste öffnen | `Win + R`, `services.msc` | `mmc.exe`, Titel „Dienste“ |
| Admin | Ereignisanzeige öffnen | `Win + R`, `eventvwr.msc` | `mmc.exe`, Titel „Ereignisanzeige“ |
| Admin | Computerverwaltung öffnen | `Win + R`, `compmgmt.msc` | `mmc.exe`, Titel „Computerverwaltung“ |
| Admin | Geräte-Manager öffnen | `Win + R`, `devmgmt.msc` | `mmc.exe`, Titel „Geräte-Manager“ |
| Admin | Datenträgerverwaltung öffnen | `Win + R`, `diskmgmt.msc` | `mmc.exe`, Titel „Datenträgerverwaltung“ |
| Admin | Lokale Benutzer und Gruppen | `Win + R`, `lusrmgr.msc` | `mmc.exe`, Titel per Regex |
| Admin | Netzwerkadapter öffnen | `Win + R`, `ncpa.cpl` | Explorer-Fenster „Netzwerkverbindungen“ |
| Admin | Programme deinstallieren | `Win + R`, `appwiz.cpl` | Titel „Programme und Features“ |
| Admin | Systemeigenschaften | `Win + R`, `sysdm.cpl` | Titel „Systemeigenschaften“ |
| Admin | Systeminformationen | `Win + R`, `msinfo32` | Prozess `msinfo32.exe` |
| Admin | Task-Manager öffnen | `Strg + Shift + Esc` | Prozess `Taskmgr.exe` |
| Admin | Terminal als Administrator | `Win + X`, `A` | Prozess `WindowsTerminal.exe` |
| Admin | Programm als Admin aus der Suche starten | `Win`, Name, `Strg + Shift + Enter` | Kürzel (UAC-Dialog erscheint) |
| Admin | Einstellungen öffnen | `Win + I` | Titel „Einstellungen“ |
| Admin | Remotedesktop öffnen | `Win + R`, `mstsc` | Prozess `mstsc.exe` |
| Admin | PC sperren | `Win + L` | Sitzungssperre-Ereignis |
| Terminal | Neuer Tab | `Strg + Shift + T` | Kürzel, Terminal im Vordergrund |
| Terminal | Fenster teilen | `Alt + Shift + +` | Kürzel |
| Browser | Adressleiste | `Strg + L` | Kürzel, Browser im Vordergrund |
| Browser | Neuer / geschlossener Tab zurück | `Strg + T` / `Strg + Shift + T` | Kürzel |
| Browser | Zu Tab 3 springen | `Strg + 3` | Kürzel |
| Browser | Zurück | `Alt + Pfeil links` | Kürzel |
| Browser | Profil wechseln | `Strg + Shift + M` | Kürzel |
| Browser | Link per Suche öffnen | `Strg + F`, Text, `Esc`, `Enter` | Übungsseite meldet Erfolg |
| Browser | Vimium: Button „Ticket speichern“ klicken | `f` + Buchstaben | Übungsseite meldet Erfolg |
| Browser | Vimium: ins Suchfeld | `gi` | Übungsseite meldet Erfolg |
| Browser | Formular nur mit `Tab` ausfüllen und absenden | `Tab`, `Leertaste`, `Enter` | Übungsseite meldet Erfolg |

**Szenarien für V1 (Beispiele):**

1. Dienst prüfen: Dienste öffnen → „Druckwarteschlange“ per Tipp-Suche finden → Eigenschaften mit `Alt + Enter`.
2. Log finden: Ereignisanzeige öffnen → Windows-Protokolle → System.
3. Adapter prüfen: `ncpa.cpl` → Adapter wählen → Eigenschaften.
4. Web-Ticket: Übungsseite → Ticketliste per Vimium → Ticket öffnen → Kommentar ins Formular → speichern.

TANSS hat keine öffentlich dokumentierten Tastenkürzel. Die Web-App-Übungen trainieren deshalb die allgemeinen Browser-Techniken auf der eigenen Übungsseite.

## Architektur & Projektstruktur

Die ExerciseEngine ist der einzige Baustein mit Logik zum Übungsablauf; Hooks, Prüfung und Speicherung sind austauschbare Dienste dahinter.

&#91;embedded content: Bausteine des Trainers · 9 Komponenten\]

Tastendrücke laufen über den InputService in die Engine, der Verifier meldet, ob das erwartete Fenster erschienen ist (auch den Titel der Übungsseite), und der Scheduler entscheidet, welche Übung als Nächstes fällig ist.

### Projektstruktur

```text
KeyTrainer/
  CLAUDE.md
  PLAN.md                     <- dieses Dokument als Markdown
  src/KeyTrainer/
    App.xaml
    Overlay/OverlayWindow.xaml
    Input/KeyboardHook.cs
    Input/MouseHook.cs
    Input/ComboDetector.cs    <- Tasten zu "Win+R", Sequenzen, Texteingabe
    Verify/WindowVerifier.cs
    Verify/SessionWatcher.cs
    Engine/ExerciseEngine.cs
    Engine/Scenario.cs
    Learning/LeitnerScheduler.cs
    Data/ExerciseRepository.cs
    Data/ProgressStore.cs
    Assets/exercises.json
    Assets/practice/index.html
  tests/KeyTrainer.Tests/     <- ComboDetector, Scheduler, Prüfregeln
```

### Datenformat einer Übung

```json
{
  "id": "admin-services",
  "category": "Admin",
  "level": 2,
  "prompt": "Öffne die Dienste-Verwaltung",
  "hint": "Ausführen-Dialog, dann .msc-Befehl",
  "solution": "Win + R, services.msc, Enter",
  "mode": "keysAndResult",
  "expect": { "sequence": ["Win+R", "text:services.msc", "Enter"] },
  "verify": { "process": "mmc.exe", "titleRegex": "Dienste", "timeoutMs": 5000 }
}
```

- `mode`: `keys` | `keysAndResult` | `result`.
- `expect.sequence`: Kombinationen in Normalform (`Strg+Shift+T`), `text:` für getippte Zeichenfolgen.
- `verify`: beliebige Kombination aus `process`, `windowClass`, `titleRegex`, `practiceTitle`, `sessionLock`.
- Szenarien: `"steps": [ … ]` mit je eigenem `prompt`, `expect` und `verify`.

## Umsetzung in Phasen

Sechs Phasen, jede endet mit einer lauffähigen App. Claude Code soll immer nur eine Phase umsetzen, testen und dann auf deine Freigabe warten.

### Phase 1: Gerüst und Tastaturerkennung

WPF-Projekt, `KeyboardHook`, `ComboDetector`, ein Debug-Fenster zeigt jede erkannte Kombination an.

- [ ] `Win + R`, `Strg + Shift + T`, `Alt + Tab`, `Win + Shift + S` erscheinen korrekt normalisiert
- [ ] Getippter Text wird als `text:` erkannt, Sondertasten nicht
- [ ] Hook wird beim Beenden und bei Absturz sauber entfernt
- [ ] Unit-Tests für `ComboDetector`

### Phase 2: Overlay und Übungsablauf

Overlay-Fenster, `exercises.json` laden, Modi Lernen und Quiz für Übungen mit `mode: keys`.

- [ ] Overlay bleibt sichtbar und übernimmt nie den Fokus (Test: Overlay offen, `Win + R` öffnet trotzdem den Ausführen-Dialog)
- [ ] Richtige Eingabe → grün, nächste Aufgabe; falsche → Hinweis
- [ ] Trainer-Kürzel (Nächste, Hinweis, Pause) funktionieren global

### Phase 3: Ergebnisprüfung

`WindowVerifier`, `SessionWatcher`, Sequenzen mit Texteingabe.

- [ ] Alle Admin-Übungen aus dem Katalog werden richtig erkannt
- [ ] Timeout greift, falsches Fenster zählt als Fehler
- [ ] Titel-Regeln sind in der JSON-Datei anpassbar

### Phase 4: Maus-Zähler und Wiederholung

`MouseHook`, `LeitnerScheduler`, `ProgressStore`.

- [ ] Mausnutzung während einer Aufgabe wird erkannt und angezeigt
- [ ] Fortschritt bleibt nach Neustart erhalten
- [ ] Fällige Übungen und max. 5 neue pro Tag stimmen

### Phase 5: Übungsseite und Szenarien

`practice/index.html`, Szenario-Ablauf mit Einzelschritten.

- [ ] Die 4 Beispielszenarien sind durchspielbar
- [ ] Vimium-, `Tab`- und `Strg + F`-Übungen werden über den Seitentitel erkannt

### Phase 6: Statistik und Auslieferung

Tagesziel, Serie, schwächste Kürzel, Einstellungsdialog, Veröffentlichung.

- [ ] `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` erzeugt eine lauffähige .exe
- [ ] App startet ohne Admin-Rechte und ohne Installation

## Technische Grenzen & Sicherheit

Ein globaler Tastatur-Hook ist technisch dasselbe wie ein Keylogger. Der Trainer muss deshalb sparsam damit umgehen und darf nur auf dem eigenen Rechner laufen.

### Grenzen, die Claude Code kennen muss

- **`Strg + Alt + Entf`** lässt sich nicht abfangen. Nicht in den Katalog aufnehmen.
- **`Win + L`** lässt sich nicht abfangen, nur die Sperre selbst erkennen (`SessionSwitch`).
- **Fenster mit Admin-Rechten:** Ist ein erhöhtes Fenster im Vordergrund (z. B. Terminal als Admin, Datenträgerverwaltung), bekommt ein normal laufender Hook keine Tasten mehr (UIPI). Lösung: In solchen Übungen nur das Ergebnis prüfen, die Fensterliste funktioniert weiterhin. Optional den Trainer als Admin starten.
- **Fenstertitel** hängen von Sprache und Windows-Version ab. Deshalb Regex in der JSON-Datei und lieber Prozess oder Fensterklasse prüfen.

### Sicherheitsregeln

- Trainer-eigene Kürzel über `RegisterHotKey`, nicht über den Hook.
- Tastatur- und Maus-Hook **nur während einer laufenden Übung** aktiv, bei Pause sofort entfernen.
- **Keine Tasten auf die Festplatte schreiben.** Eingaben nur im Speicher zum Vergleich halten, gespeichert wird nur das Ergebnis (richtig, falsch, Zeit, Maus ja/nein).
- Der Trainer ändert nichts am System: Er öffnet keine Admin-Werkzeuge selbst, schließt keine fremden Fenster und führt keine Befehle aus.
- **Nicht auf Kunden-PCs oder Servern einsetzen.** Virenscanner und EDR-Systeme melden globale Tastatur-Hooks oft als verdächtig; das kann beim Kunden Alarm auslösen.

## So startest du mit Claude Code

Claude Code muss direkt unter Windows laufen, nicht in WSL: WPF lässt sich nur unter Windows bauen, und die Hooks lassen sich nur dort testen.

1. **.NET 8 SDK** installieren (prüfen mit `dotnet --version`).
2. Ordner `KeyTrainer` anlegen.
3. Dieses Dokument als Markdown exportieren und als `PLAN.md` in den Ordner legen.
4. `CLAUDE.md` mit dem Inhalt unten anlegen.
5. Claude Code im Ordner starten und den Start-Prompt eingeben.
6. Nach jeder Phase die manuellen Tests selbst durchführen und erst dann die nächste Phase freigeben.

### Inhalt für CLAUDE.md

```markdown
# KeyTrainer

Windows-Tastaturtrainer in C# / .NET 8 / WPF. Vollständiger Plan: PLAN.md.

## Arbeitsweise
- Immer nur die aktuelle Phase aus PLAN.md umsetzen.
- Nach jeder Phase: `dotnet build` und `dotnet test` ausführen, Ergebnis zusammenfassen,
  eine Liste manueller Tests ausgeben und auf Freigabe warten.
- Hooks und Overlay sind nicht automatisch testbar: dafür immer manuelle Testschritte nennen.

## Regeln
- Tastatureingaben niemals in Dateien oder Logs schreiben.
- Tastatur- und Maus-Hook nur während einer laufenden Übung aktiv.
- Trainer-Kürzel über RegisterHotKey, nicht über den Hook.
- Alle Win32-Aufrufe gesammelt in `Native/NativeMethods.cs`.
- Übungen nur in `Assets/exercises.json`, nie im Code.
- UI-Texte auf Deutsch, Code und Kommentare auf Englisch.
- Das Overlay darf nie den Fokus übernehmen (WS_EX_NOACTIVATE).
```

### Start-Prompt

```text
Lies PLAN.md und CLAUDE.md. Erstelle zuerst einen kurzen Umsetzungsplan
für Phase 1 und warte auf meine Freigabe. Danach setze Phase 1 um,
schreibe die Unit-Tests, führe dotnet build und dotnet test aus und gib
mir eine Liste der manuellen Tests, die ich am Rechner machen soll.
```

### Für jede weitere Phase

```text
Phase <N> ist abgenommen. Weiter mit Phase <N+1>, gleiches Vorgehen.
```

Wenn ein manueller Test scheitert: Beschreibe Claude Code genau, was du gedrückt hast, was passiert ist und was du erwartet hast.

---

## Abweichungen und Ergänzungen (03.11.2026, abgestimmt)

- **.NET 10 LTS** statt .NET 8 (Support-Ende .NET 8: 10.11.2026).
- **Raw Input statt Low-Level-Hook:** Tastatur und Maus per `RegisterRawInputDevices` + `RIDEV_INPUTSINK`. Nur mitlesen, nichts blockieren, wirkt für Virenscanner harmloser. `WH_KEYBOARD_LL` nur als Fallback, falls Phase-1-Tests scheitern. Wo oben „Hook“ steht, ist die Eingabeerfassung gemeint.
- **Aufteilung:** `KeyTrainer.Core` (net10.0, plattformneutral, Unit-Tests auch unter Linux) und `KeyTrainer` (net10.0-windows, WPF). Build der .exe über GitHub Actions `windows-latest`.
- **Tracking:** Jeder Versuch wird gespeichert (Übung, Zeiten, erkannte Kürzel inkl. falscher, Maus, Hinweisstufe, Ergebnis). Getippter Text nur als „passt/passt nicht“, nie im Klartext.
- **Hilfestellung gestuft:** Tipp → erste Taste → komplette Lösung; per Hotkey oder automatisch nach Zeit/Fehlversuchen. Falsches Kürzel wird erklärt (`explainWrong`). JSON: `hints: [...]` statt `hint`.
- **Overlay:** einklappbar, bei Vollbild ausgeblendet. Trainer-Kürzel nicht mit `Strg+Alt` (= AltGr auf deutscher Tastatur).
- **Virenscanner:** kein Packer/Obfuscator, `EnableCompressionInSingleFile` aus, saubere Assembly-Metadaten, Manifest `asInvoker`, kein Netzwerk/Autostart/Injektion; Code-Signatur (Azure Trusted Signing) in Phase 6 vorgesehen.
- **Technik:** UWP-Fenster (Rechner, Einstellungen) über `ApplicationFrameHost` → Kindfenster auflösen; Vordergrund per `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`; `ToUnicodeEx` mit Flag `0x4` (Tottasten bleiben intakt).
- **Overlay oben mittig** am Bildschirmrand (Nutzerwunsch 04.11.), nicht rechts.
- **Update-Knopf:** lädt die neueste Version aus GitHub-Releases (öffentlich, Nutzerentscheidung), prüft SHA-256, tauscht die exe und startet neu.
- **Katalog erweitert** um FISI-Themen (Konsolen, Server/AD, Kommandozeile, Terminal, Fernwartung, Linux/SSH, Büro, Alltags-Szenarien), 152 Übungen; Felder `requires`, `elevated`, `unverified`, `contexts`.
- **Raw Input verworfen (04.11.):** In der VM kamen Win+R/Win+E nur als „Win“ an – die Shell schluckt solche Kürzel vor Raw Input. Erfassung jetzt per Low-Level-Hook (nur beobachten). Der CI-Selbsttest schickt Win+R per SendInput und prüft die Erkennung.
- **Phase 2 (04.11.):** Aufgaben-Leiste oben mittig, Modi Lernen/Quiz, gestufte Hinweise (Tipps → erste Taste → Lösung; automatisch nach 20 s bzw. 2 Fehlversuchen), Fehlererklärungen, Kontextprüfung des Vordergrundfensters, Trainer-Kürzel Strg+Shift+F1–F4. Übungen mit `keysAndResult` prüfen vorerst nur die Tasten; `result` und Szenarien folgen mit Phase 3.
- **Neuausrichtung (04.11., Nutzerentscheidung):** Ziel ist, den PC ohne Maus zu steuern. Aufgaben = Ziele, geprüft am PC-Zustand (Fenster, Fensterzustand, Titelwechsel, Prozesse, Ordner, Sperre); jeder Tastatur-Weg zählt; Maus-Klick/Scroll = Aufgabe zählt nicht (WH_MOUSE_LL, nur Zähler). Keine roten Fehler für andere Tasten, nur neutrale Anzeige. Mehrschrittige, in sich geschlossene Aufgaben. Kommandozeile/Terminal/Linux/Fernwartung/Büro/Server sind Extras (standardmäßig aus). Fehlende Software aufklappbar mit Installieren-Knopf. Phase 3 (Ergebnisprüfung) und Teile von Phase 4 (Maus) damit vorgezogen; Leitner und Statistik folgen.
- **E2E auf deutschem Windows 11 (06.11.):** MMC, Task-Manager & Co. laufen beim Admin-Konto ohne UAC-Abfrage erhöht, simulierte Eingaben und der Hook enden dort (UIPI). Der Test erkennt das, meldet „teilweise“ und schließt solche Fenster per `EndTask`. `verify.processGone` durch `windowClosed` ersetzt (weniger Fenster als der Höchststand im Schritt, damit andere offene Fenster nicht stören). `processRestarted` vergleicht Prozess-IDs, weil die Startzeit von `spoolsv.exe` ohne Admin-Rechte nicht lesbar ist.
