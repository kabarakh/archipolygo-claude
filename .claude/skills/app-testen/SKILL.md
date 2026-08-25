---
name: app-testen
description: Wie Archipolygo (die Avalonia-Desktop-App) manuell auf UI-/Verhaltens-Bugs getestet wird - per Referenz-App ohne echte Archipelago-Verbindung, Windows-Screenshots und simulierten Mausklicks, statt die App wiederholt neu zu starten. Nutzen, wenn ein UI-Bug (Scrolling, Layout, Filter, Reihenfolge, Bindings, ...) reproduziert oder ein Fix verifiziert werden soll.
---

# Archipolygo manuell testen

Es gibt kein Testprojekt (siehe `CLAUDE.md`). UI- und Verhaltens-Bugs werden
deshalb visuell am laufenden Programm verifiziert - aber gezielt und
ressourcenschonend, nicht durch wiederholtes Durchklicken der echten App mit
einer echten Archipelago-Verbindung.

Es gibt bereits ein `TestHarness/`-Projekt im Repo-Root, das genau das
umsetzt, was Grundprinzip 1 unten beschreibt - die echte
`MainWindowViewModel`/`MainWindow`-Struktur, aber mit `FakeConnectionManager`
und `FakePersistenceService` statt echter Archipelago-Verbindung, gesteuert
über ein separates `ControlPanelWindow` mit Buttons zum Einspeisen von
Test-Events/-Hints/-Items. Für neue Testfälle diesen Harness zuerst
anschauen und wo möglich erweitern (z. B. neue Buttons im
`ControlPanelWindow` für das jeweilige Szenario), statt jedes Mal von vorne
anzufangen.

## Grundprinzip 1: Testfall als Referenz-App nachbauen, nicht live gegen Archipelago testen

Für den zu untersuchenden Fall (z. B. "viele Events schnell nacheinander,
während unten gescrollt wurde", "Filter-Kombination X+Y", "Tab-Wechsel
während ComboBox lädt") eine Reproduktion bauen statt eine echte Verbindung
zu einem Archipelago-Server aufzubauen.

Bestenfalls ist das keine separate Mini-App, sondern die **echte Avalonia-
Struktur** (`MainWindow`, `MainWindowViewModel`, `GroupViewModel`, die realen
`.axaml`-Views/Styles/Bindings) - nur die Verbindungsschicht wird ersetzt:

- Statt eines echten `ConnectionManager`/`ArchipelagoSession` eine
  **Fake-Implementierung von `IConnectionManager`** verwenden, die keine
  Netzwerkverbindung aufbaut, sondern nur die vorhandenen Events/Callbacks
  bedient (`OnLeaderMessageReceived`, `OnHintsUpdated`, ...).
- Diese Fake-Verbindung über ein paar zusätzliche Buttons im (Test-)Fenster
  "befeuern" - z. B. "1 Event hinzufügen", "50 Events auf einmal",
  "Hint hinzufügen", "Leader wechseln simulieren" - die per Klick
  synthetische `EventEntry`/`HintEntry`/`ReceivedItemEntry` in die echten
  `GroupViewModel`-Collections einspeisen.
- So laufen echte Bindings, echtes Scrolling-/Filter-/Sortierverhalten und
  die echten `.axaml`-Quirks (siehe `CLAUDE.md`) exakt wie in der
  Produktiv-App - nur ohne Netzwerk, Login-Timing oder Server-Zustand als
  Störfaktor, und ohne die echte App mehrfach neu starten zu müssen (siehe
  Grundprinzip 3). Ein Bug-Szenario lässt sich per Knopfdruck beliebig oft
  wiederholt auslösen, in derselben laufenden Instanz.
- Nur falls eine vollständige Fake-`IConnectionManager`-Implementierung für
  den konkreten Fall unverhältnismäßig aufwändig wäre, ersatzweise eine
  kleinere, eigenständige Reproduktion bauen, die die betroffenen
  View-Models direkt mit synthetischen Daten befüllt - aber die
  Wiederverwendung der echten Struktur ist der Normalfall, nicht die
  Ausnahme.
- Statt der Buttons direkt im Testfenster kann die Fake-Verbindung auch über
  ein separates, kleines "Control Panel"-Fenster gesteuert werden. In dem
  Fall die Fensterpositionen beider Fenster explizit setzen (z. B.
  `WindowStartupLocation`/`Position` im Code, feste Koordinaten für beide),
  sodass sich Test-Fenster und Control Panel **nie überlappen** - sonst
  verdeckt das eine Fenster Teile des anderen auf jedem Screenshot, und
  `Take-Screenshot.ps1` (das per Prozessname/`MainWindowHandle` arbeitet)
  könnte je nach Fokus-Reihenfolge auch das falsche der beiden Fenster
  fotografieren.

## Grundprinzip 2: Sichtprüfung per Windows-Screenshot + simulierten Mausklicks

Da es sich um eine native Desktop-App handelt (kein Browser), funktionieren
die Browser-Tools hier nicht. Stattdessen: echte Windows-Screenshots der
laufenden Referenz-App und echte simulierte Mausklicks/Scrolls, um das
Verhalten so zu prüfen, wie ein Mensch es sehen würde - nicht nur anhand von
Log-Ausgaben oder Code-Lektüre.

Fertige Hilfsskripte dafür liegen in `scripts/`:

- [scripts/Take-Screenshot.ps1](scripts/Take-Screenshot.ps1) - schießt einen
  Screenshot vom Fenster eines laufenden Prozesses (per Prozessname) und
  speichert ihn als PNG.
- [scripts/Send-Click.ps1](scripts/Send-Click.ps1) - bewegt den echten
  Mauszeiger an Bildschirmkoordinaten und löst einen Linksklick (oder Scroll)
  aus, genau wie ein Benutzer es täte.

Typischer Ablauf für einen Testdurchlauf:

1. Referenz-App aus Grundprinzip 1 starten (`dotnet run ...` im Hintergrund).
2. Screenshot vor der Interaktion machen, um den Ausgangszustand zu sehen
   und Koordinaten für den nächsten Klick abzulesen (Screenshot mit `Read`
   ansehen wie ein Bild).
3. Mit `Send-Click.ps1` an der abgelesenen Koordinate klicken/scrollen.
4. Erneut Screenshot machen, mit `Read` ansehen und den Effekt beurteilen.
5. Schritte 2-4 wiederholen, bis der Testfall durchgespielt ist - dieselbe
   laufende Instanz der Referenz-App weiterverwenden, nicht neu starten.

Beispielaufrufe:

```powershell
powershell -File .claude/skills/app-testen/scripts/Take-Screenshot.ps1 -ProcessName AvaloniaApplication1 -OutFile shot1.png
```

```powershell
powershell -File .claude/skills/app-testen/scripts/Send-Click.ps1 -X 640 -Y 420
```

### Niemals Vollbild-Screenshots - immer das eine Ziel-Fenster treffen

`Take-Screenshot.ps1` fotografiert standardmaessig nur `MainWindowHandle` -
das ist pro Prozess **genau ein** Fenster nach einer eigenen
Windows-Heuristik, nicht zwingend das gemeinte. Sobald eine Referenz-App
mehr als ein sichtbares Top-Level-Fenster hat (z. B. `MainWindow` +
separates `ControlPanelWindow`, oder - wie beim Multi-Window-Tabs-Prototyp -
zusaetzliche, zur Laufzeit erzeugte Fenster), reicht das nicht mehr, um
gezielt eines davon zu fotografieren.

**Der Fehler, der diese Notiz ausgeloest hat:** um dieser Mehrfenster-
Problematik auszuweichen, wurde einmal ersatzweise der gesamte virtuelle
Bildschirm (alle Monitore) per `CopyFromScreen` uber `[System.Windows.Forms.
SystemInformation]::VirtualScreen` fotografiert. Das Ergebnis zeigte neben
den Test-Fenstern auch private Discord-Chats, Browser-Tabs und weitere
Anwendungen auf den anderen Monitoren des Users - ein klarer
Privacy-Vorfall, nicht nur ein unschoenes Bild.

**Richtig:** `Take-Screenshot.ps1` hat dafuer den Parameter
`-TitleContains <Teilstring>` - er enumeriert alle sichtbaren Top-Level-
Fenster des Zielprozesses per `EnumWindows`/`GetWindowThreadProcessId` und
waehlt gezielt das erste, dessen Titel den Teilstring enthaelt, statt sich
auf `MainWindowHandle` zu verlassen:

```powershell
powershell -File .claude/skills/app-testen/scripts/Take-Screenshot.ps1 -ProcessName TestHarness -OutFile panel.png -TitleContains "Test Control Panel"
```

Einen eindeutigen, unterscheidenden Teilstring waehlen, falls mehrere
Fenstertitel sich ueberlappen (z. B. reicht "Archipolygo" allein nicht, wenn
sowohl das Hauptfenster als auch ein Prototyp-Fenster mit "Archipolygo (...)"
im Titel offen sind).

Falls sich auch damit ein bestimmter Screenshot-/Klick-Testfall nicht sauber
auf ein Fenster eingrenzen laesst: lieber kurz nachfragen oder eine
gezieltere Loesung suchen (z. B. das Zielfenster per `SetWindowPos`
vergroessern, um scrollen zu vermeiden), statt ersatzweise den ganzen
Bildschirm zu fotografieren.

### Bekannter, gefixter Bug: `Send-Click.ps1 -Scroll` mit negativem Wert

`-Scroll` mit einem negativen Wert (nach unten scrollen) warf frueher einen
Cast-Fehler (`[uint32]` auf einen negativen Ausdruck). Behoben, indem der
P/Invoke-Parameter fuer `mouse_event`s `dwData` als `int` statt `uint`
deklariert ist - der native Aufruf bekommt exakt das gleiche Bitmuster,
PowerShell wirft dabei aber nicht mehr. Negative `-Scroll`-Werte
funktionieren seitdem wie dokumentiert.

## Grundprinzip 3: Die eigentliche App möglichst nie neu starten

Ein `dotnet run`/App-Neustart ist langsam und verschleiert echtes Verhalten
(z. B. Startup-Sync-Timing, Reconnect-Logik) hinter Neustart-Rauschen. Daher:

- Für normale UI-/Layout-/Filter-/Sortier-Bugs: **niemals** die echte App
  mehrfach neu starten, um etwas auszuprobieren. Stattdessen die Referenz-App
  aus Grundprinzip 1 einmal starten und darin beliebig viele Interaktionen
  durchspielen (Screenshots + Klicks wie oben).
- Einzige Ausnahme: Es geht *explizit* um Verbindungs-/Reconnect-/
  Leader-Switch-/Startup-Sync-Verhalten (`ConnectionManager`,
  `InitializeGroupAsync`, `SwitchLeaderAsync`, ...), das sich nur mit einem
  echten Prozessstart beobachten lässt. Auch dann: so wenige Neustarts wie
  möglich - vorher genau überlegen, welche Zustände in einem einzigen Lauf
  nacheinander geprüft werden können, statt für jede Teilfrage neu zu
  starten.

## Grundprinzip 4: Nie ohne Rückfrage beenden, nie Fokus von Vollbild-Apps wegreißen

- Den App-Prozess (echte App *und* Referenz-App) nie eigenmächtig beenden
  (`Stop-Process`, `taskkill`, Fenster schließen) - immer erst den User
  fragen, bevor ein laufender Prozess terminiert wird. Das gilt auch, wenn
  eine App "sowieso nur zum Testen" gestartet wurde.
- `Take-Screenshot.ps1` holt das Testfenster per `SetForegroundWindow` in den
  Vordergrund, um es zu fotografieren - das reißt zwangsläufig den Fokus von
  der gerade aktiven Anwendung weg. Falls der User gerade in einer
  Vollbild-Anwendung arbeiten könnte (Spiel, Präsentation, Video),
  **vorher nachfragen**, bevor der Fokus weggenommen wird. Im Zweifel lieber
  fragen als einfach den Screenshot-Workflow laufen lassen.
- Das gilt genauso fuer `Send-Click.ps1`, `Take-Screenshot.ps1` (per
  `SetForegroundWindow`) und jede eigene Maus-/Tastatur-Simulation (z. B. ein
  Ad-hoc-Skript fuer einen Drag mit `SetCursorPos`) - die bewegen den echten
  Mauszeiger, senden echte Eingaben oder reissen den Fokus von der App weg,
  in der der User gerade liest/tippt (z. B. Claude Code selbst), unabhaengig
  davon, ob gerade eine Vollbild-App laeuft.
  **Vorfaelle, die zu dieser (mehrfach verschaerften) Fassung gefuehrt
  haben:** erst wurden mehrere `Send-Click.ps1`-Aufrufe und ein
  Drag-Simulationsskript nacheinander abgefeuert, ohne zwischendurch
  nachzufragen. Als Reaktion darauf wurde einmal vorab gefragt ("darf ich X
  und Y tun?") und diese eine Zustimmung danach faelschlich fuer eine ganze
  Kette weiterer Aktionen (Prozess neu starten, Fenster verschieben, mehrfach
  Screenshot/Klick) als weiterhin gueltig behandelt - auch das hat der User
  wieder als unangekuendigte Fokus-Wegnahme empfunden.

  **Die konkrete Pruefung, die das ab jetzt verhindert:** bevor irgendeine
  Aktion ausgefuehrt wird, die Fenster-Fokus braucht (Screenshot per
  `SetForegroundWindow`, ein simulierter Klick/Drag per `SetCursorPos`),
  zuerst **passiv** (ohne selbst etwas zu veraendern) pruefen, ob der Fokus
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

  - Ist der aktuelle Fokus bereits der erwartete Test-Prozess (der User
    schaut also schon selbst auf das Testfenster, z. B. weil er gerade
    manuell dort geklickt hat), keine Rueckfrage noetig - die Aktion
    veraendert nichts, was der User nicht sowieso gerade ansieht.
  - Liegt der Fokus auf irgendeinem anderen Fenster (im Normalfall: Claude
    Code selbst, wo der User liest/tippt), **davon ausgehen, dass der User
    gerade etwas anderes macht und nicht dem automatisierten Test zuschauen
    moechte** - vor der Aktion explizit in Chat-Worten fragen und auf eine
    Antwort warten, statt sich auf eine fruehere, mittlerweile lose
    gewordene Zustimmung zu verlassen.
  - Ist absehbar, dass diese Pruefung im Rahmen des aktuellen Testschritts
    **mehrfach hintereinander** anschlagen wird (z. B. eine Klickfolge mit
    mehreren Screenshots dazwischen), das **vorher als Warnung ankuendigen**
    ("das könnte mehrfach nachfragen, weil dabei wiederholt der Fokus zu
    TestHarness wechselt") - statt den User wiederholt ueberraschend zu
    unterbrechen, ohne dass er das kommen sah. Der User kann darauf mit
    einer pauschalen Freigabe fuer die angekuendigte Reihe antworten, wenn
    er nicht bei jedem einzelnen Schritt gefragt werden moechte.

## Grundprinzip 5: Debug-Infos in der Test-App in eine Log-Datei schreiben

Nicht jeder relevante Zustand lässt sich zuverlässig an einem Screenshot
ablesen (z. B. genaue Scroll-Offsets, welche Events wann bei welchem
`GroupViewModel` ankamen, Reihenfolge von Property-Changed-Events). Für
solche Fälle in der Referenz-App gezielt zusätzliche Debug-Ausgaben in eine
Log-Datei schreiben (z. B. `Console.WriteLine`/`Debug.WriteLine` in eine
Textdatei umleiten, oder direkt `File.AppendAllText` an den interessanten
Stellen):

- Log-Datei an einem klar erkennbaren, temporären Ort ablegen (z. B. im
  Scratchpad-Verzeichnis oder direkt neben der Referenz-App), nicht irgendwo
  im Repo verstreut.
- Nach dem Testdurchlauf die Log-Datei mit `Read`/`Grep` auswerten, um
  Screenshots durch harte Fakten zu ergänzen (exakte Werte, Zeitpunkte,
  Reihenfolge).
- Die Log-Datei nach Abschluss des Tests wieder löschen - sie ist reines
  Debug-Hilfsmittel für den jeweiligen Testlauf, kein Artefakt, das im Repo
  oder im Scratchpad liegen bleiben soll.

## Kurzfassung

1. Bug/Testfall bestenfalls im vorhandenen `TestHarness/`-Projekt nachbauen
   bzw. es erweitern - echte Avalonia-Struktur, aber Verbindungsschicht durch
   eine per Button befeuerbare Fake-Verbindung ersetzt (synthetische Daten
   statt echter Session).
2. Verhalten per echtem Windows-Screenshot + simuliertem Mausklick prüfen,
   nicht nur am Code ablesen.
3. Die eigentliche App nicht mehrfach neu starten - außer bei
   Verbindungstests, und dann so selten wie möglich.
4. Prozesse nie ohne Rückfrage beenden; vor dem Screenshot-Workflow (Fokus-
   Diebstahl!) nachfragen, falls der User in einer Vollbild-Anwendung
   arbeiten könnte.
5. Bei Bedarf Debug-Infos zusätzlich in eine temporäre Log-Datei schreiben,
   nach dem Test auswerten und anschließend wieder löschen.
6. Niemals den gesamten virtuellen Bildschirm (alle Monitore) fotografieren,
   um mehreren Fenstern einer Referenz-App auszuweichen - das kann private
   Inhalte auf anderen Monitoren erfassen. Stattdessen gezielt ein Fenster
   treffen, bei mehreren Top-Level-Fenstern per `Take-Screenshot.ps1
   -TitleContains "..."`.
