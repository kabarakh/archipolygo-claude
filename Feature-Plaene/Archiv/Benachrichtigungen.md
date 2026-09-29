# Umsetzungsplan: Einstellbare Benachrichtigungen (Blinken, Zähler, Badges)

## Status: ✅ Umgesetzt (2026-09-29)

Alle vier Schritte sind gebaut und automatisiert getestet.

**Noch nicht mit echten Servern erprobt:** Blinken, Zähler, Settings-UI und
native Badges hat der Entwickler nur kurz von Hand angesehen ("sieht ok
aus"). Das Verhalten im Alltag wird im Lauf der Zeit mit echten Welten
getestet. Der Windows-Taskleisten-Badge ist bisher auf keinem
Windows-Rechner gelaufen.

Die Ideen aus "Später" am Ende dieses Plans (Toasts, Sound, "re-alert after
N minutes") sind nicht umgesetzt. Sie liegen jetzt als eigene Skizze
`Benachrichtigungen-Erweiterungen.md` im Feature-Plan-Verzeichnis.

Die Offenen Detailfragen weiter unten sind so entschieden:
1. Chat-Absender "eigener Slot" wird über den Slot-Roster erkannt, auch für
   Geschwister-Slots, die nicht Leader sind.
2. Hint-Zeilen sind in Archipelago.MultiClient.Net ein eigener
   Nachrichtentyp und zählen nie als Chat.
3. `OtherItem` bleibt ungeteilt.

Wie die Umsetzung pro Schritt vom Plantext abweicht, steht unten. Den
Plantext ab "Ausgangslage" also nicht ungeprüft für bare Münze nehmen.

Hervorgegangen aus der groben Skizze vom 2026-09-26 (Diskussion zum
Fenster-Blinken aus `Tab-Eigenes-Fenster.md` im Feature-Plan-Archiv:
"Discord zeigt die Anzahl an und hört nach einiger Zeit auf zu blinken");
Design-Diskussion 2026-09-28.

### Umsetzung pro Schritt

- ✅ **Schritt 1** (2026-09-29): `AttentionCategory`, `AttentionTracker`
  (Kategorie-Schalter, Stumm pro Server, Drosselung), Blink-Modus in
  `WindowAttentionService`, alle Aufrufstellen umgestellt, `OtherItem`/
  `Chat`/`ChatMention` neu verdrahtet, Live-Gate für Hints, Linux-Fix.
  Abweichungen vom Plantext unten:
  - `IWindowAttentionService` bleibt als Name für die Plattform-Schicht
    erhalten (statt einer neuen `IGroupVisibility`-Abstraktion). Die
    Schnittstelle liefert jetzt Fenster als opake Objekte
    (`ResolveWindow`/`IsActive`/`WindowAcknowledged`-Event) und führt das
    Blinken aus. Der Tracker entscheidet.
  - `Report` nimmt das `GroupViewModel` statt nur die `Guid`, weil der
    Tracker den Stumm-Schalter lesen muss und ab Schritt 2 den Zähler dort
    hochzählt.
  - Das Live-Gate für Hints hängt an derselben Grace-Period wie bei
    Items (`isLeaderSession && hasAnnouncedConnection`). Für einen
    nachträglich hinzugefügten Slot gilt: Nur der erste
    `TrackHints`-Callback ist Backlog.
  - Chat-Erkennung nur für `ChatLogMessage` (echte Spieler-Chats). Hint-,
    Server- und Join/Leave-Zeilen sind in Archipelago.MultiClient.Net
    6.7.1 eigene Typen, die Detailfrage "Hint-Zeile als Mention" hat sich
    damit erledigt. Erwähnung = `SlotName` oder `Alias` eines
    konfigurierten Slots an Wortgrenzen.
  - Noch nicht umgesetzt (kommt mit Schritt 2): Zählen. Das
    `Count`-Flag pro Kategorie wird schon gespeichert, aber noch nicht
    ausgewertet.
  - Die Settings-UI fehlt noch (Schritt 3). `MainWindowViewModel.SaveSettings`
    reicht die Einstellungen aber bereits an den Tracker weiter.
- ✅ **Schritt 2** (2026-09-29): Zähler, "Gesehen"-Logik, Fenstertitel.
  Zwei Entscheidungen des Entwicklers weichen vom Plantext unten ab:
  - **Der neue Zähler ersetzt den bisherigen Tab-Zähler**
    (`UnreadEventCount`, zählte jedes Event mit `ConcernsOwnSlot` und wurde
    beim Auswählen des Tabs zurückgesetzt). Der rote Tab-Pill und die
    Dashboard-Anzeigen ("N events", "unread events") zeigen jetzt
    `GroupViewModel.UnreadAttentionCount`, also dieselbe Zahl wie im
    Fenstertitel. Einen zusätzlichen Tab-Pill (Stufe A') gibt es deshalb
    nicht.
  - **Das Dashboard zählt doch nicht als "gesehen"** (revidiert die
    Entscheidung vom 2026-09-28). Sonst stünden die Dashboard-Zahlen im
    aktiven Fenster immer auf 0. Gesehen = eigener Tab ausgewählt, Dashboard
    nicht sichtbar, Fenster aktiv, oder eigenes abgetrenntes Fenster aktiv.
  - Umsetzung: `IGroupHostWindow.IsShowingGroup` (von `MainWindow` und
    `DetachedGroupWindow` implementiert), `IWindowAttentionService.IsGroupSeen`,
    `IAttentionTracker.RefreshSeenState`. Das wird aufgerufen bei
    Tab-Wechsel, Dashboard-Umschalten, Abtrennen/Andocken und
    Fenster-Aktivierung. Bei der Aktivierung passiert es einen
    Dispatcher-Durchlauf verzögert, weil Avalonia 12.0.4 `Activated` vor dem
    Setzen von `IsActive` feuert.
  - Neue Einstellung `ShowUnreadInTitle` (Default an). Die UI dafür kommt
    mit Schritt 3.
- ✅ **Schritt 3** (2026-09-29): Settings-UI und Stumm-Schalter. Der
  Entwickler hat den TestHarness-Prototyp übersprungen (Klicktests laufen
  auf macOS nicht). Stattdessen direkt in der echten App umgesetzt,
  **manueller Test durch den Entwickler steht noch aus**.
  - `SettingsWindow`: Abschnitt "Notifications" zwischen "Event history
    limit" und dem Versionsblock. Enthält eine Blink-Modus-ComboBox, das
    Feld "Times" (nur Windows, per `SettingsViewModel.SupportsBlinkCount`,
    und nur im Modus "Blink a few times"), die Kategorie-Tabelle mit den
    Spalten Count/Blink, einen Hinweistext und die Checkbox "Show unread
    count in window title".
  - `ConnectionEditorWindow`, nur im Modus `EditGroup`: Checkbox "Mute
    notifications (no blinking, no unread count)" direkt unter
    Auto-connect. Stummschalten setzt einen bereits vorhandenen Zähler
    auf 0.
  - Rechtsklickmenü an Tab-Header und Dashboard-Zeile: Eintrag
    "Mute notifications" bzw. "Unmute notifications"
    (`MainWindowViewModel.ToggleNotificationsMuted`, speichert sofort).
    Beschriftet mit Text statt mit einem Häkchen, wie beim
    Dashboard-Umschaltknopf.
  - Stumm-Symbol neben dem Servernamen in Tab-Header und Dashboard-Zeile
    (`BellOffGeometry` in `SharedTemplates.axaml`). Statt 🔕 ist es von Hand
    als Pfad gezeichnet: Glocke mit Schrägstrich, weil Emoji je nach
    Plattform unterschiedlich oder gar nicht gerendert werden. Tooltip
    "Notifications muted for this server".
  - Das TestHarness-Control-Panel hat einen neuen Abschnitt
    "Notifications" mit Buttons, die Ereignisse direkt an den echten
    `AttentionTracker` melden. Das TestHarness nutzt dafür jetzt dieselbe
    Tracker- und Fenster-Verdrahtung wie `App.axaml.cs`, so lässt sich das
    Ganze von Hand ohne Server testen.
- ✅ **Schritt 4** (2026-09-29): native Badges. **Manueller Test pro
  Plattform steht noch aus**, headless ist nur die Logik drumherum
  abgedeckt (`UnreadBadgeTests`).
  - `IUnreadBadgeService`/`UnreadBadgeService`: Unter **macOS** setzt es
    `NSApp.dockTile.badgeLabel` über die objc-Runtime. Der Badge gilt für
    die ganze App und zeigt die Summe aller Gruppen,
    `MainWindowViewModel.AppBadgeCount`, gedeckelt bei "99+".
  - Unter **Windows** nutzt es `ITaskbarList3::SetOverlayIcon` pro Fenster:
    im Hauptfenster die angedockten Gruppen
    (`MainWindowViewModel.MainWindowBadgeCount`), in abgetrennten Fenstern
    `GroupViewModel.DetachedWindowBadgeCount`, gedeckelt bei "9+". Die
    Interface-GUID und die vtable-Reihenfolge sind mit Avalonias eigener
    `TaskBarList`-Interop (12.0.4) abgeglichen. Das Icon wird mit Avalonia
    gerendert (`RenderTargetBitmap`), als PNG an `CreateIconFromResourceEx`
    übergeben (laut Microsoft seit Vista zulässig) und pro Beschriftung
    gecacht.
  - **Stolperstein:** Avalonias Win32-Backend setzt in `RefreshIcon` selbst
    `SetOverlayIcon(hwnd, null)`, bei jedem DPI- oder Icon-Wechsel. Deshalb
    wird das Badge in `ScalingChanged` neu gesetzt (Avalonia feuert es
    direkt nach dem Löschen) und zusätzlich in `Opened`, weil das Overlay
    erst wirkt, wenn der Taskleisten-Button existiert.
  - **Linux entfällt** (Entscheidung des Entwicklers): Der
    LauncherEntry-Badge bräuchte eine `.desktop`-Datei, die das
    Release-Zip nicht mitliefert. Der Titel-Zähler deckt Linux ab.
  - Neue Einstellung `ShowUnreadBadge` (Default an), Checkbox "Show unread
    count as taskbar/Dock badge". Auf Linux ist sie ausgeblendet
    (`SettingsViewModel.SupportsUnreadBadge`).
  - Nicht mehr in diesem Plan: siehe "Später" unten (Toasts, Sound,
    "re-alert after N minutes").

## Ausgangslage (Stand 2026-09-28)

- `IWindowAttentionService.RequestAttention(Guid groupId)` blinkt das für
  die Gruppe zuständige Fenster (per `IGroupWindowLocator`), sofern es nicht
  `IsActive` ist. Feste Obergrenze seit 2026-09-26: Windows 4× Blinken
  (`FlashWindowEx`, danach bleibt der Button markiert), macOS 1× Hüpfen
  (`NSInformationalRequest`), Linux `_NET_WM_STATE_DEMANDS_ATTENTION`.
- Vier Aufrufstellen:
  - `HintService` (neuer, ungefundener Hint),
  - `SessionEventTranslator` 2× (Progression-Item an einen Geschwister-Slot
    per passiver Abdeckung; Progression-Item an einen direkt verbundenen Slot,
    nur bei `isLive`),
  - `ConnectionManager` (DeathLink-Empfang).
- **Probleme, die dieser Plan nebenbei behebt:**
  1. *Keine Drosselung:* Jeder Aufruf startet das Blinken neu. Fünf
     Progression-Items in Folge heißen fünfmal hintereinander blinken.
  2. *Hint-Auslöser nicht an Live gekoppelt:* Anders als beim Item-Auslöser
     gibt es in `HintService` kein `isLive`-Gate. Ein Catch-up-Sync mit
     Hints, die dieser Slot noch nicht gesehen hat, löst ebenfalls aus.
     Vor der Umsetzung prüfen, ob das in der Praxis passiert (z. B. beim
     Start mit mehreren Slots). Im neuen Modell gilt die Regel "nur live"
     für alle Kategorien.
  3. *Linux:* `PropModeAppend` hängt das Atom bei jedem Aufruf erneut an
     `_NET_WM_STATE` an. Das ist harmlos, aber unsauber. Vorher prüfen, ob
     es schon enthalten ist, oder mit `PropModeReplace` die bestehende Liste
     neu schreiben.

## Getroffene Entscheidungen (2026-09-28)

| Frage | Entscheidung |
|---|---|
| Was zählt / blinkt? | **Pro Kategorie einstellbar**, jeweils getrennt "Zählen" und "Blinken" (Kategorien siehe unten) |
| Wann wird zurückgesetzt? | **Pro Gruppe**, sobald ihr Tab in einem aktiven Fenster sichtbar ist. Fensterzahl = Summe der darin enthaltenen Gruppen |
| Dashboard | Ein **sichtbares Dashboard im aktiven Hauptfenster** zählt als "gesehen" für **alle angedockten** Gruppen |
| Granularität | **Global** in `settings.json` + **Stumm-Schalter pro Server** (Gruppe) |
| macOS-Blink-Modi | "N-mal" → 1× hüpfen (Informational), "Bis Fokus" → dauerhaft (Critical); das Anzahl-Feld wird auf macOS ausgeblendet |
| Umfang | Blink-Einstellung + Drosselung, Zähl-Logik mit Titel-Zähler, native Badges (Win/macOS/Linux) |

## Kategorien

`AttentionCategory` (neues Enum in `Models/`):

| Kategorie | Auslöser | Default Zählen | Default Blinken |
|---|---|---|---|
| `OwnHint` | neuer, ungefundener Hint; ein eigener Slot ist Empfänger oder Finder (`ConcernsOwnSlot`) | ✅ | ✅ |
| `DeathLink` | empfangener DeathLink | ✅ | ✅ |
| `ProgressionItem` | Progression-Item an einen eigenen Slot | ✅ | ✅ |
| `OtherItem` | Nützliches Item, Filler oder Trap an einen eigenen Slot | ❌ | ❌ |
| `ChatMention` | Chat-Nachricht eines fremden Spielers, die den Namen eines eigenen Slots enthält | ✅ | ✅ |
| `Chat` | jede andere Chat-Nachricht eines fremden Spielers | ❌ | ❌ |

- Mit diesen Defaults verhält sich die App nach dem Update fast genauso
  wie heute. Neu kommen nur die Drosselung, der Zähler und `ChatMention`
  dazu.
- `ChatMention` hat Vorrang vor `Chat`. Eine Nachricht fällt immer in genau
  eine der beiden Kategorien.
- Die Erkennung passiert per `OrdinalIgnoreCase`-Vergleich gegen die
  `SlotName`s der Gruppe (CLAUDE.md-Konvention) und nur an Wortgrenzen.
  Sonst würde z. B. der Slot "Link" bei "DeathLink" anschlagen.
- Eigene Nachrichten zählen nicht: Weder der Leader selbst noch ein anderer
  konfigurierter Slot ist Absender. Server-/Systemzeilen (Join/Leave, Goal,
  ...) sind kein `Chat`.
- **Alle Kategorien zählen nur live**, also nie aus einem Catch-up-Sync oder
  Backlog (siehe Problem 2 oben).

## Architektur

### Neuer zentraler Baustein: `IAttentionTracker`

Ersetzt `IWindowAttentionService` als das, was die vier Aufrufstellen
aufrufen. `WindowAttentionService` bleibt als reine Plattform-Schicht
darunter bestehen.

```csharp
public interface IAttentionTracker
{
    /// Called from session callbacks (any thread) - posts to the UI thread itself.
    void Report(Guid groupId, AttentionCategory category);

    /// Re-evaluates "seen" for every group - called on window activation,
    /// tab selection change, dashboard toggle, detach/dock.
    void RefreshSeenState();
}
```

Ablauf in `Report` (auf dem UI-Thread):

1. Gruppe stummgeschaltet (`ServerConnectionGroup.NotificationsMuted`) →
   Ende.
2. Kategorie weder "Zählen" noch "Blinken" → Ende.
3. Gruppe gerade **gesehen** (siehe unten) → Ende. Der Nutzer schaut schon
   hin, also weder zählen noch blinken.
4. Bei "Zählen": `GroupViewModel.UnreadAttentionCount++`.
5. Bei "Blinken" und Fenster nicht aktiv und **Drosselung** lässt es zu →
   `IWindowAttentionService.RequestAttention(window, mode, count)`.

**Drosselung:** Pro Fenster gibt es ein Flag `AttentionPending`. Es wird
gesetzt, wenn geblinkt wurde, und gelöscht, wenn das Fenster das nächste
Mal aktiv wird. Solange es gesetzt ist, wird nicht erneut geblinkt; der
Zähler läuft trotzdem weiter. Bewusst als feste Regel ohne Einstellung
(Discord-Verhalten). Falls sich im Alltag zeigt, dass man ein zweites
Blinken nach längerer Zeit vermisst, kann später eine Einstellung
"re-alert after N minutes" dazukommen.

### "Gesehen"-Logik

Eine Gruppe gilt als gesehen, wenn

- sie in einem `DetachedGroupWindow` liegt und dieses Fenster `IsActive`
  ist, **oder**
- sie angedockt ist, das Hauptfenster `IsActive` ist **und** (sie die
  `SelectedGroup` ist **oder** `IsDashboardVisible` gilt).

`RefreshSeenState()` setzt `UnreadAttentionCount = 0` für jede aktuell
gesehene Gruppe. Auslöser:

- `Window.Activated` (Hauptfenster und jedes `DetachedGroupWindow`),
- `MainWindowViewModel.OnSelectedGroupChanged`,
- `OnIsDashboardVisibleChanged`,
- Abtrennen und Andocken eines Tabs.

Damit die Logik ohne echte Fenster testbar bleibt (Kategorie A), fragt der
Tracker die Fensterzustände über eine kleine Abstraktion ab (z. B.
`IGroupVisibility`: `IsSeen(groupId)`, `ResolveWindow(groupId)`), die
`IGroupWindowLocator` plus `MainWindowViewModel` kapselt.

**Abtrennen/Andocken:** Der Zähler hängt an der Gruppe, nicht am Fenster,
wandert also automatisch mit. Nach dem Umzug ruft der Code
`RefreshSeenState()` auf. Ein frisch abgetrenntes Fenster ist in der Regel
sofort aktiv, der Zähler geht dann also auf 0.

Der Zähler wird **nicht persistiert**. Nach einem Neustart beginnt er bei 0,
für verpasste Aktivität gibt es die "neu seit letzter Sitzung"-Markierung.

### Einstellungen

`AppSettings` (additiv, ein älteres `settings.json` bekommt die Defaults
ohne Migration):

```csharp
public string AttentionBlinkMode { get; set; } = "Count";   // "Off" | "Count" | "UntilFocus"
public int AttentionBlinkCount { get; set; } = 4;           // Windows only
public Dictionary<string, AttentionCategorySetting> AttentionCategories { get; set; } = Defaults();
public bool ShowUnreadInTitle { get; set; } = true;
public bool ShowUnreadBadge { get; set; } = true;           // native taskbar/Dock badge
```

- Für den Modus wird wie bei `ThemePreference` ein einfacher String
  verwendet, weil kein `JsonStringEnumConverter` registriert ist.
- Die Dictionary-Schlüssel sind die Enum-Namen (`nameof`).
- **Achtung:** Kategorien, die im Dictionary fehlen (älteres
  `settings.json` oder eine später hinzugefügte Kategorie), müssen beim
  Lesen auf ihre Defaults zurückfallen. Dasselbe gilt für das
  `ObservableCollection`-Populate-Problem aus CLAUDE.md: prüfen, ob ein
  Dictionary-Property mit Initializer vom Deserializer ersetzt oder
  befüllt wird, und das per Test absichern.

`ServerConnectionGroup.NotificationsMuted` (bool, Default `false`) wird mit
`groups.json` persistiert.

### Plattform-Schicht (`WindowAttentionService`)

`RequestAttention(Window window, AttentionBlinkMode mode, int count)`:

| Modus | Windows | macOS | Linux |
|---|---|---|---|
| `Off` | – | – | – |
| `Count` | `FlashWindowEx`, `FLASHW_ALL`, `uCount = count` | `requestUserAttention:` Informational (10) | `DEMANDS_ATTENTION` |
| `UntilFocus` | `FLASHW_ALL \| FLASHW_TIMERNOFG` | `requestUserAttention:` Critical (0) | `DEMANDS_ATTENTION` |

- Bei "Off" wird die Plattform-Schicht gar nicht erst aufgerufen, der
  Zähler läuft aber trotzdem.
- Auf Linux gibt es keinen Unterschied zwischen den Modi. Dauer und Stärke
  bestimmt dort der Window-Manager.

## Anzeige des Zählers

### Stufe A: Fenstertitel (überall wirksam)

- Hauptfenster: `(3) Archipolygo`. Die Zahl ist die Summe über alle
  angedockten Gruppen.
- `DetachedGroupWindow`: `(3) ServerName`. Heute bindet der Titel direkt an
  `HeaderText`, künftig an ein neues `WindowTitle`-Property.
- Bei Zählerstand 0 oder `ShowUnreadInTitle = false` bleibt es beim
  bisherigen Titel.

### Stufe A': Tab-Header und Dashboard-Overview (optional, kleiner Zusatz)

- Ein kleines Zahlen-Pill am Tab-Header und an der Dashboard-Overview-Zeile
  der Gruppe. Das beantwortet "welcher Server war es?", was weder Titel
  noch Taskleisten-Badge zeigen.
- Kostet wenig, weil `UnreadAttentionCount` ohnehin am `GroupViewModel`
  hängt.
- Stil-Hinweis aus CLAUDE.md beachten: Wird das Pill per Style
  ein- und ausgeblendet, darf es keinen konkurrierenden lokalen Wert geben.

### Stufe B: Native Badges

Hinter einer eigenen Abstraktion `IUnreadBadgeService`
(`SetWindowCount(Window, int)` / `SetAppCount(int)`). Jede Plattform
bekommt sie best-effort und in `try/catch`, genau wie die Blink-Helfer.

| Plattform | API | Bezug | Aufwand/Risiko |
|---|---|---|---|
| **macOS** | `NSApp.dockTile.setBadgeLabel:` (NSString) per objc-Runtime | **App-weit** → Summe über alle Fenster | gering: gleiches Muster wie `MacWindowAttention`, zusätzlich eine NSString-Erzeugung (`stringWithUTF8String:`) |
| **Linux** | DBus-Signal `com.canonical.Unity.LauncherEntry.Update` (`count`, `count-visible`) | **App-weit** | mittel: braucht einen DBus-Client (neue NuGet-Abhängigkeit, z. B. `Tmds.DBus.Protocol`, bewusst entscheiden) und eine `.desktop`-Datei, deren Name in der `app_uri` steht. Unter Velopack/zip-Installationen existiert die evtl. nicht, dann ist das ein No-op |
| **Windows** | `ITaskbarList3::SetOverlayIcon(hwnd, hIcon, description)` (COM) | **Pro Fenster** | hoch: COM-Interop plus Rendern eines 16×16-Zahl-Icons (Avalonia `RenderTargetBitmap` → HICON, z. B. per `CreateIconIndirect`) |

Reihenfolge innerhalb dieser Stufe: macOS → Windows → Linux (nach Aufwand
und Nutzen). Linux darf auch wegfallen, wenn die neue Abhängigkeit oder das
`.desktop`-Problem es nicht rechtfertigen. Der Fenstertitel deckt Linux
bereits ab.

- **Windows-Details:**
  - Bei Zahlen > 9 wird "9+" angezeigt.
  - Das Icon wird pro Zahl gecacht und beim Ersetzen per `DestroyIcon`
    wieder freigegeben.
  - `SetOverlayIcon(hwnd, IntPtr.Zero, null)` entfernt das Overlay.
  - Ein Aufruf vor der Nachricht `TaskbarButtonCreated` ist wirkungslos.
    Beim Anlegen eines Fensters nur dann setzen, wenn der Zähler > 0 ist.
- **Vor der Umsetzung** müssen alle APIs gegen die offizielle Doku geprüft
  werden (CLAUDE.md-Grundregel). Für eine neue NuGet-Abhängigkeit gilt die
  Doku genau der gepinnten Version.

## Settings-UI

- Neuer Abschnitt "Notifications" im bestehenden `SettingsWindow`. Der
  Dialog ist aktuell 360 px breit mit `SizeToContent="Height"`; eventuell
  wird er breiter oder bekommt einen Expander.
- Inhalt:
  - Blink-Modus als ComboBox: Off / Blink N times / Until focused.
  - Anzahl als `NumericUpDown`, nur auf Windows sichtbar und nur bei "N
    times" (`ShowXxx`-Bool-Muster).
  - Kategorie-Tabelle mit den Spalten Kategorie | Count | Blink, eine
    Checkbox pro Zelle.
  - Checkboxen "Show count in window title" und "Show badge on taskbar/Dock
    icon".
- **Pro Server:** Checkbox "Mute notifications for this server" im
  `ConnectionEditorWindow`, Modus `EditGroup` (`ShowXxx`-Muster).
  Zusätzlich ein Eintrag im Tab-Kontextmenü, falls es dort schon eines gibt.
  Ein gemuteter Tab zeigt dezent ein 🔕 im Header.
- Den Aufbau vorab mit dem `ui-feature-prototyp`-Skill im TestHarness
  durchklicken, besonders die Kategorie-Tabelle und die Dialogbreite.

## Umsetzungsschritte

Jeder Schritt ist einzeln lauffähig und getestet.

1. **Tracker + Kategorien + Drosselung (ohne neue UI).**
   - `AttentionCategory`, `IAttentionTracker`/`AttentionTracker` und die
     Settings-Felder mit Defaults anlegen.
   - Die vier Aufrufstellen auf `Report(groupId, category)` umstellen und
     `OtherItem`, `Chat` und `ChatMention` neu verdrahten.
   - Live-Gate für Hints einbauen, Linux-Append-Fix, Blink-Modus in der
     Plattform-Schicht.
   - Tests (Kategorie A): Zählen/Blinken je Kategorie, Mute, Drosselung,
     kein Zählen, solange gesehen, Default-Fallback für fehlende
     Dictionary-Schlüssel, Wortgrenzen-Erkennung bei Mentions. Kategorie B:
     kein `Report` aus einem Catch-up-Sync.
2. **"Gesehen"-Logik + Titel-Zähler (+ optional Tab-Pill).**
   - `UnreadAttentionCount` am `GroupViewModel`, `RefreshSeenState`-Hooks,
     `WindowTitle` an beiden Fenstertypen.
   - Tests: Kategorie A für die Reset-Regeln (Tab, Dashboard, abgetrennt,
     Abtrennen/Andocken); Kategorie C für die Titel-Bindung.
3. **Settings-UI + Stumm pro Server.**
   - Vorher Prototyp im TestHarness.
   - Tests (Kategorie C): Dialog speichert und lädt die Einstellungen;
     `ShowXxx`-Sichtbarkeiten.
4. **Native Badges:** macOS → Windows → optional Linux. Diese Stufe lässt
   sich nur manuell pro Plattform prüfen. Die Summenbildung
   (Fenster/App-weit) dahinter ist Kategorie-A-testbar.

Nach jedem Schritt README ("Using the app") und CLAUDE.md prüfen: Wird
`IAttentionTracker` im "Where things live"-Abschnitt erwähnt, ersetzt der
Eintrag `IWindowAttentionService` als Einstiegspunkt.

## Offene Detailfragen (klein, bei der Umsetzung klären)

- Wo genau landen Chat-Nachrichten heute in `SessionEventTranslator`, und
  lässt sich "Absender ist ein eigener Slot" dort sauber bestimmen, auch
  für Geschwister-Slots, die nicht Leader sind?
- Zählt ein `ChatMention` aus der Hint-Chat-Zeile (`[Hint]: ...`) doppelt,
  also zusätzlich zu `OwnHint`? Vorschlag: Hint-Zeilen sind nie `Chat`.
- Soll `OtherItem` zwischen Nützlich, Filler und Trap weiter unterteilt
  werden? Vorerst nicht, mit Blick auf `Item-Trap-Icons.md`.

## Später (nicht umgesetzt, jetzt in `Benachrichtigungen-Erweiterungen.md`)

- **Toast-Benachrichtigungen** (Windows-Toast / `UNUserNotification` /
  libnotify) mit Inhalt ("Player X sent you Hookshot"). Deutlich mehr
  Aufwand. Unter Windows braucht es eine AUMID an einer Verknüpfung; prüfen,
  ob Velopack die schon anlegt. Die Kategorie-Tabelle könnte dann eine
  dritte Spalte "Toast" bekommen.
- **Sound** pro Kategorie, ebenfalls als weitere Spalte.
- **"Re-alert after N minutes"** für die Drosselung (siehe oben).
- **Tray-Icon:** Mit Titel- und Taskleisten-Badge ist der Teil
  "dauerhaft einsehbarer Zählerstand" aus `Tray-Icon.md` abgedeckt. Dort
  bleibt nur noch "aus der Taskleiste verschwinden" offen.
