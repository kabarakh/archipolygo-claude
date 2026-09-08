---
name: ui-feature-prototyp
description: Für neue Feature-Ideen, die vor allem die UI/UX betreffen - zuerst grob im TestHarness prototypisch nachbauen und vom Entwickler selbst live durchklicken lassen, bevor die Idee in die echte App (AvaloniaApplication1) übernommen wird. Nutzen, bevor Code für eine neue UI-Idee (neues Panel, neue Interaktion, neues Layout, ...) in die Produktiv-App eingebaut wird.
---

# Neue UI-Feature-Ideen zuerst im TestHarness prototypisieren

UI-Ideen lassen sich schlecht allein am Code oder an einer Beschreibung
beurteilen - Layout, Klickfluss und "wie fühlt sich das an" zeigen sich erst,
wenn man es tatsächlich anklickt. Deshalb wird eine neue, vor allem
UI-getriebene Feature-Idee **zuerst grob im vorhandenen `TestHarness/`-
Projekt** nachgebaut, bevor überhaupt Code für die echte App
(`AvaloniaApplication1`) geschrieben wird.

## Wann dieser Skill greift

- Die Idee betrifft überwiegend UI/UX: ein neues Panel, eine neue Spalte
  oder ein neuer Filter, eine andere Anordnung, eine neue Interaktion
  (Kontextmenü, Drag&Drop, zusätzlicher Dialog-Schritt), visuelles Feedback
  (Badges, Farben, Animationen, neue Sortierung).
- Auch bei "Variante A oder B?"-Fragen: beide Varianten grob im TestHarness
  bauen und den Entwickler beide durchklicken lassen, statt das im Chat
  abstrakt zu diskutieren.
- **Nicht** nötig für reine Backend-/Verbindungs-/Persistenz-Änderungen ohne
  sichtbares UI-Delta - die gehen direkt in `AvaloniaApplication1`.

## Vorgehen

1. **Idee im TestHarness grob prototypisieren.** Wo möglich echte Views/
   Controls aus `AvaloniaApplication1` wiederverwenden oder als Basis
   kopieren, damit der Prototyp repräsentativ ist: die echte
   `MainWindowViewModel`/`MainWindow`-Struktur, aber mit
   `FakeConnectionManager`/`FakePersistenceService`
   (`AvaloniaApplication1.TestSupport`) statt echter Archipelago-Verbindung,
   damit echte Bindings/Scrolling/Filter/Sortierung und echte
   `.axaml`-Quirks (siehe `CLAUDE.md`) exakt wie in der Produktiv-App
   laufen - nur ohne Netzwerk/Login-Timing als Störfaktor. Über
   `ControlPanelWindow`/`FakeConnectionManager` genug plausible Testdaten
   einspeisen, damit die neue UI sich sinnvoll befüllt zeigt (z. B. genug
   Events, um ein neues Filter-Feature zu demonstrieren). Der Prototyp muss
   nicht production-ready sein - Ziel ist ein anklickbarer Beweis der Idee,
   keine fertige Implementierung; TODOs/Hacks im Code sind an dieser Stelle
   okay.
2. **Vor jedem Handover an den Entwickler: die tatsächlich neue Interaktion
   selbst erfolgreich getestet haben** (siehe "Sichtprüfung per Screenshot +
   simuliertem Mausklick" unten) - "baut fehlerfrei" und "ein Screenshot des
   Ruhezustands sieht gut aus" sind kein Beleg dafür, dass die eigentliche
   neue Interaktion funktioniert. Konkreter Vorfall, der diese Regel
   ausgelöst hat: ein Multi-Window-Tabs-Prototyp baute fehlerfrei und der
   initiale Screenshot sah korrekt aus, aber der erste tatsächliche Klick
   auf einen Tab crashte die App sofort (ein Folgefehler in der
   Selektionslogik, der nur bei echter Pointer-Interaktion auftrat) - das
   hätte ein eigener Testklick vor dem Handover gefunden, statt dass der
   Entwickler es tut. Also: die neue(n) Interaktion(en) gezielt selbst
   auslösen (Klick, bei Drag&Drop ein echtes Press-Move-Release, bei einem
   neuen Dialogschritt der ganze Ablauf, ...) und per Screenshot
   verifizieren, dass dabei nichts crasht und das erwartete Ergebnis
   eintritt - nicht nur den Ruhezustand fotografieren.
   - Jede Maus-/Fokus-Übernahme für diesen Testdurchlauf bleibt
     genehmigungspflichtig (siehe "Nie ohne Rückfrage Fokus wegreißen oder
     Prozesse beenden" unten) - einmal ankündigen reicht, nicht bei jedem
     einzelnen simulierten Klick erneut nachfragen, aber auch nicht
     kommentarlos eine lange Kette von Klicks/Drags hindurch fortsetzen,
     ohne dass der User Gelegenheit hatte, das zu unterbrechen.
   - Findet der Test einen Fehler, den Fehler beheben und den *gleichen*
     Testschritt wiederholen, bis er tatsächlich funktioniert - erst dann
     zu Schritt 3 übergehen.
3. **Den Entwickler selbst live durchklicken lassen, nicht nur einen
   Screenshot zeigen.** Auch nach einem erfolgreichen eigenen Test reicht
   die eigene Sichtprüfung per Screenshot/simuliertem Klick (für die
   Vorab-Verifikation gedacht) für die eigentliche Freigabe-Entscheidung
   nicht aus - hier soll ein Mensch die Idee selbst bewerten und ein Gefühl
   dafür bekommen ("fühlt sich das richtig an", nicht nur "funktioniert es
   technisch"). Also: `TestHarness` sichtbar für den User starten (`dotnet
   run --project TestHarness`, im Vordergrund, nicht versteckt im
   Hintergrund) und um Feedback bitten.
4. **Erst nach Rückmeldung/Freigabe des Entwicklers** die Idee in die echte
   App übernehmen - Prototyp-Code sauber in echte Views/ViewModels
   überführen, TestHarness-spezifische Abkürzungen dabei entfernen. Die im
   `ControlPanelWindow` neu hinzugekommenen Buttons können bleiben, wenn sie
   fürs künftige Testen dieses Features nützlich sind, oder wieder entfernt
   werden, wenn sie nur für die eine Entscheidung gebraucht wurden.

## Sichtprüfung per Screenshot + simuliertem Mausklick

Da es sich um eine native Desktop-App handelt (kein Browser), funktionieren
die Browser-Tools hier nicht. Stattdessen: echte Screenshots der laufenden
`TestHarness`-Instanz und echte simulierte Mausklicks/Scrolls, um das
Verhalten so zu prüfen, wie ein Mensch es sehen würde - nicht nur anhand von
Log-Ausgaben oder Code-Lektüre. Das funktioniert grundsätzlich sowohl unter
Windows als auch unter macOS (Archipolygo ist eine Avalonia-App und läuft
auf beiden - siehe `.github/workflows/release.yml`, das auch
`osx-x64`/`osx-arm64`-Builds erzeugt); welche Variante gilt, richtet sich
einfach danach, auf welchem Betriebssystem gerade getestet wird.

Fertige Hilfsskripte dafür liegen in `scripts/` - für Windows als
PowerShell, für macOS als Bash-Pendant mit gleichem Parametermodell:

- [scripts/Take-Screenshot.ps1](scripts/Take-Screenshot.ps1) /
  [scripts/Take-Screenshot.sh](scripts/Take-Screenshot.sh) - schießt einen
  Screenshot vom Fenster eines laufenden Prozesses (per Prozessname) und
  speichert ihn als PNG.
- [scripts/Send-Click.ps1](scripts/Send-Click.ps1) /
  [scripts/Send-Click.sh](scripts/Send-Click.sh) - bewegt den echten
  Mauszeiger an Bildschirmkoordinaten und löst einen Linksklick (oder
  Scroll) aus, genau wie ein Benutzer es täte.

Typischer Ablauf für einen Testdurchlauf (Details für macOS im Abschnitt
"macOS-Variante" unten):

1. `TestHarness` starten (`dotnet run --project TestHarness` im
   Hintergrund) - siehe auch Schritt 1 im Vorgehen oben; die
   `ControlPanelWindow`-Buttons speisen die Testdaten ein.
2. Screenshot vor der Interaktion machen, um den Ausgangszustand zu sehen
   und Koordinaten für den nächsten Klick abzulesen (Screenshot mit `Read`
   ansehen wie ein Bild).
3. Mit `Send-Click.ps1` an der abgelesenen Koordinate klicken/scrollen.
4. Erneut Screenshot machen, mit `Read` ansehen und den Effekt beurteilen.
5. Schritte 2-4 wiederholen, bis der Testfall durchgespielt ist - dieselbe
   laufende Instanz weiterverwenden, nicht neu starten, solange keine
   Code-Anpassung nötig war (ein `dotnet run`-Neustart ist langsam und
   verschleiert echtes Verhalten hinter Neustart-Rauschen; nach jeder
   tatsächlichen Code-Anpassung ist ein Neustart dagegen normal und
   erwartet - siehe "Warum nicht direkt in der echten App ausprobieren"
   unten).

Beispielaufrufe:

```powershell
powershell -File .claude/skills/ui-feature-prototyp/scripts/Take-Screenshot.ps1 -ProcessName TestHarness -OutFile shot1.png
```

```powershell
powershell -File .claude/skills/ui-feature-prototyp/scripts/Send-Click.ps1 -X 640 -Y 420
```

### macOS-Variante

Gleicher Ablauf, gleiche Skript-Parameter (Prozessname, Ausgabedatei,
optionaler Titel-Teilstring; X/Y, optionales Scroll/Doppelklick) - nur die
zugrunde liegenden Werkzeuge sind andere, weil es keine Win32-API gibt:

```bash
.claude/skills/ui-feature-prototyp/scripts/Take-Screenshot.sh -p TestHarness -o shot1.png
```

```bash
.claude/skills/ui-feature-prototyp/scripts/Send-Click.sh -x 640 -y 420
```

**Voraussetzung: [cliclick](https://github.com/BlueM/cliclick)
(`brew install cliclick`).** `Send-Click.sh` führt die eigentliche
Maussimulation (Klick/Doppelklick) darüber aus statt über AppleScript/System
Events' `click at` - ohne installiertes `cliclick` bricht das Skript sofort
mit einer entsprechenden Fehlermeldung ab. `Take-Screenshot.sh` selbst
braucht `cliclick` nicht (nur Fensterabfrage + `screencapture`), aber wer auf
macOS testet, braucht es trotzdem, sobald auch geklickt werden soll.

**Einmalig nötige Berechtigungen** (Systemeinstellungen -> Datenschutz &
Sicherheit), für den Prozess, der die Skripte tatsächlich ausführt (z. B.
Terminal/iTerm, oder was auch immer Claude Code hier als Shell-Host
verwendet):

- **Bedienungshilfen (Accessibility)** - für `cliclick` selbst (meldet das
  beim Fehlen deutlich: "Accessibility privileges not enabled") und für
  `Take-Screenshot.sh`s Fensterabfrage (Position/Größe eines Fensters per
  System Events). Fehlt Letzteres, schlägt die Fensterabfrage mit
  AppleScript-Fehler `-1719` fehl ("keine Berechtigung für den
  Hilfszugriff") - das wurde beim Schreiben dieses Abschnitts tatsächlich so
  beobachtet (System Events kann zwar problemlos Prozessnamen auflisten,
  aber ohne diese Freigabe keine Fenstergeometrie abfragen).
- **Bildschirmaufnahme (Screen Recording)** - für `screencapture` selbst
  (das `Take-Screenshot.sh` intern aufruft).

Ohne `cliclick` und diese beiden Freigaben lässt sich die macOS-Variante
nicht benutzen - das ist kein Bug in den Skripten, sondern by-design auf
modernem macOS (anders als unter Windows, wo `GetWindowRect`/`SetCursorPos`
ohne Extra-Freigabe funktionieren).

**Bekannte Einschränkung: kein echtes koordinatenbasiertes Mausrad-Scrollen
- auch nicht mit cliclick.** Win32s `mouse_event(MOUSEEVENTF_WHEEL, ...)`
hat kein direktes Äquivalent auf macOS: weder AppleScript/System Events noch
das installierte `cliclick` (Version 5.1, per `cliclick -h` tatsächlich
geprüft) kennen ein Wheel-/Scroll-Kommando - `cliclick`s `w:` ist "WAIT",
nicht "wheel". `Send-Click.sh -s <Schritte>` bricht deshalb bewusst mit
einer klaren Fehlermeldung ab, statt eine nur ungefähre Tastatur-Näherung
(z. B. Pfeiltasten über `cliclick`s `kp:`-Kommando) stillschweigend als
gleichwertigen Ersatz zu präsentieren. Für reine Klick-/Doppelklick-Fälle
(die meisten UI-Prototypen hier) ist das ohne Belang - nur Szenarien, die
gezieltes Scrollen an einer bestimmten Bildschirmposition simulieren
müssen, bleiben auf macOS ungelöst; falls das doch mal gebraucht wird, erst
nachfragen statt eine Näherungslösung stillschweigend zu bauen.

### Niemals Vollbild-Screenshots - immer das eine Ziel-Fenster treffen

`Take-Screenshot.ps1` fotografiert standardmäßig nur `MainWindowHandle` -
das ist pro Prozess **genau ein** Fenster nach einer eigenen
Windows-Heuristik, nicht zwingend das gemeinte. Sobald eine `TestHarness`-
Instanz mehr als ein sichtbares Top-Level-Fenster hat (z. B. `MainWindow` +
separates `ControlPanelWindow`, oder - wie beim Multi-Window-Tabs-Prototyp -
zusätzliche, zur Laufzeit erzeugte Fenster), reicht das nicht mehr, um
gezielt eines davon zu fotografieren. Das mac-Pendant `Take-Screenshot.sh`
hat dieselbe Schwäche mit demselben Gegenmittel: ohne `-t` nimmt es einfach
"window 1" des Prozesses (ebenfalls nur eine Heuristik), mit `-t
TitleSubstring` gezielt das Fenster mit passendem Titel-Teilstring.

**Der Fehler, der diese Notiz ausgelöst hat:** um dieser Mehrfenster-
Problematik auszuweichen, wurde einmal ersatzweise der gesamte virtuelle
Bildschirm (alle Monitore) per `CopyFromScreen` über `[System.Windows.Forms.
SystemInformation]::VirtualScreen` fotografiert. Das Ergebnis zeigte neben
den Test-Fenstern auch private Discord-Chats, Browser-Tabs und weitere
Anwendungen auf den anderen Monitoren des Users - ein klarer
Privacy-Vorfall, nicht nur ein unschönes Bild.

**Richtig:** `Take-Screenshot.ps1` hat dafür den Parameter
`-TitleContains <Teilstring>` - er enumeriert alle sichtbaren Top-Level-
Fenster des Zielprozesses per `EnumWindows`/`GetWindowThreadProcessId` und
wählt gezielt das erste, dessen Titel den Teilstring enthält, statt sich
auf `MainWindowHandle` zu verlassen:

```powershell
powershell -File .claude/skills/ui-feature-prototyp/scripts/Take-Screenshot.ps1 -ProcessName TestHarness -OutFile panel.png -TitleContains "Test Control Panel"
```

Einen eindeutigen, unterscheidenden Teilstring wählen, falls mehrere
Fenstertitel sich überlappen. Wird die Fake-Verbindung über ein separates,
kleines `ControlPanelWindow` gesteuert (statt Buttons direkt im
Test-Fenster), zusätzlich die Fensterpositionen beider Fenster explizit
setzen (z. B. `WindowStartupLocation`/`Position` im Code, feste
Koordinaten für beide), sodass sich Test-Fenster und Control Panel **nie
überlappen** - sonst verdeckt das eine Fenster Teile des anderen auf jedem
Screenshot, und `Take-Screenshot.ps1` (das per Prozessname/
`MainWindowHandle` arbeitet) könnte je nach Fokus-Reihenfolge auch das
falsche der beiden Fenster fotografieren.

Falls sich auch damit ein bestimmter Screenshot-/Klick-Testfall nicht sauber
auf ein Fenster eingrenzen lässt: lieber kurz nachfragen oder eine
gezieltere Lösung suchen (z. B. das Zielfenster per `SetWindowPos`
vergrößern, um scrollen zu vermeiden), statt ersatzweise den ganzen
Bildschirm zu fotografieren.

### Bekannte Ungenauigkeit: abgelesene Klick-Y-Koordinate landet leicht zu hoch

In `ControlPanelWindow`-artigen Fenstern (`StackPanel` in einem
`ScrollViewer`, ein Button nach dem anderen) lag ein aus dem Screenshot
abgelesener Y-Wert für einen Button wiederholt ca. **5px zu hoch** - der
Klick landete knapp über dem Button statt drauf, ohne sichtbaren Fehler
(einfach keine Reaktion). Betroffen war insbesondere der letzte, am unteren
Fensterrand leicht abgeschnittene Button in einer langen Liste.

**Was NICHT geholfen hat:** ein Hover-Check per `SetCursorPos` + Screenshot,
um vor dem Klick per Hintergrundfarbe zu verifizieren, dass die Maus wirklich
auf einem Button steht. Dieses Theme zeigt auf `PointerOver` keinen sichtbar
unterscheidbaren Hover-Hintergrund (jedenfalls nicht zuverlässig per
Pixel-Diff erkennbar) - die vermeintlichen Farbabweichungen beim genaueren
Hinsehen waren nur ClearType-Subpixel-Fransen der Button-Beschriftung, kein
Hover-Indikator. Zeit in Crop/Zoom-Skripte zu stecken, um das doch irgendwie
sichtbar zu machen, war verschwendete Mühe.

**Was tatsächlich geholfen hat:** einfach ca. 5px tiefer klicken als der
Screenshot suggeriert, und am tatsächlichen Effekt (nicht an der
Cursor-/Hover-Optik) ablesen, ob der Klick gesessen hat. Bei einem
verdächtigen Nicht-Reagieren also zuerst einen leicht nach unten korrigierten
Y-Wert probieren, bevor man in aufwändigere Verifikationstechnik investiert.

### Bekannter, gefixter Bug: `Send-Click.ps1 -Scroll` mit negativem Wert

`-Scroll` mit einem negativen Wert (nach unten scrollen) warf früher einen
Cast-Fehler (`[uint32]` auf einen negativen Ausdruck). Behoben, indem der
P/Invoke-Parameter für `mouse_event`s `dwData` als `int` statt `uint`
deklariert ist - der native Aufruf bekommt exakt das gleiche Bitmuster,
PowerShell wirft dabei aber nicht mehr. Negative `-Scroll`-Werte
funktionieren seitdem wie dokumentiert.

### Debug-Infos bei Bedarf in eine Log-Datei schreiben

Nicht jeder relevante Zustand lässt sich zuverlässig an einem Screenshot
ablesen (z. B. genaue Scroll-Offsets, welche Events wann bei welchem
`GroupViewModel` ankamen, Reihenfolge von Property-Changed-Events). Für
solche Fälle im Prototyp gezielt zusätzliche Debug-Ausgaben in eine
Log-Datei schreiben (z. B. `Console.WriteLine`/`Debug.WriteLine` in eine
Textdatei umleiten, oder direkt `File.AppendAllText` an den interessanten
Stellen), an einem klar erkennbaren, temporären Ort (Scratchpad-Verzeichnis
oder direkt neben `TestHarness`), mit `Read`/`Grep` auswerten und nach dem
Testdurchlauf wieder löschen - reines Debug-Hilfsmittel für den jeweiligen
Testlauf, kein Artefakt, das im Repo oder im Scratchpad liegen bleiben soll.

## Nie ohne Rückfrage Fokus wegreißen oder Prozesse beenden

- Den `TestHarness`-Prozess nie eigenmächtig beenden (`Stop-Process`,
  `taskkill`, Fenster schließen) - immer erst den User fragen, bevor ein
  laufender Prozess terminiert wird. Das gilt auch, wenn er "sowieso nur
  zum Testen" gestartet wurde.
- `Take-Screenshot.ps1` holt das Testfenster per `SetForegroundWindow` in den
  Vordergrund, um es zu fotografieren - das reißt zwangsläufig den Fokus von
  der gerade aktiven Anwendung weg. Falls der User gerade in einer
  Vollbild-Anwendung arbeiten könnte (Spiel, Präsentation, Video),
  **vorher nachfragen**, bevor der Fokus weggenommen wird. Im Zweifel lieber
  fragen als einfach den Screenshot-Workflow laufen lassen.
- Das gilt genauso für `Send-Click.ps1`, `Take-Screenshot.ps1` (per
  `SetForegroundWindow`) und jede eigene Maus-/Tastatur-Simulation (z. B. ein
  Ad-hoc-Skript für einen Drag mit `SetCursorPos`) - die bewegen den echten
  Mauszeiger, senden echte Eingaben oder reißen den Fokus von der App weg,
  in der der User gerade liest/tippt (z. B. Claude Code selbst), unabhängig
  davon, ob gerade eine Vollbild-App läuft. Auf macOS gilt exakt dasselbe
  für `Send-Click.sh` (echter Klick per System Events) und
  `Take-Screenshot.sh` (`set frontmost to true` reißt den Fokus genauso wie
  `SetForegroundWindow`).
  **Vorfälle, die zu dieser (mehrfach verschärften) Fassung geführt haben:**
  erst wurden mehrere `Send-Click.ps1`-Aufrufe und ein Drag-Simulationsskript
  nacheinander abgefeuert, ohne zwischendurch nachzufragen. Als Reaktion
  darauf wurde einmal vorab gefragt ("darf ich X und Y tun?") und diese eine
  Zustimmung danach fälschlich für eine ganze Kette weiterer Aktionen
  (Prozess neu starten, Fenster verschieben, mehrfach Screenshot/Klick) als
  weiterhin gültig behandelt - auch das hat der User wieder als
  unangekündigte Fokus-Wegnahme empfunden.

  **Die konkrete Prüfung, die das ab jetzt verhindert:** bevor irgendeine
  Aktion ausgeführt wird, die Fenster-Fokus braucht (Screenshot per
  `SetForegroundWindow`, ein simulierter Klick/Drag per `SetCursorPos`),
  zuerst **passiv** (ohne selbst etwas zu verändern) prüfen, ob der Fokus
  bereits auf dem erwarteten Test-Fenster liegt - per `GetForegroundWindow`
  plus `GetWindowThreadProcessId`, verglichen mit dem Ziel-Prozess:

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

  Dieselbe passive Prüfung auf macOS (braucht dieselbe Bedienungshilfen-
  Freigabe wie oben unter "macOS-Variante" beschrieben):

  ```bash
  osascript -e 'tell application "System Events" to name of first application process whose frontmost is true'
  # z. B. "TestHarness" oder "Terminal"/"iTerm2"/"Code"/...
  ```

  - Ist der aktuelle Fokus bereits der erwartete Test-Prozess (der User
    schaut also schon selbst auf das Testfenster, z. B. weil er gerade
    manuell dort geklickt hat), keine Rückfrage nötig - die Aktion verändert
    nichts, was der User nicht sowieso gerade ansieht.
  - Liegt der Fokus auf irgendeinem anderen Fenster (im Normalfall: Claude
    Code selbst, wo der User liest/tippt), **davon ausgehen, dass der User
    gerade etwas anderes macht und nicht dem automatisierten Test zuschauen
    möchte** - vor der Aktion explizit in Chat-Worten fragen und auf eine
    Antwort warten, statt sich auf eine frühere, mittlerweile lose gewordene
    Zustimmung zu verlassen.
  - Ist absehbar, dass diese Prüfung im Rahmen des aktuellen Testschritts
    **mehrfach hintereinander** anschlagen wird (z. B. eine Klickfolge mit
    mehreren Screenshots dazwischen), das **vorher als Warnung ankündigen**
    ("das könnte mehrfach nachfragen, weil dabei wiederholt der Fokus zu
    TestHarness wechselt") - statt den User wiederholt überraschend zu
    unterbrechen, ohne dass er das kommen sah. Der User kann darauf mit
    einer pauschalen Freigabe für die angekündigte Reihe antworten, wenn er
    nicht bei jedem einzelnen Schritt gefragt werden möchte.
  - Auch **innerhalb** einer bereits freigegebenen Klick-/Screenshot-Reihe
    die Fokus-Prüfung von oben vor jeder einzelnen Aktion wiederholen, nicht
    nur einmal zu Beginn. Liegt der Fokus zwischendurch wieder nicht mehr auf
    dem erwarteten Test-Fenster (der User hat also inzwischen woanders
    hingeklickt/gearbeitet), gilt die frühere Freigabe nicht mehr automatisch
    weiter - erneut nachfragen, statt die erste Zustimmung stillschweigend
    auf den Rest der Sitzung auszudehnen.

## Warum nicht direkt in der echten App ausprobieren

- Eine echte Archipelago-Verbindung wäre nur ein Störfaktor beim reinen
  UI-Feedback (Netzwerk, Login-Timing, Server-Zustand).
- Ein halbfertiges Feature soll nicht versehentlich in `AvaloniaApplication1`
  landen, bevor der Entwickler es überhaupt gesehen und abgenickt hat.
- Kurzer Iterationszyklus: Anpassung → TestHarness neu starten → Entwickler
  schaut sich's an → ggf. weiter anpassen, ohne dass irgendetwas in der
  Produktiv-App zwischenzeitlich in einem Halbfertig-Zustand ist.
