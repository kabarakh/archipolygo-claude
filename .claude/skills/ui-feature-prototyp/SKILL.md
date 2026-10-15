---
name: ui-feature-prototyp
description: Für neue Feature-Ideen, die vor allem die UI betreffen. Baue die Idee zuerst grob im Harness nach und lass den Entwickler sie live ausprobieren. Erst danach kommt die Idee in die echte App (AvaloniaApplication1). Nutzen, bevor du Code für eine neue UI-Idee (neues Panel, neue Interaktion, neues Layout und Ähnliches) in die echte App schreibst.
---

# Neue UI-Ideen zuerst im Harness ausprobieren

**Regel:** Baue eine neue UI-Idee zuerst grob im Harness (`TestHarness/`)
nach. Schreibe erst danach Code für die echte App (`AvaloniaApplication1`).

Grund: Code oder eine Beschreibung allein zeigen nicht, wie eine UI wirkt.
Layout, Klickfolge und Gefühl zeigen sich erst, wenn jemand die UI anklickt.

## Wann dieser Skill gilt

- Die Idee betrifft vor allem die UI. Beispiele:
  - ein neues Panel, eine neue Spalte oder ein neuer Filter,
  - eine andere Anordnung,
  - eine neue Interaktion (Kontextmenü, Drag&Drop, ein weiterer
    Dialogschritt),
  - sichtbares Feedback (Badges, Farben, Animationen, neue Sortierung).
- Die Frage ist "Variante A oder Variante B?". Baue dann beide Varianten
  grob im Harness. Lass den Entwickler beide ausprobieren. Diskutiere die
  Varianten nicht nur abstrakt im Chat.
- Der Skill gilt **nicht** für Änderungen an Backend, Verbindung oder
  Persistenz ohne sichtbare Änderung der UI. Diese Änderungen kommen direkt
  in `AvaloniaApplication1`.

## Vorgehen

1. **Baue die Idee grob im Harness nach.**
   - Verwende echte Views und Controls aus `AvaloniaApplication1` wieder,
     wenn möglich. Oder kopiere sie als Basis. So zeigt der Prototyp das
     echte Verhalten.
   - Verwende die echte Struktur von `MainWindowViewModel` und
     `MainWindow`. Verwende statt einer echten Archipelago-Verbindung
     `FakeConnectionManager` und `FakePersistenceService`
     (`AvaloniaApplication1.TestSupport`).
   - So laufen Bindings, Scrollen, Filter, Sortierung und die Eigenheiten
     von `.axaml` (siehe `CLAUDE.md`) genau wie in der echten App.
     Netzwerk und Timing des Logins stören dabei nicht.
   - Speise über `ControlPanelWindow` und `FakeConnectionManager` genug
     plausible Testdaten ein. Die neue UI muss sinnvoll gefüllt sein, zum
     Beispiel mit genug Events für ein neues Filter-Feature.
   - Der Prototyp muss nicht fertig sein. Er ist ein anklickbarer Beweis der
     Idee. TODOs und Hacks im Code sind hier erlaubt.
2. **Teste die neue Interaktion selbst, bevor du an den Entwickler
   übergibst** (siehe "Sichtprüfung mit Screenshot und simuliertem Klick").
   - "Der Build ist fehlerfrei" ist kein Beweis. Ein Screenshot des
     Ruhezustands ist auch kein Beweis.
   - Löse jede neue Interaktion gezielt selbst aus. Beispiele: ein Klick,
     bei Drag&Drop ein echtes Drücken, Bewegen und Loslassen, bei einem
     neuen Dialogschritt der ganze Ablauf.
   - Prüfe mit einem Screenshot: Die App stürzt nicht ab, und das erwartete
     Ergebnis tritt ein.
   - Vorfall: Ein Prototyp für Tabs in mehreren Fenstern baute fehlerfrei.
     Der erste Screenshot sah richtig aus. Aber der erste echte Klick auf
     einen Tab ließ die App sofort abstürzen. Die Ursache war ein Fehler in
     der Logik der Auswahl. Nur ein echter Klick zeigte den Fehler. Ein
     eigener Testklick vor der Übergabe findet so einen Fehler.
   - Jede Übernahme von Maus oder Fokus für diesen Test braucht eine
     Erlaubnis (siehe "Fokus und Prozesse nie ohne Rückfrage"). Kündige den
     Test einmal an. Frage nicht vor jedem einzelnen Klick. Aber mache auch
     keine lange Kette von Klicks, ohne dass der Entwickler unterbrechen
     kann.
   - Wenn der Test einen Fehler findet, behebe ihn. Wiederhole dann
     *denselben* Testschritt, bis er funktioniert. Erst danach gehe zu
     Schritt 3.
3. **Lass den Entwickler die Idee selbst live ausprobieren.** Ein Screenshot
   genügt nicht.
   - Deine eigene Prüfung mit Screenshot und Klick ist nur eine Vorprüfung.
     Die Freigabe gibt ein Mensch. Er bewertet, ob sich die Idee richtig
     anfühlt, nicht nur, ob sie technisch funktioniert.
   - Starte den Harness sichtbar für den Entwickler
     (`dotnet run --project TestHarness`, im Vordergrund, nicht im
     Hintergrund). Bitte um Feedback.
4. **Übernimm die Idee erst nach Feedback und Freigabe des Entwicklers in
   die echte App.**
   - Überführe den Code des Prototyps sauber in echte Views und
     ViewModels. Entferne dabei die Abkürzungen aus dem Harness.
   - Neue Buttons im `ControlPanelWindow` dürfen bleiben, wenn sie für
     spätere Tests dieses Features nützlich sind. Entferne sie, wenn sie nur
     für diese eine Entscheidung nötig waren.
5. **Halte die Entscheidung im Feature-Plan fest.** Schreibe oder ändere
   den zugehörigen Plan im Feature-Plan-Verzeichnis nach dem Skill
   `deutsch-ste`.

## Sichtprüfung mit Screenshot und simuliertem Klick

Archipolygo ist eine native Desktop-App, keine Web-App. Darum funktionieren
die Browser-Tools hier nicht. Prüfe das Verhalten stattdessen so, wie ein
Mensch es sieht:

- mit echten Screenshots der laufenden Instanz des Harness,
- mit echten simulierten Mausklicks und Scrolls.

Logs und Code allein genügen nicht. Das funktioniert unter Windows und unter
macOS. Archipolygo ist eine Avalonia-App und läuft auf beiden Systemen
(siehe `.github/workflows/release.yml`, das auch Builds für `osx-x64` und
`osx-arm64` erzeugt). Welche Variante gilt, hängt vom Betriebssystem des
Tests ab.

Fertige Hilfsskripte liegen in `scripts/`. Für Windows sind sie in
PowerShell. Für macOS sind sie in Bash, mit denselben Parametern:

- [scripts/Take-Screenshot.ps1](scripts/Take-Screenshot.ps1) und
  [scripts/Take-Screenshot.sh](scripts/Take-Screenshot.sh): Machen einen
  Screenshot vom Fenster eines laufenden Prozesses (über den Prozessnamen)
  und speichern ihn als PNG.
- [scripts/Send-Click.ps1](scripts/Send-Click.ps1) und
  [scripts/Send-Click.sh](scripts/Send-Click.sh): Bewegen den echten
  Mauszeiger an Bildschirmkoordinaten und lösen einen Linksklick oder einen
  Scroll aus, wie ein Benutzer.

Ablauf eines Tests (Details für macOS unter "Variante für macOS"):

1. Starte den Harness (`dotnet run --project TestHarness` im Hintergrund).
   Die Buttons im `ControlPanelWindow` speisen die Testdaten ein (siehe
   auch Schritt 1 im Vorgehen).
2. Mache einen Screenshot vor der Interaktion. Sieh ihn mit `Read` an wie
   ein Bild. Lies dort den Ausgangszustand und die Koordinaten für den
   nächsten Klick ab.
3. Klicke oder scrolle mit `Send-Click.ps1` an der abgelesenen Koordinate.
4. Mache wieder einen Screenshot. Sieh ihn mit `Read` an und bewerte den
   Effekt.
5. Wiederhole die Schritte 2 bis 4 bis zum Ende des Testfalls.
   - Verwende dieselbe laufende Instanz weiter, solange du den Code nicht
     änderst. Ein Neustart mit `dotnet run` ist langsam und verdeckt das
     echte Verhalten.
   - Nach jeder echten Änderung am Code ist ein Neustart normal (siehe
     "Warum nicht direkt in der echten App").

Beispielaufrufe:

```powershell
powershell -File .claude/skills/ui-feature-prototyp/scripts/Take-Screenshot.ps1 -ProcessName TestHarness -OutFile shot1.png
```

```powershell
powershell -File .claude/skills/ui-feature-prototyp/scripts/Send-Click.ps1 -X 640 -Y 420
```

### Variante für macOS

Der Ablauf ist gleich. Die Parameter der Skripte sind gleich: Prozessname,
Ausgabedatei, optional ein Teil des Fenstertitels, X und Y, optional Scroll
oder Doppelklick. Nur die Werkzeuge darunter sind anders, denn macOS hat
keine Win32-API.

```bash
.claude/skills/ui-feature-prototyp/scripts/Take-Screenshot.sh -p TestHarness -o shot1.png
```

```bash
.claude/skills/ui-feature-prototyp/scripts/Send-Click.sh -x 640 -y 420
```

**Voraussetzung: [cliclick](https://github.com/BlueM/cliclick)
(`brew install cliclick`).**

- `Send-Click.sh` simuliert Klick und Doppelklick mit `cliclick`, nicht mit
  `click at` aus AppleScript oder System Events.
- Ohne `cliclick` bricht `Send-Click.sh` sofort mit einer Fehlermeldung ab.
- `Take-Screenshot.sh` braucht `cliclick` nicht. Es fragt nur das Fenster ab
  und ruft `screencapture` auf. Für Klicks unter macOS brauchst du
  `cliclick` aber trotzdem.

**Berechtigungen, einmal nötig** (Systemeinstellungen → Datenschutz &
Sicherheit). Gib sie dem Prozess, der die Skripte ausführt (zum Beispiel
Terminal, iTerm oder die Shell von Claude Code):

- **Bedienungshilfen (Accessibility):**
  - Für `cliclick`. Ohne diese Berechtigung meldet `cliclick` klar
    "Accessibility privileges not enabled".
  - Für die Fensterabfrage in `Take-Screenshot.sh` (Position und Größe
    eines Fensters über System Events). Ohne diese Berechtigung schlägt die
    Abfrage mit dem AppleScript-Fehler `-1719` fehl ("keine Berechtigung
    für den Hilfszugriff").
  - Beobachtet: System Events kann ohne diese Berechtigung Prozessnamen
    auflisten. Die Geometrie eines Fensters kann es aber nicht abfragen.
- **Bildschirmaufnahme (Screen Recording):** Für `screencapture`, das
  `Take-Screenshot.sh` aufruft.

Ohne `cliclick` und ohne diese zwei Berechtigungen funktioniert die Variante
für macOS nicht. Das ist kein Fehler in den Skripten. Ein aktuelles macOS
verlangt das so. Unter Windows funktionieren `GetWindowRect` und
`SetCursorPos` ohne zusätzliche Berechtigung.

**Bekannte Grenze: Unter macOS gibt es kein echtes Scrollen mit dem Mausrad
an einer Koordinate, auch nicht mit `cliclick`.**

- Windows hat dafür `mouse_event(MOUSEEVENTF_WHEEL, ...)`. macOS hat kein
  direktes Gegenstück.
- AppleScript und System Events kennen kein Kommando für das Mausrad.
  `cliclick` 5.1 kennt auch keins (mit `cliclick -h` geprüft). `w:` in
  `cliclick` bedeutet "WAIT", nicht "wheel".
- Darum bricht `Send-Click.sh -s <Schritte>` absichtlich mit einer klaren
  Fehlermeldung ab. Es nimmt keine ungenaue Näherung mit der Tastatur (zum
  Beispiel Pfeiltasten über `kp:` in `cliclick`) als stillen Ersatz.
- Für Klick und Doppelklick spielt das keine Rolle. Das sind die meisten
  Prototypen hier.
- Nur gezieltes Scrollen an einer Position bleibt unter macOS ungelöst. Wenn
  du das brauchst, frage zuerst. Baue keine Näherung ohne Rückfrage.

### Nie Screenshots des ganzen Bildschirms, immer nur das Zielfenster

**Mache nie einen Screenshot des ganzen Bildschirms.** Fotografiere immer
nur das eine Zielfenster.

- `Take-Screenshot.ps1` fotografiert ohne weitere Parameter nur
  `MainWindowHandle`. Windows wählt dieses eine Fenster pro Prozess nach
  eigenen Regeln. Es ist nicht immer das gewünschte Fenster.
- Ein Prozess des Harness hat manchmal mehr als ein sichtbares Fenster.
  Beispiele: `MainWindow` und ein eigenes `ControlPanelWindow`, oder
  zusätzliche Fenster, die zur Laufzeit entstehen (wie beim Prototyp für
  Tabs in mehreren Fenstern). Dann trifft `MainWindowHandle` nicht sicher
  das gewünschte Fenster.
- `Take-Screenshot.sh` unter macOS hat dasselbe Problem und dieselbe
  Lösung. Ohne `-t` nimmt es "window 1" des Prozesses, auch nur nach einer
  Regel. Mit `-t TitleSubstring` nimmt es das Fenster, dessen Titel diesen
  Text enthält.

**Vorfall:** Um dieses Problem zu umgehen, fotografierte ein Skript einmal
den ganzen virtuellen Bildschirm (alle Monitore). Es nutzte dazu
`CopyFromScreen` mit
`[System.Windows.Forms.SystemInformation]::VirtualScreen`. Das Bild zeigte
auch private Discord-Chats, Browser-Tabs und andere Apps auf den anderen
Monitoren des Entwicklers. Das war ein Vorfall beim Datenschutz.

**Richtig:** Verwende den Parameter `-TitleContains <Teilstring>` von
`Take-Screenshot.ps1`. Das Skript listet mit `EnumWindows` und
`GetWindowThreadProcessId` alle sichtbaren Fenster des Zielprozesses auf.
Dann nimmt es das erste Fenster, dessen Titel den Teilstring enthält.

```powershell
powershell -File .claude/skills/ui-feature-prototyp/scripts/Take-Screenshot.ps1 -ProcessName TestHarness -OutFile panel.png -TitleContains "Test Control Panel"
```

- Wähle einen eindeutigen Teilstring, wenn mehrere Fenstertitel ähnlich
  sind.
- Ein eigenes kleines `ControlPanelWindow` steuert manchmal die
  Fake-Verbindung, statt Buttons direkt im Testfenster. Dann setze die
  Positionen beider Fenster im Code fest (zum Beispiel mit
  `WindowStartupLocation` oder `Position`).
- Die zwei Fenster dürfen sich **nie überlappen**. Sonst verdeckt ein
  Fenster auf jedem Screenshot Teile des anderen. Außerdem fotografiert
  `Take-Screenshot.ps1` ohne Titel je nach Fokus vielleicht das falsche
  Fenster.

Manchmal lässt sich ein Test auch so nicht auf ein Fenster begrenzen. Dann
frage kurz nach oder suche eine gezielte Lösung. Zum Beispiel: Vergrößere
das Zielfenster mit `SetWindowPos`, damit kein Scrollen nötig ist.
Fotografiere nie ersatzweise den ganzen Bildschirm.

### Bekannte Ungenauigkeit: Die abgelesene Y-Koordinate liegt etwas zu hoch

- Betroffen sind Fenster wie `ControlPanelWindow`: ein `StackPanel` in
  einem `ScrollViewer`, ein Button unter dem anderen.
- Der Y-Wert, den du im Screenshot abliest, liegt dort oft ca. **5px zu
  hoch**. Der Klick landet dann knapp über dem Button. Es gibt keinen
  sichtbaren Fehler, nur keine Reaktion.
- Besonders betroffen war der letzte Button einer langen Liste, den der
  untere Fensterrand etwas abschneidet.

**Das half nicht:** Eine Prüfung des Hover-Zustands mit `SetCursorPos` und
Screenshot vor dem Klick. Das Theme zeigt bei `PointerOver` keinen sicher
erkennbaren Hintergrund. Die scheinbaren Farbunterschiede waren nur
ClearType-Ränder der Beschriftung. Skripte für Ausschnitte und Zoom
verschwendeten hier Zeit.

**Das half:** Klicke ca. 5px tiefer, als der Screenshot zeigt. Prüfe am
echten Effekt, ob der Klick getroffen hat, nicht am Aussehen von Cursor oder
Hover. Wenn ein Button nicht reagiert, probiere zuerst einen etwas tieferen
Y-Wert.

### Behobener Fehler: `Send-Click.ps1 -Scroll` mit negativem Wert

- Früher warf `-Scroll` mit negativem Wert (nach unten scrollen) einen
  Fehler beim Umwandeln in `[uint32]`.
- Die Lösung: Der P/Invoke-Parameter `dwData` von `mouse_event` hat jetzt
  den Typ `int` statt `uint`. Der native Aufruf bekommt dasselbe Bitmuster.
  PowerShell wirft dabei keinen Fehler mehr.
- Negative Werte für `-Scroll` funktionieren jetzt wie beschrieben.

### Debug-Informationen bei Bedarf in eine Log-Datei schreiben

Ein Screenshot zeigt nicht jeden Zustand sicher. Beispiele:

- genaue Scroll-Offsets,
- welche Events wann bei welchem `GroupViewModel` ankamen,
- die Reihenfolge von Property-Changed-Events.

Für solche Fälle schreibe im Prototyp gezielt Debug-Ausgaben in eine
Log-Datei:

1. Leite `Console.WriteLine` oder `Debug.WriteLine` in eine Textdatei um.
   Oder schreibe mit `File.AppendAllText` an den wichtigen Stellen.
2. Lege die Datei an einen klar erkennbaren, temporären Ort (das
   Scratchpad-Verzeichnis oder direkt neben `TestHarness`).
3. Werte die Datei mit `Read` oder `Grep` aus.
4. Lösche die Datei nach dem Test. Sie ist nur ein Hilfsmittel für diesen
   Test. Sie bleibt nicht im Repo und nicht im Scratchpad.

## Fokus und Prozesse nie ohne Rückfrage

- **Beende den Prozess des Harness nie selbst** (`Stop-Process`,
  `taskkill`, Fenster schließen). Frage immer zuerst den Entwickler. Das
  gilt auch, wenn du den Prozess nur zum Testen gestartet hast.
- `Take-Screenshot.ps1` holt das Testfenster mit `SetForegroundWindow` in
  den Vordergrund. Damit nimmt es der aktiven App den Fokus. Vielleicht
  arbeitet der Entwickler gerade in einer App im Vollbild (Spiel,
  Präsentation, Video). Dann frage, bevor du den Fokus nimmst. Im Zweifel
  frage.
- Dasselbe gilt für `Send-Click.ps1`, für `Take-Screenshot.ps1` und für
  jede eigene Simulation von Maus oder Tastatur (zum Beispiel ein eigenes
  Skript für einen Drag mit `SetCursorPos`).
  - Diese Werkzeuge bewegen den echten Mauszeiger, senden echte Eingaben
    oder nehmen der aktiven App den Fokus. Das stört den Entwickler in der
    App, in der er gerade liest oder tippt (zum Beispiel Claude Code
    selbst). Das gilt auch ohne App im Vollbild.
  - Unter macOS gilt dasselbe für `Send-Click.sh` (echter Klick über System
    Events) und `Take-Screenshot.sh` (`set frontmost to true` nimmt den
    Fokus wie `SetForegroundWindow`).

**Vorfälle, die zu dieser Regel führten:**

1. Ein Test schickte mehrere Aufrufe von `Send-Click.ps1` und ein Skript für
   einen Drag nacheinander ab. Er fragte dazwischen nicht nach.
2. Danach fragte ein Test einmal vorher: "Darf ich X und Y tun?" Danach
   behandelte er diese eine Zustimmung als gültig für eine ganze Kette:
   Prozess neu starten, Fenster verschieben, mehrere Screenshots und Klicks.
   Auch das empfand der Entwickler als Fokusübernahme ohne Ankündigung.

**Diese Prüfung verhindert das:** Prüfe vor jeder Aktion, die den Fokus
braucht, zuerst **passiv**, wo der Fokus liegt. Passiv heißt: Du änderst
dabei nichts. Aktionen mit Fokus sind ein Screenshot mit
`SetForegroundWindow` und ein simulierter Klick oder Drag mit `SetCursorPos`.
Vergleiche den Prozess des Fensters im Vordergrund mit dem Zielprozess. Unter
Windows nutzt du dafür `GetForegroundWindow` und `GetWindowThreadProcessId`:

```powershell
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Focus {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint procId);
}
"@
$fgHandle = [Win32Focus]::GetForegroundWindow()
$fgProcId = 0
[Win32Focus]::GetWindowThreadProcessId($fgHandle, [ref]$fgProcId) | Out-Null
$fgProc = Get-Process -Id $fgProcId -ErrorAction SilentlyContinue
$fgProc.ProcessName  # z. B. "TestHarness" oder "Code"/"WindowsTerminal"/...
```

Dieselbe passive Prüfung unter macOS. Sie braucht dieselbe Berechtigung für
Bedienungshilfen wie unter "Variante für macOS":

```bash
osascript -e 'tell application "System Events" to name of first application process whose frontmost is true'
# z. B. "TestHarness" oder "Terminal"/"iTerm2"/"Code"/...
```

So reagierst du auf das Ergebnis:

- **Der Fokus liegt schon auf dem Testprozess.** Der Entwickler sieht also
  selbst auf das Testfenster, zum Beispiel weil er dort gerade geklickt
  hat. Dann ist keine Rückfrage nötig. Die Aktion ändert nichts, was der
  Entwickler nicht schon ansieht.
- **Der Fokus liegt auf einem anderen Fenster.** Meistens ist das Claude
  Code selbst, wo der Entwickler liest oder tippt. Nimm dann an: Der
  Entwickler macht gerade etwas anderes und will dem Test nicht zusehen.
  Frage vor der Aktion ausdrücklich im Chat und warte auf die Antwort.
  Verlasse dich nicht auf eine frühere, alte Zustimmung.
- **Die Prüfung schlägt in diesem Testschritt vermutlich mehrmals an**, zum
  Beispiel bei einer Folge von Klicks mit Screenshots dazwischen. Kündige
  das **vorher als Warnung** an: "Das kann mehrmals nachfragen, weil der
  Fokus wiederholt zum Harness wechselt." So überraschst du den Entwickler
  nicht mit einer Unterbrechung nach der anderen. Er kann die ganze
  angekündigte Folge auf einmal freigeben.
- **Auch innerhalb einer freigegebenen Folge prüfst du den Fokus vor jeder
  Aktion neu**, nicht nur einmal am Anfang. Wenn der Fokus zwischendurch
  nicht mehr auf dem Testfenster liegt, gilt die alte Freigabe nicht mehr.
  Der Entwickler hat dann woanders geklickt oder gearbeitet. Frage neu.

## Warum nicht direkt in der echten App

- Eine echte Archipelago-Verbindung stört beim reinen Feedback zur UI
  (Netzwerk, Timing des Logins, Zustand des Servers).
- Ein halbfertiges Feature darf nicht in `AvaloniaApplication1` landen,
  bevor der Entwickler es gesehen und freigegeben hat.
- Der Zyklus ist kurz: Änderung, Neustart des Harness, der Entwickler sieht
  sie an, weitere Änderung. Die echte App ist dabei nie halbfertig.
