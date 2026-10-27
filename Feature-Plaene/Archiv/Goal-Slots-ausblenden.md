# Umsetzungsplan: Slots mit Goal ausblenden und markieren

## Status: ✅ Umgesetzt (2026-10-27)

Die Umsetzung folgt dem Plan. Diese Punkte weichen ab oder ergänzen ihn:

- `HintEntry.ReceivingSlot` und `HintEntry.FindingSlot` sind nullable
  (`RoomSlotStatus?`). Tests erzeugen `HintEntry` ohne diese Felder. Die
  Filter prüfen darum `ReceivingSlot?.HasGoal != true`.
- Die neuen Parameter `receiver` in `IMessageHistoryService` sind optional
  (`= null`). Vorhandene Aufrufe bleiben unverändert.
- `GroupViewModel.GoaledSlotNumbers` ist neu. `OnGoalReached` braucht die
  Menge, um Item-Link-Gruppen neu zu berechnen.
- Die Hilfsmethode in `ConnectionManager` heißt `ReadGoalStatesAsync`. Sie
  schreibt Fehler mit `_diagnosticLogger.Warning`.
- `FakePlayerHelper.GetPlayerInfo(team, slot)` hat jetzt eine Logik.
  `PlayerInfo.GetGroupMembers` braucht diese Methode.
- `FakeDataStorageHelper` und `FakeRoomStateHelper` stehen zusammen in
  `AvaloniaApplication1.TestSupport/FakeDataStorageHelper.cs`.
- Alle neuen Tests stehen in `GoaledSlotTests.cs` (20 Tests). Die Datei
  `AttentionTriggerTests.cs` bleibt unverändert. Ein Test prüft die
  Benachrichtigung für Hints. Die Benachrichtigung für Items prüft kein
  Test. `ItemSendLogMessage` hat einen `internal` Konstruktor.
- Die gesamte Suite (601 Tests) ist grün.

---

## Anlass

In einem großen Raum erreichen viele Slots ihr Goal vor dem Ende des
Raums. Danach zeigt das Event-Log weiter jedes Item, das ein Slot mit Goal
empfängt. Diese Einträge sind für den Benutzer Rauschen. Dasselbe gilt für
Hints, deren Empfänger sein Goal hat. Der Benutzer will diese Einträge
ausblenden. Außerdem will er sehen, welcher Slot sein Goal hat.

## Datenquellen

Die Recherche gilt für `Archipelago.MultiClient.Net` 6.7.1 (Tag `v6.7.1`)
und für `MultiServer.py` im Archipelago-Repo.

### 1. DataStorage-Schlüssel `client_status_{team}_{slot}`

- Der Server bietet diesen Schlüssel ab Archipelago 0.4.4 an. Die Server
  des Entwicklers haben Version 0.6.7 oder 0.6.8.
- Der Schlüssel ist schreibgeschützt (`Scope.ReadOnly`, auf der Leitung
  `_read_client_status_{team}_{slot}`). Jede Session kann ihn für jeden Slot
  lesen.
- Der Wert ist ein `ArchipelagoClientState`: `ClientUnknown = 0`,
  `ClientConnected = 5`, `ClientReady = 10`, `ClientPlaying = 20`,
  `ClientGoal = 30`.
- `IDataStorageHelper.GetClientStatusAsync(int? slot, int? team)` liest den
  Wert einmal. Ein Slot, der nie verbunden war, liefert `ClientUnknown`.
- **Der Server macht `ClientGoal` nie rückgängig.** `update_client_status`
  prüft das. Auch ein neuer Login des Slots ändert den Wert nicht.
- `TrackClientStatus` gibt es auch. Es sendet pro Slot ein eigenes
  `SetNotify` und ein `Get`. Die App verwendet es nicht (siehe
  "Entscheidungen").

### 2. Live-Nachricht `GoalLogMessage`

- Wenn ein Slot sein Goal erreicht, sendet der Server eine PrintJSON-
  Nachricht vom Typ `"Goal"` an alle Clients. Der Text ist "X (Team #1) has
  completed their goal."
- Die Bibliothek macht daraus eine `GoalLogMessage`. Sie erbt von
  `PlayerSpecificLogMessage` und hat `Player` (`PlayerInfo`, mit `Slot`).
- Der Server sendet diese Nachricht vor dem Auto-Collect und vor dem
  Auto-Release. Darum kennt die App den Status schon, wenn die Release-Items
  kommen.
- Heute macht `SessionEventTranslator.OnLeaderMessageReceived` daraus ein
  `EventType.Chat`. Das bleibt so.
- Der Konstruktor von `GoalLogMessage` ist `internal`. Tests können keine
  `GoalLogMessage` erzeugen (siehe "Tests").

### 3. Seed des Raums

`IArchipelagoSession.RoomState.Seed` (`string`) nennt den Seed des Raums.
Der Plan verwendet ihn, um alte Goal-Daten zu verwerfen (siehe Schritt 2).

### 4. Item-Link-Gruppen

- `PlayerInfo.IsGroup` ist `true` für eine Item-Link-Gruppe.
- `PlayerInfo.GetGroupMembers(IPlayerHelper)` liefert die Mitglieder als
  `PlayerInfo`.
- `ConnectionManager.FilterToRealPlayers` entfernt Gruppen aus dem Roster.
  Für die Ableitung des Gruppen-Status muss die App die Gruppen darum direkt
  aus `session.Players.AllPlayers` lesen.

### 5. Webhost-Tracker `player_status`

`MultiworldTrackerService` liest diesen Wert schon für das Admin-Panel. Die
App verwendet ihn für dieses Feature nicht. Er geht nur mit `TrackerId` und
liefert keine Live-Meldungen.

## Datenstruktur heute

- `EventEntry` hat keine Slot-Nummer für Empfänger oder Finder. Es hat nur
  `SlotId` (nur für eigene Slots), `Segments` und `ConcernsOwnSlot`.
- `EventTextSegment` ist ein `record (string Text, EventTextSegmentKind
  Kind)`. Es hat keine Slot-Nummer.
- Diese Methoden erzeugen ein `EventEntry` mit einem Empfänger:
  - `MessageHistoryService.HandleChatMessage` für jede Log-Nachricht des
    Leaders, auch für `ItemSendLogMessage` und `HintItemSendLogMessage`.
  - `MessageHistoryService.HandleObservedItemForSlot` für ein Item an einen
    Sibling-Slot.
  - `MessageHistoryService.HandleItemsReceivedSinceLastConnection` für den
    Backlog eines Catch-up.
  - `HintService` für einen neuen Hint (`EventType.HintReceived`).
- `HintService` erzeugt jedes `HintEntry` (`new HintEntry`, einzige Stelle).
  `HintEntry` hat `ReceivingPlayer` und `FindingPlayer` als `int`.
- Die App speichert das Event-Log und die `HintEntry`-Liste nicht auf der
  Festplatte. Neue Felder brauchen darum keine Migration.
- `VisibleEvents` und `VisibleHints` filtern beim Anzeigen.
  `DashboardViewModel` hat eigene Filter und mischt die Rohdaten aller
  Gruppen.
- Die Templates zeigen Namen so an:
  - Event-Log: `EventTextSegmentTemplate` in `Views/SharedTemplates.axaml`.
    Das Dashboard verwendet dasselbe Template.
  - Hint-Panel: eigene `TextBlock`-Elemente für `ReceivingPlayerName` und
    `FindingPlayerName` in `Views/GroupDetailView.axaml` und
    `Views/DashboardView.axaml`.
- `AttentionTracker.Report` hat drei Aufrufe für Items und Hints:
  - `SessionEventTranslator.OnLeaderMessageReceived`: Item an einen
    Sibling-Slot.
  - `SessionEventTranslator`, Item-Empfang des Leaders (`isLive`).
  - `HintService`: neuer Hint (`AttentionCategory.OwnHint`).
- `FakeArchipelagoSession.DataStorage` und `FakeArchipelagoSession.RoomState`
  werfen heute `NotImplementedException`.

## Entscheidungen

Der Entwickler hat diese Punkte am 2026-10-27 entschieden.

- **Event-Log:** Wenn der Empfänger eines Items sein Goal hat, blendet der
  Filter den Eintrag aus. Das gilt auch, wenn ein Slot mit Goal sich selbst
  ein Item schickt. Release-Items eines Slots mit Goal an andere Slots
  bleiben sichtbar.
- **Hint-Panel:** Wenn der Empfänger eines Hints sein Goal hat, blendet der
  Filter den Hint aus. Das gilt auch, wenn ein eigener Slot der Finder ist.
- **Finder mit Goal im Hint-Panel:** Der neue Filter blendet diese Hints
  nicht aus. Der Auto-Release sendet ihre Items. Dann sind die Hints
  gefunden, und der Filter "Unfound" blendet sie aus. Diese Annahme gilt
  nur, wenn der Raum `release_mode` mit "auto" hat.
- **Eigene Slots:** Die App behandelt eigene Slots mit Goal wie fremde
  Slots.
- **Schalter:** Das Event-Log und das Hint-Panel haben je einen eigenen
  Schalter ("Hide goaled"). Er ist mit den vorhandenen Filtern
  kombinierbar.
- **Startwert:** Beide Schalter sind nach dem Start aus. Die App speichert
  den Wert nicht. Das ist dasselbe Verhalten wie bei den anderen Filtern.
- **Markierung:** Die App zeigt vor jedem Slot-Namen mit Goal das Symbol
  🏁. Das Symbol ist auch bei ausgeschaltetem Filter sichtbar.
- **Benachrichtigungen:** `AttentionTracker` meldet keine Items und keine
  Hints mehr für einen eigenen Slot mit Goal.
- **Item-Link-Gruppen:** Eine Gruppe hat Goal, wenn alle ihre Mitglieder
  Goal haben.
- **Datenquelle:** Die App liest den Status einmal pro Leader-Connect mit
  `GetClientStatusAsync` für jeden Slot. Danach wertet sie nur
  `GoalLogMessage` aus. Die App verwendet kein `TrackClientStatus`. Ein Goal
  geht nicht verloren, weil der Server `ClientGoal` nie zurücksetzt.
- **Kein Tracker als Ersatz.** Die Server haben Version 0.6.7 oder 0.6.8.
- **Kein Prototyp im Harness.** Das Feature kommt direkt in die echte App.

## Ansatz

### Kernidee: ein beobachtbares Status-Objekt pro Slot im Raum

Die App erzeugt pro Gruppe und Slot-Nummer genau ein Objekt
`RoomSlotStatus`. Jeder Eintrag, jedes Segment und jeder Hint hält eine
Referenz auf dieses Objekt. Wenn ein Slot sein Goal erreicht, ändert die App
nur `RoomSlotStatus.HasGoal`. Bindings zeigen 🏁 dann sofort, auch vor
älteren Einträgen. Die Filter lesen dasselbe Objekt.

Diese Lösung hat diese Vorteile:

- Die Einträge bleiben unveränderlich.
- Die View braucht keinen Zugriff auf `GroupViewModel`. Das Dashboard
  funktioniert darum ohne Zusatzcode.
- Ein Konverter mit Zugriff auf eine Menge ist nicht nötig.

Die View setzt das Symbol also beim Anzeigen. Das klärt den ersten offenen
Punkt der Skizze.

### Schritt 1: Modell `RoomSlotStatus`

Erzeuge `Models/RoomSlotStatus.cs`:

- `public sealed partial class RoomSlotStatus : ObservableObject`.
- `public int SlotNumber { get; }`, über den Konstruktor gesetzt.
- `[ObservableProperty] private bool _hasGoal;`

**Ändere `HasGoal` nur auf dem UI-Thread.** Bindings und Filter lesen den
Wert dort. Ein Lesezugriff aus einem Session-Callback ist erlaubt, weil ein
`bool` atomar ist.

### Schritt 2: Register in `GroupViewModel`

Erzeuge die neue Partial-Datei `ViewModels/GroupViewModel.Goals.cs`. Das
Thema "Goal-Status" passt in keine vorhandene Partial-Datei.

1. Ergänze ein Feld `ConcurrentDictionary<int, RoomSlotStatus>
   _roomSlotStatuses`. Session-Callbacks rufen das Register auf einem
   fremden Thread auf. Darum ist ein `ConcurrentDictionary` nötig.
2. Ergänze `public RoomSlotStatus GetRoomSlotStatus(int slotNumber)`. Die
   Methode liefert das vorhandene Objekt oder erzeugt ein neues
   (`GetOrAdd`).
3. Ergänze `public void ApplyGoals(string? seed, IEnumerable<int>
   goaledSlotNumbers)`. Die Methode macht diese Schritte:
   1. Wenn `seed` nicht gleich dem gespeicherten Seed ist, setzt sie jedes
      `HasGoal` auf `false`. Dann speichert sie den neuen Seed.
   2. Sie setzt `HasGoal = true` für jede Nummer in `goaledSlotNumbers`.
   3. Sie setzt `HasGoal` nie auf `false`, wenn der Seed gleich bleibt. Der
      Server macht ein Goal nie rückgängig. So überschreibt ein spätes
      Leseergebnis kein Goal aus einer `GoalLogMessage`.
   4. Sie meldet `GoalStatesChanged` (siehe Schritt 7).
4. Ergänze `public event Action? GoalStatesChanged`.

Der Seed-Vergleich schützt vor einem neuen Raum auf demselben Host und
Port. Ohne ihn behält die App die Goals des alten Raums.

### Schritt 3: Status beim Leader-Connect lesen

Ergänze `SessionEventTranslator` um `public async Task
ReadGoalStatesAsync(GroupViewModel group, IArchipelagoSession session)`:

1. Lies alle echten Slots mit `ConnectionManager.FilterToRealPlayers`.
2. Rufe für jeden Slot `session.DataStorage.GetClientStatusAsync(p.Slot,
   p.Team)` auf. Warte mit `Task.WhenAll` auf alle Ergebnisse.
3. Sammle die Slot-Nummern mit `ArchipelagoClientState.ClientGoal`.
4. Ergänze die Item-Link-Gruppen, deren Mitglieder alle Goal haben (siehe
   Schritt 4).
5. Rufe `group.ApplyGoals(session.RoomState.Seed, ...)` über
   `Dispatcher.UIThread.Post` auf.
Rufe die Methode in `ConnectionManager.SwitchLeaderAsync` nach jedem
erfolgreichen Leader-Connect auf. Die Stelle ist neben dem Aufruf von
`LeaderConnected`. Warte nicht auf die Methode (`_ = ...`).
`SessionEventTranslator` hat keinen Logger. Darum fängt eine kleine
Hilfsmethode in `ConnectionManager` jede Exception ab und schreibt sie in
`_diagnosticLogger`. Ohne Ergebnis bleibt der Filter wirkungslos. Die
Verbindung bleibt davon unberührt. Diese Regeln gelten:

- **Lies den Status ohne `_groupLocks` und ohne `ConnectGate`.** Die
  Abfrage ist ein normales Paket auf einer offenen Session. Sie ist kein
  Handshake.
- Lies den Status auch bei einem Wechsel über "Chat as". Die neue Session
  ist eine andere Session. Das zusätzliche Lesen kostet wenig und hält den
  Code einfach.
- Catch-up-Sessions lesen den Status nicht. Der Leader deckt den ganzen Raum
  ab.

### Schritt 4: Item-Link-Gruppen

Ergänze in `SessionEventTranslator` eine private Methode, die aus einer
Menge von Slot-Nummern mit Goal die Gruppen mit Goal berechnet:

- Lies die Gruppen aus `session.Players.AllPlayers.Where(p => p.IsGroup)`.
  **Leite diesen Aufruf nicht durch `FilterToRealPlayers`.** Diese Methode
  entfernt genau die Gruppen. Schreibe dafür einen kurzen Kommentar in den
  Code.
- Eine Gruppe hat Goal, wenn jedes Mitglied aus `GetGroupMembers` Goal hat.
- Ein Status für eine Gruppe selbst aus `GetClientStatusAsync` ist nicht
  nötig. Eine Gruppe verbindet nie.

Verwende die Methode in Schritt 3 und in Schritt 5.

### Schritt 5: `GoalLogMessage` live auswerten

Ergänze `SessionEventTranslator.OnLeaderMessageReceived`:

- Wenn `logMessage is GoalLogMessage goal`, rufe eine neue Methode
  `internal void OnGoalReached(GroupViewModel group, IArchipelagoSession
  session, int slotNumber)` auf.
- `OnGoalReached` berechnet die Gruppen mit Goal neu (Schritt 4). Dafür
  braucht sie die Nummern aller Slots mit Goal. Sie liest diese aus dem
  Register der Gruppe und ergänzt `slotNumber`.
- `OnGoalReached` ruft `group.ApplyGoals` über `Dispatcher.UIThread.Post`
  auf.
- Die Nachricht selbst kommt weiter als `EventType.Chat` in das Event-Log.

`OnGoalReached` ist `internal`, damit Tests sie ohne `GoalLogMessage`
aufrufen können.

### Schritt 6: Referenzen in Einträgen, Segmenten und Hints

1. Ergänze `EventTextSegment` um einen optionalen dritten Parameter
   `RoomSlotStatus? Slot = null`. Vorhandene Aufrufe bleiben gültig.
2. Ergänze `EventSegmentBuilder.BuildChatSegments` um einen Parameter
   `Func<int, RoomSlotStatus>? slotStatus`. Für jedes `PlayerMessagePart`
   setzt die Methode `Slot = slotStatus?.Invoke(playerPart.SlotId)`.
3. Ergänze `BuildHintReceivedSegments` um `RoomSlotStatus? findingSlot` und
   `RoomSlotStatus? receivingSlot`. Setze sie auf die zwei Namens-Segmente.
4. Ergänze `EventEntry` um `public RoomSlotStatus? Receiver { get; init; }`.
   Setze das Feld an diesen Stellen:

   | Stelle | Wert |
   |---|---|
   | `HandleChatMessage` (neuer Parameter) | `ItemSendLogMessage.Receiver.Slot`, sonst `null` |
   | `HandleObservedItemForSlot` (neuer Parameter) | `itemSend.Receiver.Slot` |
   | `HandleItemsReceivedSinceLastConnection` (neuer Parameter) | `session.ConnectionInfo.Slot` der Catch-up-Session |
   | `HintService`, Event für einen Hint | `snapshot.ReceivingPlayer` |

   `ItemSendLogMessage` deckt auch `HintItemSendLogMessage` und
   `ItemCheatLogMessage` ab. Beide erben davon.
5. Ergänze `HintEntry` um `ReceivingSlot` und `FindingSlot` (beide
   `RoomSlotStatus`). `HintService` setzt sie aus `snapshot.ReceivingPlayer`
   und `snapshot.FindingPlayer`.

Die Werte kommen immer aus `group.GetRoomSlotStatus(nummer)`. Das klärt den
zweiten offenen Punkt der Skizze: Die Goal-Prüfung für Hints liest
`HintEntry.ReceivingSlot.HasGoal`.

Die Catch-up-Session und die Leader-Session gehören zum selben Raum. Darum
gelten ihre Slot-Nummern im selben Register.

### Schritt 7: Filter

`GroupViewModel.Panels.cs`:

1. Ergänze `[ObservableProperty] private bool _hideGoaledEvents;` und
   `[ObservableProperty] private bool _hideGoaledHints;`. Startwert ist
   `false`. Die App speichert die Werte nicht.
2. Ergänze `VisibleEvents` um `if (HideGoaledEvents) events =
   events.Where(e => e.Receiver?.HasGoal != true);`.
3. Ergänze `VisibleHints` um `if (HideGoaledHints) hints = hints.Where(h =>
   !h.ReceivingSlot.HasGoal);`.
4. Melde `VisibleEvents` und `VisibleHints` neu, wenn sich ein Schalter
   ändert.
5. Melde beide auch bei `GoalStatesChanged`. Ein neues Goal kann ohne
   Schalter-Änderung Einträge ausblenden.

`DashboardViewModel`:

1. Ergänze dieselben zwei Schalter mit eigenem Zustand. Das folgt dem
   Muster aus `Dashboard-Event-Filter.md` im Feature-Plan-Archiv: gleiche
   Schalter, kein gemeinsamer Zustand mit den Tabs.
2. Ergänze `VisibleEvents` und `VisibleHints` um dieselben Bedingungen.
3. Abonniere `GoalStatesChanged` jeder Gruppe. Verwende dafür dieselbe
   Stelle, an der das Dashboard heute `CollectionChanged` abonniert und
   wieder abmeldet.

`FilterSummaryText`:

- Ergänze `public static string? HideGoaled(bool hide) => hide ? "No
  goaled" : null;`.
- Ergänze die vier Zusammenfassungen (Events und Hints, Tab und Dashboard).

**Received Items bleibt ohne Filter.** Das Panel zeigt nur Items eigener
Slots. Die Entscheidungen nennen dieses Panel nicht.

### Schritt 8: UI

1. Ergänze in `Views/GroupDetailView.axaml` je eine `CheckBox` "Hide
   goaled" in der `CollapsibleFilterBar` des Event-Logs und des
   Hint-Panels. Setze sie in die Zeile der vorhandenen Checkboxen oder
   Filter-Buttons.
2. Ergänze dieselben zwei Checkboxen in `Views/DashboardView.axaml`.
3. Ändere `EventTextSegmentTemplate` in `Views/SharedTemplates.axaml`:
   - Binde `Text` über eine `MultiBinding` an `Text` und `Slot.HasGoal`.
   - Ein neuer Konverter `GoalMarkerTextConverter` liefert `"🏁 " + Text`,
     wenn `HasGoal` gleich `true` ist. Sonst liefert er `Text`.
   - Der Konverter muss `UnsetValue` für `Slot.HasGoal` annehmen. Das
     passiert, wenn `Slot` gleich `null` ist.
4. Binde in beiden Hint-Templates die Namen auf dieselbe Weise an
   `ReceivingSlot.HasGoal` und `FindingSlot.HasGoal`.

Mache das Symbol nicht zu einem eigenen `TextBlock` in einem horizontalen
`StackPanel`. Das Segment-Template steht in einem `WrapPanel`. Ein
`StackPanel` gibt dem Segment unbegrenzte Breite, und `TextWrapping`
funktioniert dann nicht mehr. Eine `MultiBinding` hält den einen
`TextBlock`.

### Schritt 9: Benachrichtigungen

Prüfe an den drei Aufrufen von `AttentionTracker.Report` vorher den
Empfänger:

| Stelle | Prüfung |
|---|---|
| Item an einen Sibling-Slot (`OnLeaderMessageReceived`) | `group.GetRoomSlotStatus(itemSend.Receiver.Slot).HasGoal` |
| Item-Empfang des Leaders (`isLive`) | `group.GetRoomSlotStatus(session.ConnectionInfo.Slot).HasGoal` |
| Neuer Hint (`HintService`) | `newEntry.ReceivingSlot.HasGoal` |

Wenn die Prüfung `true` liefert, meldet die App nichts. Die Hint-Prüfung
nimmt den Empfänger. Das gilt auch, wenn ein eigener Slot der Finder ist.
So verhält sich die Benachrichtigung wie der Filter.

Direkt nach dem Leader-Connect kennt die App den Status noch nicht. In
dieser kurzen Zeit kann eine Benachrichtigung für einen Slot mit Goal noch
kommen. Das ist akzeptabel. Ein Live-Item an einen Slot mit Goal ist in
dieser Zeit selten.

## Was gleich bleibt

- `ConnectionManager` öffnet keine neue Session und ändert keine Sperren.
- Die `GoalLogMessage` bleibt ein Chat-Eintrag im Event-Log.
- Der Relevanz-Filter, der Kategorie-Filter und die Slot-Filter bleiben
  unverändert.
- Das Admin-Panel behält seinen Filter `ExcludeGoaled` mit dem Tracker.
- `PersistenceService` ändert sich nicht. `groups.json`, `settings.json` und
  `sync-state` bekommen keine neuen Felder.

## Tests

Die Kategorien stehen in `Test-Umsetzungsplan.md`.

### Testhilfen

- Erzeuge `FakeDataStorageHelper` in `AvaloniaApplication1.TestSupport/`.
  Er implementiert `IDataStorageHelper`. Nur `GetClientStatusAsync` hat
  eine echte Logik: Er liefert Werte aus einem Dictionary pro Slot-Nummer.
  Alle anderen Member werfen `NotImplementedException`.
- Ändere `FakeArchipelagoSession.DataStorage`, damit es diesen Fake
  liefert.
- `FakeArchipelagoSession.RoomState` wirft heute ebenfalls
  `NotImplementedException`. Erzeuge einen `FakeRoomStateHelper` mit
  einstellbarem `Seed`. Alle anderen Member werfen
  `NotImplementedException`.

### Kategorie A (`[Fact]`, ohne Avalonia)

- `ApplyGoals`: Ein neuer Seed setzt alle Goals zurück.
- `ApplyGoals`: Derselbe Seed setzt kein Goal auf `false`.
- `GetRoomSlotStatus` liefert für dieselbe Nummer immer dasselbe Objekt.
- Item-Link-Gruppe: Goal nur, wenn alle Mitglieder Goal haben.
- `GroupViewModel.VisibleEvents` mit `HideGoaledEvents`: Ein Eintrag an
  einen Slot mit Goal fehlt. Ein Release-Eintrag an einen Slot ohne Goal
  bleibt sichtbar. Ein Eintrag ohne `Receiver` bleibt sichtbar.
- `GroupViewModel.VisibleHints` mit `HideGoaledHints`: auch ein Hint mit
  eigenem Finder fehlt.
- Ein späteres Goal blendet einen älteren Eintrag aus.
- Dieselben Fälle für `DashboardViewModel`, mit eigenem Zustand.
- `FilterSummaryText` nennt den Schalter.
- `GoalMarkerTextConverter`: mit Goal, ohne Goal, mit `UnsetValue`.

Wenn ein Test `Application.Current` berührt, verwende `[AvaloniaFact]`
(siehe `CLAUDE.md`).

### Kategorie B (`[AvaloniaFact]`, `ConnectionManager` mit Fakes)

- Ein Leader-Connect liest den Status aller echten Slots. Slots mit
  `ClientGoal` haben danach `HasGoal`.
- Ein Wechsel über "Chat as" liest den Status erneut.
- Eine Exception in `GetClientStatusAsync` trennt den Leader nicht.
- `OnGoalReached` setzt das Goal und ergänzt die Item-Link-Gruppe.
- Keine Benachrichtigung für ein Item an einen eigenen Slot mit Goal.
  Erweitere dafür `AttentionTriggerTests.cs`.

Eine `GoalLogMessage` und eine `ItemSendLogMessage` können Tests nicht
erzeugen. Ihre Konstruktoren sind `internal`. Teste darum die Methoden
hinter dem Typ-Vergleich direkt.

### Kategorie C (`[AvaloniaFact]`, echte Views)

- Ein Segment mit Goal zeigt 🏁 vor dem Namen. Nach `HasGoal = true` zeigt
  ein schon gerenderter Eintrag das Symbol ebenfalls.
- Ein Klick auf "Hide goaled" im Event-Log und im Hint-Panel blendet die
  Einträge aus.
- Dieselbe Prüfung für das Dashboard.

Führe die Tests mit `--blame-hang-timeout 60s` aus.

## Dokumentation nach der Umsetzung

- Ergänze in `CLAUDE.md` die Liste der Partial-Dateien von `GroupViewModel`
  um `.Goals.cs`.
- Ergänze in `README.md`, Abschnitt "Using the app", die zwei Schalter und
  das Symbol 🏁. Die README ist englisch.
- Verschiebe diese Datei in das Feature-Plan-Archiv. Ergänze oben einen
  Abschnitt "Status: ✅ Umgesetzt" mit den Abweichungen vom Plan.
