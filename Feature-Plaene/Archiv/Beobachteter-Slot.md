# Umsetzungsplan: Beobachteter Slot

## Status: ✅ Umgesetzt

Umgesetzt am 2026-11-09. Der Rest dieser Datei ist der Plan vor der
Umsetzung. Diese Punkte weichen vom Plan ab:

- **Slot-Filter ohne neuen Optionstyp.** Die Filter behalten den Typ
  `SlotProfile?`. Ein fremder beobachteter Slot bekommt ein eigenes
  `SlotProfile` mit der Id `WatchedSlotId`. Dieses Profil steht nur in
  `SlotFilterOptions` (`_watchedFilterEntry`), nie in `Group.Slots` oder
  `Slots`. Der Grund: Die Filter lesen nur `Id` und `DisplayName`. "Chat as"
  und der Catch-up lesen nie `SlotFilterOptions`. Ein neuer Typ ändert acht
  Dateien und verbessert kein Verhalten.
- **Ein eigener beobachteter Slot hat im Slot-Filter keine Markierung.** Er
  behält seinen normalen Eintrag.
- **Hervorhebung über ein Attached Behavior.** `WatchedEventHighlight` setzt
  die Klasse `watched` am `ListBoxItem`. Ein Style-Setter kann keine Klasse
  setzen. Eine Hervorhebung im Item-Template sitzt innerhalb des Paddings
  und nicht am Rand der Zeile.
- **"Neu" über den Attention-Tracker.** `IAttentionTracker.NoteNewWatchedItems`
  merkt sich die Gruppe. `RefreshSeenState` setzt "neu" zurück, sobald der
  Benutzer die Gruppe sieht. Items aus dem Catch-up melden nichts, sind aber
  trotzdem "neu".
- **Methode B nur als Ersatz.** Der Login als beobachteter Slot läuft nur,
  wenn die Tracker-API nichts liefert und der Benutzer ihn eingeschaltet
  hat.
- **Auswahl nur mit Verbindung.** Ohne verbundenen Leader zeigt der Editor
  nur den gespeicherten Namen und einen Hinweis. Der Grund:
  `GetRoomPlayersAsync` öffnet sonst eine eigene Verbindung.
- **Zustand "aufgeklappt" global.** Er steht in `FilterBarLayoutState` und
  gilt für alle Tabs, wie die Filterleisten.
- **Nicht getestet: der Weg über die Chat-Zeile.** `ItemSendLogMessage` hat
  nur interne Konstruktoren. Darum kann ein Fake diesen Weg nicht auslösen.
  Die Tests decken Tracker, Login, Streifen, Filter, Views und Editor ab.
- **Offen: das echte JSON.** Der Parser für `player_items_received` nimmt
  Arrays und Objekte an. Eine echte Antwort eines Webhosts hat noch niemand
  geprüft.

Die Begründung für Nummer, Hints und Catch-up steht in Umsetzungsplan.md,
Abschnitt "Beobachteter Slot: Nummer, Hints und Catch-up".

## Anlass

In einem Raum kann der Benutzer mehrere eigene Slots spielen. Er will
zusätzlich einen anderen Slot verfolgen (Beispiel: `malandor`). Besonders
will er wissen, wann dieser Slot ein Progression-Item empfängt. Heute zeigt
die App Ereignisse nur für eigene Slots. Außerdem kennt
`IAttentionTracker.Report` den Slot nicht, zu dem ein Ereignis gehört.

## Entscheidungen

### Grundmodell

- **Der beobachtete Slot ist ein fremder Slot oder ein eigener Slot.** Ein
  fremder Slot ist kein `SlotProfile`. Hints, Event-Log, Received Items und
  Dashboard zeigen ihn wie einen eigenen Slot. Er hat eine sichtbare
  Markierung (siehe "Markierung"). Für einen fremden beobachteten Slot
  gelten diese Grenzen:
  - Der Benutzer kann ihn in "Chat as" nicht auswählen.
  - Er ist nie Leader.
  - Die App fragt nicht nach seinem Passwort.
  - Die Startup-Sync-Zähler zählen ihn nicht.
- **Ein eigener beobachteter Slot bleibt ein normaler eigener Slot.** Er
  bekommt zusätzlich den Streifen, die Markierung und die Benachrichtigung
  für beobachtete Slots.
- **Eine Gruppe hat höchstens einen beobachteten Slot.** Die App speichert
  seinen Namen aus dem Roster in `ServerConnectionGroup.WatchedSlotName`
  (`string?`). Vergleiche Namen mit `OrdinalIgnoreCase`.
- **Für den beobachteten Slot sind nur Progression-Items wichtig.** Das gilt
  für den Streifen und für die Benachrichtigungen. Event-Log und
  Items-Panel zeigen trotzdem alle Items des Slots, wie bei eigenen Slots.
- **Der Status "beobachtet" steuert auch die Benachrichtigungen.** So gibt
  es ein Konzept, nicht zwei.

### Antworten vom 2026-11-09

| Nr. | Frage | Entscheidung |
|---|---|---|
| 1 | Liste oder Ticker, Position? | Statische Liste über dem Event-Log. Der Benutzer kann sie einklappen. |
| 2 | Wert von n? | Eine Einstellung pro Gruppe, im Connection-Editor neben der Auswahl des beobachteten Slots. |
| 3 | Benachrichtigung? | Neue Kategorie `WatchedProgressionItem`. Die Stummschaltung der Gruppe gilt. |
| 4 | Dashboard? | Hervorhebung mit Schalter, Filter "Watched only", Zeile in der Übersicht. |
| 5 | Zeitstempel? | Nein. `ReceivedItemEntry` bekommt kein neues Feld. |
| 6 | Catch-up? | Methode A (Tracker-API). Methode B (Login) nur als Opt-in. |
| 7 | Auch eigene Slots? | Ja. Die Auswahl zeigt fremde und eigene Slots. |
| 8 | Slot mit Goal? | Die App meldet keine Items mehr. Der Streifen bleibt und zeigt den letzten Stand. |
| 9 | Item an Item-Link-Gruppe? | Das Item zählt für den beobachteten Slot. Die Zeile nennt die Item-Link-Gruppe. |
| 10 | Zeile in der Übersicht? | Nur das letzte Item, ohne Zeitangabe. |
| 11 | Eigener beobachteter Slot: welche Benachrichtigung? | Beide: die normale Item-Kategorie und `WatchedProgressionItem`. |

## Ergebnis des Prototyps

Der Prototyp liegt in `TestHarness/WatchedSlotPrototype.cs`. Er hängt die
UI zur Laufzeit in die echten Views ein. Übernimm das Aussehen, nicht den
Code. Diese Punkte weichen von der Skizze ab oder kamen dazu:

- **Markierung:** Ein Augen-Icon als Umriss (`Path`, Strich 1,4, Farbe
  `#D49A00`), kein Emoji. Der Tooltip ist "Watched slot".
  - Die Box des Icons hat das Seitenverhältnis der Geometrie (14 × 9).
    Sonst setzt `Stretch="Uniform"` das Icon an den oberen Rand der Box.
  - Das Icon hat `Margin="0,2,0,0"`. Die sichtbare Mitte der Schrift liegt
    tiefer als die Mitte der Zeilenbox.
- **Beschriftungen ohne Symbol:** "Highlight watched", "Watched only". Im
  Slot-Filter heißt der Eintrag "malandor (watched)".
- **Hervorhebung im Event-Log:** Ein Rand links (3 px, `#FFC107`) und ein
  getönter Hintergrund (`#33FFC107`). Die Klasse `watched` am
  `ListBoxItem` steuert sie. Der Schalter "Highlight watched" steht vor
  "Hide goaled".
- **Streifen zugeklappt:** Die Kopfzeile zeigt dann das neueste
  Progression-Item im Format der Zeilen. Ohne Item zeigt sie "last n
  progression items".
- **Streifen aufgeklappt:** Kopfzeile (Pfeil, Auge, Name, "last n
  progression items", Badge "n new", bei Goal 🏁 und "goal reached, no
  more notifications"). Darunter die Liste mit `MaxHeight` 140.
- **Zeile:** "NEW" (nur wenn neu), Item in der Farbe für Progression, "←",
  Absender, "(Location)" mit Opacity 0,6, bei Item-Links "via <Gruppe>"
  kursiv.
- **"Watched only" im Dashboard:** Eine echte `ListBox` wie das Event-Log
  daneben. Ein `ScrollViewer` allein hatte keinen Hintergrund und sah
  anders aus. Die Zeile ist Gruppe (in Gruppenfarbe), Auge, Slot-Name,
  dann die Zeile wie im Streifen.
- **Zeile in der Übersicht:** Auge und "malandor: Varia Suit" unter dem
  Fortschrittsbalken, eingerückt wie der Balken.
- **Connection-Editor:** Der Abschnitt "Watched slot" steht direkt unter
  "Slots". Inhalt: Auswahl ("(none)", eigene Slots zuerst mit "(own
  slot)", dann fremde Slots), "show last [n] items", ein Hilfetext und die
  Checkbox "Catch up by logging in as this slot (others see it join)".
- **Nicht im Prototyp:** Die Zeile in den Einstellungen für
  `WatchedProgressionItem` und das echte `RefreshSeenState`. Der Prototyp
  nutzte `ProgressionItem` als Ersatz.

## Tracker-API (geprüft am 2026-11-09)

Quelle: `WebHostLib/api/tracker.py` und `NetUtils.py` im Archipelago-Repo,
Branch `main`.

- `GET api/tracker/<id>` liefert `player_items_received`. Jeder Eintrag
  hat `team`, `player` und `items`. Der Cache des Endpunkts ist 60
  Sekunden.
- `items` ist eine Liste von `NetworkItem`. Das ist ein `NamedTuple` mit
  den Feldern `item`, `location`, `player` (Absender) und `flags`. Darum
  ist ein Item im JSON vermutlich ein Array `[item, location, player,
  flags]`. Prüfe das an einer echten Antwort, bevor du den Parser
  schreibst.
- `GET api/static_tracker/<id>` liefert zusätzlich `groups`. Die App liest
  Item-Link-Gruppen schon aus der Session. Dieses Feld ist darum nicht
  nötig.
- Die IDs wandelt die App mit dem Datapackage der Leader-Session in Namen
  um. Ohne Leader-Session holt die App keine Items nach.

## Umsetzung

**Halte diese Regeln aus `CLAUDE.md` ein:**
- Listen-Daten gehen über `UiUpdateQueue`. Hänge Events nur mit
  `GroupViewModel.AppendEvent` an.
- Jede UI-Änderung aus `ConnectionManager` geht über
  `Dispatcher.UIThread.Post`.
- Lies das Roster nur über `ConnectionManager.FilterToRealPlayers`.
- Ein Login geht nur über `ConnectSlotSessionAsync`.
- Setze eine Eigenschaft nicht lokal, wenn ein Style sie auch setzt.

### 1. Datenmodell und Persistenz

1. Ergänze in `ServerConnectionGroup`:
   - `WatchedSlotName` (`string?`),
   - `WatchedSlotId` (`Guid`). Die App erzeugt die Id, wenn der Benutzer
     einen fremden Slot wählt. Events, Items und Sync-State nutzen sie.
   - `WatchedItemCount` (`int`, Standard 8),
   - `WatchedCatchUpByLogin` (`bool`, Standard `false`).
2. Prüfe, dass `PersistenceService` und `ConfigExportService` die Felder
   schreiben und lesen.
3. Prüfe `ConfigImportMerger`. Ein Import mit gleichem Host und Port
   übernimmt die Felder.
4. Ergänze `RoomSlotStatus.IsWatched`. Setze das Flag für den
   beobachteten Slot und für jede Item-Link-Gruppe, in der er Mitglied
   ist. So markiert eine spätere Auswahl auch ältere Einträge.
5. Die App ermittelt die Slot-Nummer aus dem Roster, wenn der Leader
   verbindet und wenn der Benutzer die Auswahl ändert.

### 2. Slot-Filter

1. Ersetze `SlotProfile?` in `SlotFilterOptions` und in den drei Filtern
   durch einen eigenen Optionstyp (zum Beispiel `SlotFilterOption` mit `Id`,
   `DisplayName` und `IsWatched`).
2. Ändere `DashboardServerSlotFilter` und `SlotFilterDisplayConverter`
   entsprechend.
3. Ein fremder beobachteter Slot bekommt einen Eintrag mit `WatchedSlotId`.
   Ein eigener beobachteter Slot behält seinen Eintrag.
4. Ein künstliches `SlotProfile` darf nicht entstehen. Sonst landet es in
   "Chat as" und im Catch-up.

### 3. Live-Daten

1. `SessionEventTranslator.OnLeaderMessageReceived`: Ergänze einen Zweig
   für Items an den fremden beobachteten Slot und an seine
   Item-Link-Gruppen. Der Zweig nutzt `WatchedSlotId` als `SlotId`.
2. Hints: Rufe `TrackHints(slot: n)` auch für einen fremden beobachteten
   Slot auf, wie `TrackHintsForSiblingOnLeader`.
3. Ergänze `AttentionCategory.WatchedProgressionItem`. Ergänze die Zeile in
   der Tabelle der Einstellungen.
4. Melde ein Progression-Item des beobachteten Slots mit dieser Kategorie.
   Wenn der Slot ein Goal hat, melde nichts.
5. Bei einem eigenen beobachteten Slot meldet die App beide Kategorien.

### 4. Catch-up

1. Methode A: Ergänze `player_items_received` in `MultiworldTrackerService`.
2. Speichere den Index des zuletzt gesehenen Items für `WatchedSlotId` im
   Sync-State.
3. Rufe den Abruf auf, wenn die Gruppe online kommt
   (`groupJustCameOnline`), und bei "Refresh".
4. Methode B: Nur wenn `WatchedCatchUpByLogin` gesetzt ist. Hänge einen
   Eintrag an die `CatchUpQueue` an. Die App verbindet sich mit dem Tag
   `Tracker`. Sie fragt nach keinem Passwort. Wenn der Login fehlschlägt,
   schreibt sie eine Zeile ins Event-Log.
5. Ein eigener beobachteter Slot braucht keinen neuen Catch-up.
6. Items aus dem Catch-up lösen keine Benachrichtigung aus.

### 5. Streifen im Server-Tab

1. Ergänze `GroupViewModel.Watched.cs` (neue Teildatei). Sie enthält die
   Zeilen des Streifens, den Zustand "aufgeklappt" und die Zeile für die
   Übersicht.
2. Die Zeilen sind eine stabile Collection. Sie kommen über
   `UiUpdateQueue`.
3. Speichere "aufgeklappt" in `FilterLayout`, wie
   `EventsFiltersExpanded`.
4. Setze "neu" in `RefreshSeenState` zurück.
5. Ergänze den Streifen in `GroupDetailView.axaml` über dem Event-Log (Aussehen
   siehe "Ergebnis des Prototyps").
6. Ergänze die Klasse `watched` am `ListBoxItem` des Event-Logs und den
   Schalter "Highlight watched".

### 6. Dashboard

1. Ergänze den Schalter "Highlight watched" und die Klasse `watched` im
   Event-Log des Dashboards.
2. Ergänze den Filter "Watched only" in `DashboardViewModel`. Er zeigt eine
   eigene `ListBox` über alle Gruppen.
3. Ergänze die Zeile in der Übersicht.

### 7. Connection-Editor

1. Ergänze `ShowWatchedSlot` (nur Modus `EditGroup`).
2. Fülle die Auswahl mit `GetRoomPlayersAsync`. Ohne Verbindung zeigt die
   Auswahl nur den gespeicherten Namen.
3. Ergänze die Felder für n und die Checkbox für Methode B.
4. Speichere erst bei "Save". "Cancel" ändert nichts.

## Tests

Kategorien nach `Test-Umsetzungsplan.md`:

- **`[Fact]`:**
  - Parser für `player_items_received` mit einer echten Antwort als
    Fixture.
  - Vergleich mit dem Index des zuletzt gesehenen Items.
  - Persistenz und Config-Import der neuen Felder.
  - Optionstyp der Slot-Filter.
- **`[AvaloniaFact]` mit `FakeArchipelagoSession`:**
  - Ein Item an den fremden beobachteten Slot erscheint in Event-Log,
    Items und Streifen.
  - Ein Item an eine Item-Link-Gruppe mit dem beobachteten Slot zählt.
  - Ein Goal unterdrückt die Benachrichtigung.
  - Ein eigener beobachteter Slot meldet beide Kategorien.
  - Catch-up meldet nichts.
  - Methode B läuft nur mit Opt-in.
- **`[AvaloniaFact]` mit echten Views:**
  - Der Streifen ist nur mit beobachtetem Slot sichtbar.
  - Zugeklappt zeigt die Kopfzeile das neueste Item.
  - Die Klasse `watched` folgt dem Schalter.
  - Das Icon ist vertikal zentriert (wie
    `HorizontalStackPanelAlignmentTests`).
  - Der Connection-Editor speichert die Auswahl nur bei "Save".

## Spätere Ergänzungen

- Toast und Ton aus `Benachrichtigungen-Erweiterungen.md`. Dieser Fall ist
  ein guter erster Einsatz für sie.
- Ein Eintrag im Kontextmenü am Spielernamen im Event-Log ("Watch this
  slot"). Der Prototyp hat das nicht geprüft.
