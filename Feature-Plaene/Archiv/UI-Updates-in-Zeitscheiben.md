# UI-Updates in Zeitscheiben

## Status: ✅ Umgesetzt (2026-11-06)

Die Erklärung des fertigen Codes steht in `Umsetzungsplan.md`, Abschnitt
"UI-Updates in Zeitscheiben". Der Code weicht an diesen Stellen vom Plan
unten ab:

- **Hints:** `HintService` fügt Hints weiter einzeln mit `Hints.Add` ein.
  Die Deduplizierung braucht jeden neuen Hint sofort in `group.Hints`. Nur
  die Hint-Zähler und der Neuaufbau nach einem `Found`-Wechsel warten bis
  zum Ende des Batch.
- **`SyncHints`:** Die Methode legt pro Snapshot eine eigene Änderung in
  die Queue. Der `HintIndex` bleibt pro Gruppe erhalten. Der Plan hat
  diesen Punkt nicht genannt.
- **`ReceivedItems`:** Items kommen einzeln dazu. `FilteredCollection`
  hängt jedes Item ohne Neuaufbau an. `ClearReceivedItemsForSlot` entfernt
  alle Items mit einem `Reset` (`RemoveAll`).
- **Attention:** `AttentionTracker.Report` läuft durch die Queue. Eine
  zusätzliche Bündelung pro Zeitscheibe gibt es nicht. `_pendingWindows`
  verhindert schon ein zweites Blinken.
- **Entfernte Gruppen:** Der Flush prüft nicht, ob eine Gruppe noch
  existiert. Eine Änderung für eine entfernte Gruppe trifft nur ein
  `GroupViewModel` ohne View. Das alte `Post` hat sich genauso verhalten.
- **Dashboard-Hints:** Ein neuer Hint kommt hinter den letzten Hint seiner
  Gruppe. Ein `Found`-Wechsel entfernt nur diese Row.
- **Tests:** `RunJobs()` führt `Background`-Jobs aus. Darum gibt es keine
  Methode `FlushAll()`.
- **`SelectionModel`:** Ein Test mit ausgewählter Zeile und echter
  `GroupDetailView` prüft die Range-Meldungen. Eine eigene Prüfung der
  Avalonia-Quelle gab es nicht.
- **Nicht gebaut:** die optionale Anzeige "Processing N messages..." und die
  Messung mit einem `DispatcherTimer` (Abschnitt "Messen"). Die Tests
  prüfen die Zahl der Meldungen und Eingaben zwischen den Zeitscheiben.
  Eine Zeitmessung mit einem echten Server fehlt.

Status vor der Umsetzung: 📝 Bereit zur Umsetzung.

## Problem

Ein Release oder ein großer Catch-up erzeugt tausende Log-Zeilen in kurzer
Zeit. Die App friert dann für viele Sekunden ein. Der Benutzer kann nicht
klicken und sieht keine neuen Zeilen.

Die Ursache liegt im UI-Thread, nicht im Netzwerk:

- `MessageHistoryService.AddEvent` macht für jede Zeile einen eigenen
  `Dispatcher.UIThread.Post`.
- Jedes `Events.Add` löst `OnPropertyChanged(nameof(VisibleEvents))` aus.
- `VisibleEvents` ist ein LINQ-`IEnumerable` ohne eigene Änderungsmeldung.
  Darum kopiert `ItemsSourceView` bei jeder Meldung die ganze gefilterte
  Liste neu (Avalonia 12.0.4, `ItemsSourceView.SetSource`).
- `DashboardViewModel` hört auf `Events` jeder Gruppe. Bei jeder Zeile
  erzeugt es neue `DashboardEventRow`-Objekte für alle Gruppen und sortiert
  sie mit `OrderBy`.
- `Hints` und `ReceivedItems` haben dasselbe Muster.
  `RaiseHintAggregatesChanged` zählt bei jedem Hint dreimal alle Hints.

Eine Zeile kostet also Arbeit proportional zur ganzen Liste. Bei
`EventHistoryLimit = 2000` und 3000 Zeilen sind das Millionen Schritte.

## Ziel

- Die App bleibt bei jedem Burst bedienbar.
- Der Benutzer sieht die Zeilen laufend ankommen. Die Liste wächst sichtbar
  und springt nicht erst am Ende.
- Bei wenig Verkehr erscheint jede Zeile sofort. Es gibt keine feste
  Verzögerung.
- Filter, Goal-Markierung, Stick-to-bottom und "Copy from here" verhalten
  sich wie vorher.

## Entscheidung: kein festes Zeitfenster

Ein festes Fenster (zum Beispiel 500 ms sammeln, dann senden) löst das
Problem nicht allein. Ein Flush mit 2000 Zeilen kostet heute 2000 volle
Neuaufbauten. Außerdem verzögert ein festes Fenster jede normale
Chat-Zeile.

Die Lösung hat darum drei Teile:

1. Ein app-weiter Update-Puffer sammelt alle UI-Änderungen.
2. Der Puffer arbeitet in Zeitscheiben mit `DispatcherPriority.Background`.
3. Jede Liste verarbeitet eine Zeitscheibe mit genau einer
   Änderungsmeldung.

Teil 3 bringt den größten Gewinn. Teil 2 sorgt dafür, dass der Benutzer die
Zeilen ankommen sieht.

## Teil 1: Update-Puffer (`UiUpdateQueue`)

Ein neuer Service `Services/UiUpdateQueue.cs` (Interface `IUiUpdateQueue`)
ersetzt die einzelnen `Dispatcher.UIThread.Post`-Aufrufe für Listen-Daten.

- `Enqueue(GroupViewModel group, Action apply)` legt eine Änderung in eine
  `ConcurrentQueue` und ist von jedem Thread aus erlaubt.
- Der erste `Enqueue` nach einer leeren Queue plant genau einen Flush. Alle
  weiteren `Enqueue` planen nichts. Ein `Interlocked`-Flag schützt das.
- Die Reihenfolge aller Änderungen bleibt erhalten, auch über `Events`,
  `Hints` und `ReceivedItems` hinweg. Darum gibt es nur eine Queue für die
  ganze App.

Diese Aufrufer müssen den Puffer verwenden:

- `MessageHistoryService.AddEvent`, `TrackReceivedItem`,
  `HandleObservedItemForSlot`, `ClearReceivedItemsForSlot`.
- `HintService.SyncHints` und `AddHintFromChat`.
- `AttentionTracker.Report` (siehe Teil 4).

Diese Aufrufer bleiben bei `Dispatcher.UIThread.Post`:

- `ConnectionState`, Leader-Wechsel, `LeaderConnected`,
  `ServerReplyReceived` und andere Einzelmeldungen. Sie sind selten und
  billig.

**`SessionEventTranslator` baut Segmente, Roster und Snapshots weiter im
Hintergrund-Thread.** Nur das Einfügen in die Collections läuft im
UI-Thread.

## Teil 2: Flush in Zeitscheiben

Der Flush läuft mit `DispatcherPriority.Background`. In Avalonia 12.0.4
ist `Background` (-2) niedriger als `Input` (-1) und `Render` (4). Darum
verarbeitet Avalonia Eingaben und zeichnet ein Frame, bevor der nächste
Flush beginnt.

Ablauf eines Flush:

1. Starte eine `Stopwatch`.
2. Öffne für jede betroffene Gruppe einen Batch (`group.BeginUpdateBatch()`).
3. Nimm Änderungen aus der Queue und führe sie aus.
4. Stoppe nach `SliceBudget` (Startwert 8 ms) oder bei leerer Queue.
5. Schließe alle offenen Batches. Erst jetzt melden die Listen ihre
   Änderungen (Teil 3).
6. Wenn die Queue nicht leer ist, plane den nächsten Flush mit `Background`.

Ergebnis:

- Bei wenig Verkehr enthält ein Flush eine Zeile. Sie erscheint im nächsten
  Durchlauf des Dispatchers.
- Bei einem Burst wächst die Liste etwa einmal pro Frame um einen Block.
  Der Benutzer sieht die Zeilen laufen und kann dazwischen klicken.

`SliceBudget` ist eine Konstante in `UiUpdateQueue`. Mache sie nicht zu
einer Einstellung. Der Benutzer kann den Wert nicht sinnvoll wählen.

Optional: `UiUpdateQueue.PendingCount` als beobachtbare Zahl. Ab etwa 200
wartenden Änderungen zeigt die Statusleiste "Processing N messages...".
Diese Anzeige ist nicht Teil der ersten Umsetzung.

## Teil 3: Eine Meldung pro Zeitscheibe

### `RangeObservableCollection<T>`

Ein neuer Typ in `Models/` (oder `ViewModels/`) erbt von
`ObservableCollection<T>`.

- `AddRange(IReadOnlyList<T>)` meldet ein `Add` mit allen neuen Items.
- `RemoveRangeAtStart(int count)` meldet ein `Remove` mit allen entfernten
  Items.
- `ResetTo(IEnumerable<T>)` ersetzt den Inhalt und meldet ein `Reset`.

`VirtualizingStackPanel.OnItemsChanged` in Avalonia 12.0.4 verarbeitet
`NewItems.Count > 1` und `OldItems.Count > 1`. Prüfe vor der Umsetzung,
dass `ListBox`-Selektion (`SelectionModel`) Range-Meldungen auch
verarbeitet. Lies dazu die Quelle beim Tag `12.0.4`.

### Batch in `GroupViewModel`

`GroupViewModel.Panels.cs` bekommt `BeginUpdateBatch()` und
`EndUpdateBatch()`. Während eines Batch gilt:

- `AppendEvent(EventEntry)` sammelt das Event in einer Pending-Liste.
- Das Trimmen auf `EventHistoryLimit` passiert einmal in
  `EndUpdateBatch`.
- `Hints`, `ReceivedItems` und `Found`-Wechsel setzen nur ein Dirty-Flag.
- `EndUpdateBatch` ruft einmal `AddRange`, einmal `RemoveRangeAtStart` und
  einmal `RaiseHintAggregatesChanged` auf.

Außerhalb eines Batch (Tests, seltene Aufrufer) wirkt jeder Aufruf sofort.
Er verhält sich dann wie ein Batch mit einer Änderung.

### Gefilterte Listen inkrementell

`VisibleEvents`, `VisibleHints` und `VisibleReceivedItems` werden echte
Collections. Ein neuer Typ `FilteredCollection<T>` hält eine Quelle, ein
Prädikat und eine eigene `RangeObservableCollection<T>`.

- Wenn die Quelle ein `Add` meldet, prüft er nur die neuen Items. Er hängt
  die passenden Items mit einem `AddRange` an.
- Wenn die Quelle ein `Remove` am Anfang meldet, entfernt er dieselben
  Items vorne. Die Reihenfolge ist gleich, darum reicht ein Vergleich vom
  Anfang.
- Wenn sich ein Filter ändert, baut er die Liste neu (`ResetTo`).
- Ein Goal-Wechsel (`GoalStatesChanged`) und ein `Found`-Wechsel bei
  aktivem Filter lösen ebenfalls `ResetTo` aus. Höchstens einmal pro
  Zeitscheibe.

Die Properties behalten den Typ `IEnumerable<T>`. Sie geben immer dieselbe
Instanz zurück. **`OnPropertyChanged(nameof(VisibleEvents))` darf nach
einem Add nicht mehr kommen.** Sonst kopiert `ItemsSourceView` die Liste
wieder neu. Filterwechsel melden die Property auch nicht mehr. Die
Collection meldet dann `Reset`.

`EventFilterSummaryText` und `RightPanelFilterSummaryText` hängen heute an
der `VisibleEvents`-Meldung (`OnPropertyChanged`-Override). Melde sie
direkt aus den Filter-Handlern.

### Dashboard

`DashboardViewModel.VisibleEvents` und `VisibleHints` bekommen dieselbe
Technik über alle Gruppen.

- Neue Events einer Gruppe haben den neuesten `Timestamp`. Darum hängt das
  Dashboard sie an das Ende an. `OrderBy` entfällt beim Anhängen.
- Wenn eine Gruppe Events trimmt, liegen die Rows verteilt in der Liste.
  Das Dashboard entfernt sie in einem Durchlauf und meldet einmal `Reset`.
- Wenn das Dashboard nicht sichtbar ist (`IsDashboardVisible == false`),
  setzt es nur ein Dirty-Flag. Beim Einblenden baut es die Liste einmal
  neu.

**Das Dashboard darf keine neuen `DashboardEventRow`-Objekte für
vorhandene Events erzeugen.** Ein `Dictionary<EventEntry, DashboardEventRow>`
hält die vorhandenen Rows. Sonst baut die `ListBox` alle Container neu.

## Teil 4: Attention-Meldungen bündeln

`AttentionTracker.Report` läuft heute pro Item als eigener Post. Leite die
Meldung über `UiUpdateQueue`. Innerhalb einer Zeitscheibe zählt jede
Kombination aus Gruppe und Kategorie einmal für das Blinken. Die
Unread-Zahl zählt weiter jede Meldung. `UnreadAttentionCount` meldet sich
einmal pro Zeitscheibe.

## Was gleich bleibt

- Catch-up-Backlog meldet weiter keine Attention.
- `JumpToNewestButton` bleibt unverändert. Ein Range-Add löst ein
  `ScrollChanged` aus, und Stick-to-bottom greift wie bisher.
- "Copy from here" liest `VisibleEvents`. Die Property bleibt ein
  `IEnumerable<EventEntry>` mit derselben Reihenfolge.
- `EventHistoryLimit` gilt weiter pro Gruppe.

## Risiken

- **Reihenfolge:** Wenn ein Aufrufer am Puffer vorbei postet, kann eine
  Zeile vor einer älteren Zeile erscheinen. Darum müssen alle
  Listen-Änderungen durch `UiUpdateQueue` laufen.
- **Gruppe entfernt:** Eine Änderung in der Queue kann eine entfernte
  Gruppe betreffen. Der Flush ignoriert Änderungen für Gruppen, die nicht
  mehr in `MainWindowViewModel.Groups` sind.
- **Tests:** Viele Tests erwarten Einträge direkt nach
  `Dispatcher.UIThread.RunJobs()`. `RunJobs` führt auch
  `Background`-Jobs aus. Prüfe das in Avalonia 12.0.4. Wenn nötig, bekommt
  `UiUpdateQueue` eine Methode `FlushAll()` für Tests.
- **Scroll-Anker:** Die Workaround-Logik in `JumpToNewestButton` bezieht
  sich auf das Trimmen oben. Prüfe Stick-to-bottom mit einem Burst von
  5000 Zeilen.

## Schritte

1. Baue `RangeObservableCollection<T>` und `FilteredCollection<T>`.
   Schreibe `[Fact]`-Tests für Add, Trim und Reset.
2. Stelle `Events`, `Hints` und `ReceivedItems` auf
   `RangeObservableCollection<T>` um.
3. Stelle `VisibleEvents`, `VisibleHints` und `VisibleReceivedItems` auf
   `FilteredCollection<T>` um. Entferne die Property-Meldungen nach Adds.
4. Baue `BeginUpdateBatch`/`EndUpdateBatch` in `GroupViewModel`.
5. Baue `UiUpdateQueue` mit Zeitscheiben. Registriere den Service in
   `App.axaml.cs`.
6. Leite `MessageHistoryService`, `HintService` und `AttentionTracker`
   über den Puffer.
7. Stelle das Dashboard auf inkrementelle Listen um.
8. Schreibe einen `[AvaloniaFact]`-Test: 5000 Zeilen in die Queue, dann
   flushen. Der Test zählt die `CollectionChanged`-Meldungen von
   `VisibleEvents`. Erwartet: weniger als 50, nicht 5000.
9. Schreibe einen `[AvaloniaFact]`-Test mit der echten `GroupDetailView`:
   Burst, dann prüfen, dass Stick-to-bottom am Ende steht.
10. Ergänze `Umsetzungsplan.md` um einen Abschnitt "UI-Updates in
    Zeitscheiben". Ergänze `CLAUDE.md` um einen Gotcha: Listen-Daten nie
    direkt posten, immer `UiUpdateQueue`.

## Messen

Miss vor und nach der Umsetzung mit demselben Burst:

- Zeit vom ersten bis zum letzten Eintrag in `Events`.
- Längste Zeit ohne verarbeitete Eingabe (Dispatcher-Lücke).

Ein einfacher Weg: `FakeArchipelagoSession` sendet 5000
`ItemSendLogMessage` in einer Schleife. Ein `DispatcherTimer` mit 16 ms
protokolliert seine echten Abstände. Ziel: kein Abstand über 50 ms.
