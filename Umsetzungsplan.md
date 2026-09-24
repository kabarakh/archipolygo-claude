# Umsetzungsplan: Archipelago Multi-Connection Client (Avalonia/C#)

Konkretisierung von `Archipelago_Avalonia_Projektplan_v2.md` zu ausführbaren Implementierungsschritten. Basis: vorhandenes Projekt `AvaloniaApplication1` (.NET 10, Avalonia 12.0.4, CommunityToolkit.Mvvm 8.4.1).

## Benötigte zusätzliche Pakete

- `Archipelago.MultiClient.Net` (6.7.1) – offizieller .NET-Client für Archipelago-Server, übernimmt WebSocket-Verbindung, Item/Hint/Chat-Events, Session-Verwaltung.
- `System.Text.Json` ist bereits Teil von .NET 10, kein zusätzliches Paket für die Profil-/Hint-Persistierung nötig.

## Phase 1 – Projektgerüst, MVVM, Tabs, Profilverwaltung

1. NuGet-Referenz `Archipelago.MultiClient.Net` zum `.csproj` hinzufügen.
2. Ordnerstruktur anlegen: `Models/`, `Services/`, `ViewModels/`, `Views/` (Models-Ordner existiert bereits leer).
3. Models erstellen:
   - `ServerProfile` (Id, Name, Host, Port, SlotName, Password, AutoConnect) – als einfache Klasse mit `[ObservableObject]`-Attribut oder POCO, je nachdem ob sie im Editor live gebunden wird.
   - `ConnectionState`-Enum (Disconnected, Connecting, Connected, Reconnecting, Error).
   - `ProfileSyncState` (pro `ServerProfile`, separat persistiert): `LastSeenItemIndex` (int, höchster verarbeiteter Item-Index), `SeenHintIds` (`HashSet<int>` aller bereits bekannten Hint-Ids). Wird in Phase 2/4 befüllt, siehe dort.
4. `PersistenceService` implementieren: Laden/Speichern von `List<ServerProfile>` als JSON in einer Datei im AppData-Verzeichnis (`Environment.SpecialFolder.ApplicationData`). Zusätzlich Laden/Speichern von `ProfileSyncState` pro Profil-Id (eigene JSON-Datei oder eigener Bereich in der Profildatei), damit `LastSeenItemIndex` und `SeenHintIds` über Programmstarts hinweg erhalten bleiben (wird in Phase 2/4 befüllt).
5. `MainWindowViewModel`: `ObservableCollection<TabViewModel> Tabs`, `AddProfileCommand`, `RemoveProfileCommand`, lädt Profile beim Start über `PersistenceService`.
6. `TabViewModel`-Grundgerüst: hält `ServerProfile`, `IsConnected`, Platzhalter-Properties für spätere Phasen.
7. `MainWindow.axaml`: `TabControl` mit `ItemsSource="{Binding Tabs}"`, Tab-Header zeigt `ServerProfile.Name`.
8. `ConnectionEditorViewModel` + zugehörige View (Dialog/Flyout) zum Anlegen/Bearbeiten eines Profils (Host, Port, SlotName, Password, AutoConnect).
9. Manuelles Testen: Profile anlegen, Tabs erscheinen, Persistenz über Neustart prüfen.

## Phase 2 – Einzelverbindung, Eventanzeige

1. Model `EventEntry` (Timestamp, Text, Type) anlegen; `Type` als Enum (Connected, Disconnected, ItemReceived, HintReceived, Chat, Error). Zusätzliches Feld `IsNewSinceLastSession` (bool) für die Markierung aus Schritt 3.
2. `ConnectionManager`-Service:
   - Methode `ConnectAsync(ServerProfile)` → erstellt `ArchipelagoSession` via `ArchipelagoSessionFactory.CreateSession(host, port)`, ruft `session.TryConnectAndLogin(...)` auf.
   - Abonniert relevante Events der Session (`Socket.PacketReceived`, `Items.ItemReceived`, `MessageLog.OnMessageReceived` o. ä., je nach Library-API in 6.7.1).
   - Methode `DisconnectAsync(ConnectionTab)`.
3. `MessageHistoryService` – Item-Index-Tracking ergänzen:
   - Beim erfolgreichen Connect liefert `session.Items.AllItemsReceived` (bzw. äquivalente API in 6.7.1) die vollständige, indexierte Liste aller je empfangenen Items für diesen Slot, unabhängig davon, ob der Client währenddessen offline war.
   - `ProfileSyncState.LastSeenItemIndex` für dieses Profil aus dem `PersistenceService` laden.
   - Alle Items mit Index > `LastSeenItemIndex` werden als `EventEntry` mit `IsNewSinceLastSession = true` angezeigt (das schließt Items ein, die während der Abwesenheit empfangen wurden, nicht nur live waempfangene).
   - Live während der Session neu eintreffende Items (`Items.ItemReceived`-Event) werden ebenfalls mit `IsNewSinceLastSession = true` erzeugt, da sie per Definition neuer als der gespeicherte Stand sind.
   - Beim Verbindungsende (oder periodisch alle paar Sekunden, um Datenverlust bei Absturz zu vermeiden) `LastSeenItemIndex` auf den höchsten gesehenen Index setzen und über `PersistenceService` speichern.
   - Rohe Events (`Socket.PacketReceived`, Chat, Error etc.) werden wie bisher 1:1 in `EventEntry` umgewandelt, ohne Index-Tracking.
4. `TabViewModel` erweitern: `ObservableCollection<EventEntry> Events`, `ConnectCommand`, `DisconnectCommand`, `IsConnected`-Status aus `ConnectionManager`-Callback aktualisieren.
5. View: Eventliste als `ListBox`/`ItemsControl` mit Timestamp + Text, farbliche Markierung nach `Type` via `IValueConverter` oder Style-Selector. Zusätzlich visuelle Hervorhebung (z. B. fetter Text, Punkt-Icon, andere Hintergrundfarbe) für Einträge mit `IsNewSinceLastSession = true`.
6. Testen: Client verbinden, einige Items während der Verbindung empfangen lassen, trennen, weitere Items vom Server auslösen (während Client offline ist), erneut verbinden – die während der Abwesenheit empfangenen Items müssen als "neu" markiert sein, ältere bereits gesehene nicht.

## Phase 3 – Mehrere parallele Verbindungen, Ungelesen-Markierung

1. `ConnectionManager` so erweitern, dass er eine `Dictionary<Guid, ArchipelagoSession>` für mehrere gleichzeitige Sessions verwaltet (Thread-sicher, z. B. `ConcurrentDictionary`).
2. `ConnectionTab`-Model ergänzen: `HasUnreadEvents`, `IsConnected`, Referenz auf `ArchipelagoSession`.
3. `TabViewModel`: beim Empfang neuer Events in nicht-aktivem Tab `HasUnreadEvents = true` setzen; `MainWindowViewModel` setzt es beim Tab-Wechsel (`SelectedTabChanged`) auf `false`.
4. UI: Tab-Header-Template mit Badge/Punkt-Indikator, gebunden an `HasUnreadEvents` (z. B. `IsVisible`-Binding auf einen kleinen Kreis neben dem Titel).
5. Testen: zwei Profile gleichzeitig verbinden, Events in inaktivem Tab erzeugen, Markierung prüfen, beim Tab-Wechsel verschwinden lassen.

## Phase 4 – Hint-System, Filterung, Badge

1. Model `HintEntry` (Key, ReceivingPlayer, FindingPlayer, ItemName, LocationName, Found, ReceivedAt) anlegen. Zusätzliches Feld `IsNewSinceLastSession` (bool). Hinweis: Archipelago-Hints haben laut Library (`Hint`-Modell) keine native Id; `Key` ist ein synthetischer Schlüssel aus `ReceivingPlayer`/`FindingPlayer`/`ItemId`/`LocationId`, der einen Hint eindeutig identifiziert.
2. `HintService`:
   - Synchronisiert Hint-Daten über `session.Hints.TrackHints(...)` (liefert bei jeder Änderung die vollständige aktuelle Hint-Liste, analog zu `AllItemsReceived`) in `ObservableCollection<HintEntry>` pro Tab.
   - Aktualisiert `Found`-Status bei entsprechenden Events (HintEntry ist dafür `ObservableObject` mit `[ObservableProperty] Found`, damit die UI ohne Collection-Reset aktualisiert).
   - Persistiert Hints über `PersistenceService` (JSON pro Profil-Id, getrennt von der Profilliste).
   - Hint-Index-Tracking (analog zu Items, siehe Phase 2): Beim Connect liefert der Server die vollständige aktuelle Hint-Liste für den Slot. `ProfileSyncState.SeenHintIds` für dieses Profil laden. Jeder Hint, dessen `Id` noch nicht in `SeenHintIds` enthalten ist, wird mit `IsNewSinceLastSession = true` markiert – das deckt sowohl Hints ab, die während der Abwesenheit erzeugt wurden, als auch neue Hints, die live während der Session eintreffen.
   - Da Archipelago-Hints keinen garantiert lückenlosen fortlaufenden Index haben (anders als Items), wird nicht mit einem einzelnen `LastSeenHintIndex`, sondern mit der Menge `SeenHintIds` aller bereits angezeigten Hint-Ids verglichen.
   - Beim Verbindungsende (oder periodisch) alle aktuell bekannten Hint-Ids in `SeenHintIds` übernehmen und über `PersistenceService` speichern; Set kann dabei wachsen – optional bei sehr langer Nutzung auf gefundene/erledigte Hints begrenzen, um die Datei klein zu halten.
3. `TabViewModel` erweitern: `Hints`, `VisibleHints` (gefiltert), `SelectedHintFilter` (Enum: All, Unfound), `UnfoundHintCount`; `VisibleHints` als abgeleitete Collection (z. B. via `ObservableCollection`-Refresh oder `DynamicData`, falls eingebunden – sonst manuelles Neufiltern bei Änderungen).
4. View: Hint-Liste unterhalb/neben Eventliste (`Grid` mit zwei Bereichen wie im Plan skizziert), Filter-ToggleButtons oder ComboBox für All/Unfound. Hints mit `IsNewSinceLastSession = true` analog zu neuen Items visuell hervorheben (gleicher Stil wie in Phase 2, zur Konsistenz idealerweise gemeinsamer Converter/Style).
5. Tab-Header-Template um Badge erweitern: `"{ServerProfile.Name} ({UnfoundHintCount})"`.
6. Testen: Hints empfangen, Found-Status durch Item-Funde aktualisieren, Filter umschalten, Persistenz über Reconnect/Neustart prüfen.

## Phase 5 – Nachrichten senden, Auto-Reconnect, Einstellungen

1. `TabViewModel`: `SendMessageCommand` ruft `ConnectionManager.SendMessageAsync(tab, text)` → `session.Say(text)` (oder äquivalente API).
2. Eingabefeld + Senden-Button in der View, nur aktiv wenn `IsConnected`.
3. Auto-Reconnect: `ConnectionManager` reagiert auf `Socket`-Disconnect-Event, prüft `ServerProfile.AutoConnect`, versucht mit Backoff (z. B. 5s, 10s, 30s) erneut zu verbinden; Status im `ConnectionState`-Enum sichtbar machen.
4. `SettingsViewModel` + View: globale Einstellungen (z. B. Standard-AutoConnect, Historie-Limit aus den Erweiterungen vorbereiten).
5. Testen: Server kurz stoppen/starten, Reconnect-Verhalten beobachten; Nachricht senden und im Serverlog/Chat verifizieren.

## UI: Tab-Gruppierung nach Host/Port

Tabs werden nicht alphabetisch oder nach Erstellungsreihenfolge angezeigt, sondern so, dass alle Verbindungen mit identischer Host/Port-Kombination immer direkt nebeneinander stehen (z. B. mehrere Slots auf demselben lokalen Server). `MainWindowViewModel` hält dazu `Tabs` weiter als flache `ObservableCollection<TabViewModel>`, ordnet sie aber nach jedem Hinzufügen/Bearbeiten eines Profils per `ObservableCollection.Move` (nicht Clear+Add, um `SelectedTab` zu erhalten) so um, dass nach (Host, Port) gruppiert wird; innerhalb einer Gruppe und zwischen Gruppen bleibt die bisherige Reihenfolge so stabil wie möglich erhalten (`GroupBy` ist stabil).

## Querschnittliche Aufgaben (während aller Phasen)

- Dependency Injection: `Microsoft.Extensions.DependencyInjection` für Services (ConnectionManager, PersistenceService, MessageHistoryService, HintService) registrieren und in ViewModels injizieren statt sie statisch zu instanziieren.
- Fehlerbehandlung: Verbindungsfehler als `EventEntry` vom Typ `Error` im jeweiligen Tab anzeigen, nicht nur loggen.
- UI-Thread-Sicherheit: Alle Updates aus Archipelago-Callbacks über `Dispatcher.UIThread.Post`/`InvokeAsync` an ObservableCollections weiterleiten.
- Unit-Tests für `PersistenceService`, `HintService`-Filterlogik und `MessageHistoryService` (xUnit-Projekt ergänzen).

## Reihenfolge / Abhängigkeiten

Phase 1 → 2 → 3 sind strikt sequenziell (Mehrfachverbindung baut auf Einzelverbindung auf). Phase 4 (Hints) kann teilweise parallel zu Phase 3 begonnen werden, sobald `ConnectionManager` aus Phase 2 steht. Phase 5 ist unabhängig und kann nach Phase 3 erfolgen.

Status: Phasen 1–5 sind implementiert. Die ursprünglich für Phase 6 geplanten Punkte (Log-Export, Desktop-Benachrichtigungen, Tray-Icon) wurden gestrichen und nicht umgesetzt – **die Bezeichnung „Phase 6" wurde stattdessen für einen späteren, deutlich größeren Umbau des Verbindungsmodells wiederverwendet**, der unten dokumentiert ist. Alle Code-Kommentare, die auf „Phase 6" verweisen, meinen diesen Umbau, nicht die ursprünglich hier geplanten (und verworfenen) Punkte.

## Phase 6 – Server/Slot/Leader-Umbau (tatsächlich umgesetzt)

Grundlegende Änderung am Verbindungsmodell: vorher war ein `ServerProfile` (Host+Port+SlotName+Password) eine eigenständige, unabhängige Verbindung – bei mehreren Slots im selben Raum lief also eine Session pro Slot gleichzeitig. Das wurde ersetzt durch:

1. **`ServerConnectionGroup`** (ein Tab = ein Archipelago-Raum: Host, Port, gemeinsames Passwort, `AutoConnect`, `PreferredLeaderSlotId`) und **`SlotProfile`** (nur noch `SlotName` + optionales Passwort-Override) als eigene Modelle. Ein `ServerProfile` alter Prägung entspricht jetzt einer Gruppe mit genau einem Slot.
2. `PersistenceService` migriert eine vorhandene alte `profiles.json` einmalig automatisch in das neue `groups.json`-Format (`MigrateLegacyProfilesIfNeeded`), wobei Profile mit gleichem Host+Port zu einer Gruppe zusammengefasst werden und ihre alte `Id` als neue `SlotProfile.Id` übernehmen, damit bereits gespeicherter Sync-Status (letzter gesehener Item-Index, gesehene Hint-Ids) weiter gültig bleibt.
3. **Nur noch eine Live-Verbindung pro Gruppe** (`ConnectionManager`, `_leaderSlotByGroup`): der aktuell verbundene Slot heißt „Leader". Ein Leader-Wechsel (`SwitchLeaderAsync`) verbindet den neuen Leader zuerst und trennt den alten erst danach – bewusste kurze Überlappung, damit es keine sichtbare Lücke gibt.
4. **Passive Aktualisierung der Nicht-Leader-Slots**: Da die Leader-Session den gesamten Raum-Chat/Log sieht, werden Item-Sends und Hints, die einen anderen konfigurierten Slot betreffen, direkt in dessen Historie gespiegelt (`OnLeaderMessageReceived`/`OnHintsUpdated`), ohne dass dafür eine eigene Verbindung nötig ist.
5. **Catch-up-Sync** (`CatchUpSyncAsync`): ein Slot, der während der App-Abwesenheit (oder seit dem Hinzufügen) nichts mitbekommen hat, verbindet sich kurz, holt den Rückstand nach und trennt sich sofort wieder. `InitializeGroupAsync` macht das beim Start für jeden Nicht-Leader-Slot, mit Verzögerung zwischen Gruppen (`StartupGroupSpacing`), damit ein Neustart mit mehreren Servern nicht alle gleichzeitig mit Login-Versuchen bombardiert.
6. **Automatisches Persistieren der Leader-Wahl**: jeder erfolgreiche Leader-Connect (egal ob über das Account-Dropdown, beim Anlegen des ersten Slots oder nach einem Auto-Reconnect) setzt `AutoConnect=true`/`PreferredLeaderSlotId` auf der Gruppe und meldet das über `IConnectionManager.GroupPersistNeeded` – das Auto-Connect-Verhalten ist also nicht mehr nur eine Checkbox beim Server-Anlegen, sondern folgt automatisch jeder tatsächlichen Verbindung. Ein manuelles Disconnect setzt `AutoConnect` genauso automatisch wieder zurück.

## Nach Phase 6 – weitere Erweiterungen des Verbindungsmodells

Direkt auf dem Phase-6-Umbau aufbauend, in loser Reihenfolge umgesetzt:

- **Mehrere Slots mit weniger Klicks hinzufügen**: der „Add slot"-Dialog erlaubt jetzt, mehrere Slots aus dem Raum-Roster in eine Warteliste (`StagedSlots`) zu stellen (optional mit eigenem Passwort-Override pro Slot) und beim Bestätigen alle auf einmal hinzuzufügen, statt einen Dialog-Durchlauf pro Slot zu brauchen.
- **Sequenzielles Catch-up beim Mehrfach-Hinzufügen**: werden mehrere Slots auf einmal hinzugefügt, läuft ihr Catch-up-Sync nacheinander statt gleichzeitig (`MainWindowViewModel.CatchUpNewSlotsSequentiallyAsync`) – gleichzeitig hätte den Server mit zu vielen parallelen Verbindungsversuchen überlastet.
- **Slot-Verwaltung im „Edit server"-Dialog**: Übersicht aller konfigurierten Slots eines Servers mit Auswahl des Standard-Leaders (`Make default`) und Entfernen einzelner Slots (`RemoveSlotFromGroup`), ohne dass dafür der ganze Server gelöscht werden muss. Entfernen trennt bei Bedarf zuerst die laufende Verbindung.
- **Sortierung der Slot-Listen**: das Account-Dropdown, die drei Slot-Filter und die Slot-Verwaltung zeigen Slots alphabetisch an, mit dem (aktuellen bzw. Standard-)Leader jeweils an erster Stelle (`GroupViewModel.RefreshSlotOrder`, `ConnectionEditorViewModel.SortSlotsForDisplay`).
- **Filterung des Raum-Rosters**: Slot 0 (vom Protokoll für den Server selbst reserviert, taucht oft wörtlich als Spieler „Server" auf) und Item-Link-Gruppen-Einträge werden aus jeder Spielerliste herausgefiltert (`ConnectionManager.FilterToRealPlayers`), damit sie nicht versehentlich als echter Slot konfigurierbar sind.
- **Mehrfachauswahl + Sammel-Kopieren**: Events- und Hints-Liste unterstützen unabhängig voneinander Mehrfachauswahl und Kopieren aller markierten Zeilen auf einmal (Strg/Cmd+C).
- **Transiente Verbindungsfehler werden wiederholt**: schlägt ein Connect/Login mit `TaskCanceledException`/`OperationCanceledException`/`TimeoutException` fehl, versucht `ConnectSlotSessionAsync` es mit einer frischen Session bis zu zweimal erneut, bevor der Fehler tatsächlich gemeldet wird (deckt u. a. einen beobachteten Fehlerfall ab: Leader-Wechsel kurz nach dem Reconnect eines anderen Slots). Ein echter Login-Fehler (falsches Passwort) wird nie wiederholt.
- **Fortschrittsanzeige beim Start**: ein neues Event (`IConnectionManager.SlotInitialSyncCompleted`) meldet nach jedem beim Start synchronisierten Slot Fortschritt; `MainWindowViewModel` zählt mit und `MainWindow.axaml` zeigt währenddessen eine Leiste „Catching up slots: N/M" inklusive Fortschrittsbalken an.
- **GitHub-Actions-Release-Workflow** (`.github/workflows/release.yml`): bei jedem veröffentlichten GitHub Release werden automatisch selbstständige Single-File-Builds für Windows, Linux und macOS (x64 und arm64) erzeugt und dem Release als Downloads angehängt.

Kein eigenes Testprojekt vorhanden (die unter „Querschnittliche Aufgaben" oben erwähnten Unit-Tests wurden nicht umgesetzt).

## „Add slot"-Dialog: Mehrfachauswahl mit Suche statt ComboBox + „Add to list"

Ausgearbeitet in einer Chat-Session, als Vergleich mit der Slot-Auswahl in
`J:\dev\Archipelago`s `multi_slot_tracker_webui` (`SlotPicker.vue`) und der
zugehörigen apworld (`worlds\multi_slot_tracker`) – dieser Tracker beobachtet
Slots nur passiv und braucht daher kein Passwort, weshalb dessen Muster hier
nicht 1:1 übernommen werden kann. Zuerst als Prototyp im `TestHarness`
durchgeklickt (siehe unten), nach Freigabe durch den Entwickler direkt in
`ConnectionEditorViewModel`/`ConnectionEditorWindow.axaml` umgesetzt – ist
also jetzt der reale „Add slot"-Dialog der App.

**Ziel:** Beim Hinzufügen mehrerer Slots auf einmal (großer Room-Roster)
nicht mehr einen ComboBox-Eintrag nach dem anderen einzeln auswählen und per
„Add to list" einzeln in die Warteliste stellen müssen.

**UI (ersetzt ComboBox + „Add to list" in `ConnectionEditorMode.AddSlot`):**

1. Freitext-Suchfeld filtert die Liste der verfügbaren Slots live nach
   Slot-Name/Anzeigename (analog `SlotPicker.vue`s `filtered`-Computed).
2. Darunter eine Checkbox-Liste der (gefilterten) verfügbaren Slots statt
   der ComboBox – Mehrfachauswahl ohne Zwischenschritt.
3. Zwei Buttons „Select visible" / „Deselect visible", die nur auf die
   *aktuell gefilterte* Teilmenge wirken (1:1 aus `SlotPicker.vue`
   übernommen) – deckt sowohl „ein paar gezielt suchen und anhaken" als auch
   „alle passenden auf einen Schlag" ab, ohne zwei getrennte Bedienwege zu
   brauchen.
4. Bestätigen-Button bleibt deaktiviert, solange nichts ausgewählt ist, mit
   Hinweistext (analog `SlotPicker.vue`s `canApply`/Hinweis „Select at least
   one slot to continue").

**Passwort – der Punkt, der beim Vue-Picker fehlt, weil der nur beobachtet
statt sich einzuloggen:**

- **Kein** globales Batch-Passwort-Feld in diesem Dialog: Das gemeinsame
  Server-Passwort der Gruppe ist zu diesem Zeitpunkt schon bekannt – es wird
  beim allerersten Verbinden eines Leaders für diese Gruppe abgefragt, also
  *vor* jeder Slot-Auswahl (`ConnectionEditorMode.NewGroup`/erster Slot).
  Ein zusätzliches Passwort-Feld in diesem Dialog wäre daher nur ein
  zweites, redundantes Eingabefeld für denselben Wert.
- Stattdessen bleibt genau das, was schon vor diesem Redesign existierte:
  pro Zeile ein optionales, standardmäßig eingeklapptes Override-Feld
  (kleines 🔒-Icon zum Aufklappen) für die seltenen Ausnahme-Slots, die auf
  einem custom-gehosteten Multiworld ein eigenes, abweichendes Passwort
  brauchen.
- Vorrangregel beim tatsächlichen Hinzufügen: Pro-Slot-Override (falls
  getippt) > kein Override (Slot fällt beim Verbinden auf das gemeinsame
  Passwort der Gruppe zurück – das ist bereits das bestehende Verhalten,
  keine neue Logik).

**Status:** Zuerst als anklickbarer Prototyp in `TestHarness/AddSlotPickerPrototype/`
erprobt (mit synthetischen Spielernamen statt echtem Room-Roster; siehe
`ControlPanelWindow`, ehemaliger Abschnitt „Add-Slot Redesign (Prototyp)").
Nach Durchklicken/Freigabe durch den Entwickler direkt umgesetzt in
`Models/SelectableSlotRow.cs`, `ViewModels/ConnectionEditorViewModel.cs`
(`FilteredSlotRows`/`SelectVisible`/`DeselectVisible`/`BuildSlotsToAdd`) und
`Views/ConnectionEditorWindow.axaml` – das ist jetzt der reale Dialog, den
`ConnectionEditorMode.AddSlot` anzeigt.

## ConnectionManager: Locking, Nebenläufigkeit und Workarounds im Detail

Diese Sektion sammelt die längeren Warum-Begründungen, die früher direkt als
Kommentare in `Services/ConnectionManager.cs` standen. Die Datei war auf über
2100 Zeilen angewachsen, wovon fast die Hälfte Kommentar war; die
ausführliche Begründung für jede nicht-offensichtliche Stelle steht jetzt
hier, im Code bleibt nur noch ein Ein-Satz-Verweis auf den passenden
Unterabschnitt. Reine "was tut diese Methode"-Doku bleibt weiterhin direkt
im Code.

### Item-Backlog-Grace-Period und Retry-Delay (Testbarkeit)

`_itemBacklogGracePeriod` und `_transientConnectRetryDelay` sind über den
Konstruktor überschreibbar und defaulten auf die schon immer verwendeten
Werte (2s Backlog-Grace, 2s Retry-Delay). Grund: Die Fakes aus
`AvaloniaApplication1.TestSupport` (`FakeArchipelagoSession`/`FakeSessionFactory`)
machen zwar jeden Connect/Login über eine `TaskCompletionSource`
deterministisch steuerbar, aber diese zwei Delays sind reine `Task.Delay`-
Aufrufe ohne Bezug zu einer Session – ohne die Override-Möglichkeit würde
jeder Test, der einen Catch-up-Sync oder einen transienten Retry berührt,
mehrere Sekunden lang wirklich warten. Die Test-DI übergibt nahezu
Null-Werte, `App.axaml.cs` (echte App) übergibt die Parameter nie und bekommt
so die echten Defaults.

### Warum der Item-Backlog eine Grace-Period braucht

Der Server schickt den initialen `ReceivedItems`-Rückstand als separate
WebSocket-Nachricht *nach* dem `Connected`-Paket. Das bedeutet, `LoginAsync()`
kann zurückkehren (und die await-Fortsetzung weiterlaufen), bevor alle
Backlog-Items ihr `ItemReceived`-Event gefeuert haben. Würde
`hasAnnouncedConnection` sofort auf `true` gesetzt, könnte die Fortsetzung
mitten in einem Burst von `ItemReceived`-Aufrufen laufen und den Rest des
Bursts fälschlich als "live" statt "Backlog" einstufen. Die zwei Sekunden
Grace-Period stellen sicher, dass der synchrone Burst vollständig durch ist,
bevor umgeschaltet wird; in der Praxis dauert der Burst nur Millisekunden.

### `_sessions`: Lifecycle und maximale gleichzeitige Einträge

Schlüssel ist `SlotProfile.Id`. Ein Eintrag existiert, solange für diesen
Slot irgendeine aktive Verbindung besteht: als Gruppen-Leader, als
kurzlebige Catch-up-/Switch-Ziel-Session, oder als bewusst offengehaltene
Hint-Picker-Probe-Session (siehe `RunAsSlotAsync`s `keepAlive`-Parameter und
`ReleaseHeldSessionAsync`), solange der Slot im Picker ausgewählt bleibt.
Für dieselbe Gruppe können höchstens zwei oder drei Einträge gleichzeitig
existieren (alter + neuer Leader während eines Wechsels, Leader + eine
Catch-up-Session, oder Leader + eine gehaltene Hint-Picker-Probe für einen
anderen Slot).

### `_missingLocationsBySlot`: bewusst nur im Speicher

Wird bei jedem erfolgreichen Connect (Leader wie Catch-up) aktualisiert und
für den Leader per `CheckedLocationsUpdated` live gehalten. Absichtlich
nicht in `groups.json` persistiert (anders als die reinen Zähler
`LocationsChecked`/`LocationsTotal` auf `SlotProfile`) – eine vollständige
Location-Liste würde die Datei bei einem großen Spiel erheblich aufblähen,
und sie lässt sich beim nächsten Connect ohnehin billig neu aufbauen. Das
ist, was `GetHintableLocationsAsync` erlaubt, für jeden seit App-Start
mindestens einmal verbundenen Slot sofort zu antworten, ohne eigens dafür
einen Probe-Connect zu brauchen.

### `_roomInfoBySlot`

Der `RoomInfoPacket` aus dem letzten `ConnectAsync()`-Aufruf pro Slot, nur
damit `GetHintableItemsAsync` `DataPackageChecksums` lesen kann, ohne einen
eigenen Netzwerk-Roundtrip zu brauchen. Wird bei jedem (Re-)Connect
überschrieben; ein veralteter Eintrag zwischen zwei Connects ist harmlos,
weil `GetHintableItemsAsync` immer nur im Moment eines frischen
`RunAsSlotAsync`-Aufrufs für genau diesen (gerade verbundenen) Slot liest.

### `_connectCancellationSources`

GroupId → das `CancellationTokenSource` des gerade einzigen laufenden
Connect-Versuchs dieser Gruppe (Leader-Connect aus `SwitchLeaderAsync` oder
Catch-up-Dip aus `CatchUpSyncAsync` – laut Invariante "Slots verbinden sich
nie gleichzeitig" nie beides zugleich). `DisconnectGroupAsync` cancelt dieses
Token, *bevor* es auf `_groupLocks` wartet, damit ein manuelles Disconnect
einen feststeckenden/wiederholenden Connect tatsächlich unterbricht, statt
sich stillschweigend dahinter einzureihen, bis die Retries von selbst
aufgeben.

### `_autoReconnectSuppressed`

Wird unmittelbar vor einem bewussten `DisconnectGroupAsync` gesetzt und erst
durch ein späteres explizites `SwitchLeaderAsync` (Account-Dropdown, Start)
wieder gelöscht. Bewusst *nicht* von `OnSocketClosed` konsumiert/entfernt,
weil manche Socket-Implementierungen `SocketClosed` bei einem einzelnen
bewussten Close mehrfach feuern – ein Flag, das sich selbst zurücksetzt,
würde nur das erste dieser Events unterdrücken.

### Disconnect: Reihenfolge Cancel vor Lock

`DisconnectGroupAsync` setzt das Suppression-Flag und cancelt ein laufendes
Connect-Token synchron, *bevor* es `_groupLocks` überhaupt anfasst – und in
genau dieser Reihenfolge. `SwitchLeaderAsync`/`CatchUpSyncAsync` halten
diesen Lock für die gesamte Dauer ihres Connects (inklusive aller
Retry-Versuche); würde Disconnect zuerst auf den Lock warten, würde es sich
lautlos hinter einem feststeckenden Connect einreihen, bis der von selbst
aufgibt. Das Flag *vor* dem Cancel zu setzen (statt erst im gated Body)
schließt außerdem eine Race mit `InitializeGroupAsync`s eigener "hat der
Nutzer gerade getrennt?"-Prüfung: Die läuft erst, nachdem ein laufendes
`SwitchLeaderAsync` die Cancellation bemerkt und zurückgekehrt ist – das kann
frühestens nach dieser Zeile passieren, das Flag ist also garantiert
sichtbar, unabhängig davon, wie schnell diese Methode den Lock tatsächlich
bekommt.

### Sibling-Catch-up-Sweep: wann er läuft und wann er abbricht

Läuft nur, wenn ein Leader-Connect die *Gruppe* tatsächlich online bringt
(`previousLeaderId is null`) – also beim Start (`InitializeGroupAsync`) oder
nach einem manuellen Reconnect/unerwarteten Drop. Eine offline Gruppe hat
null Netzwerkaktivität (siehe `InitializeGroupAsync`); der Moment des
tatsächlichen Connects ist also genau der Moment, in dem "jeden
konfigurierten Slot aktuell machen" von Grund auf neu passieren muss –
einfacher und vorhersagbarer, als zu versuchen, nur das nachzuholen, was ein
vorheriger, möglicherweise unterbrochener Durchlauf übersprungen hat.

War die Gruppe schon online (`previousLeaderId is not null` – ein reiner
Account-Wechsel über das "Chat as"-Dropdown), ist jeder andere konfigurierte
Slot die ganze Zeit über schon durch das passive Broadcast-Coverage des
ausgehenden Leaders aktuell gehalten worden (siehe
`OnLeaderMessageReceived`/`OnHintsUpdated`) – ein erneuter Sync bei jedem
einzelnen Account-Wechsel wäre nur eine redundante Runde Logins ohne neuen
Nutzen.

Der Sweep bricht sofort ab, sobald die Gruppenverbindung mittendrin
unterbrochen wurde – entweder durch ein manuelles Disconnect
(`_autoReconnectSuppressed`, synchron von `DisconnectGroupAsync` gesetzt,
noch bevor es überhaupt versucht, denselben Lock zu bekommen) oder weil der
Leader selbst unerwartet abbricht (`OnSocketClosed` entfernt ihn synchron
aus `_leaderSlotByGroup`, ebenfalls vom eigenen Event-Thread des Sockets,
ohne auf diesen Lock zu warten). So oder so bleibt keine Gruppenverbindung
mehr übrig, an die sich die restlichen Sibling-Slots anhängen könnten –
weitermachen würde nur sinnlos neue Verbindungen nacheinander öffnen. Der
nächste erfolgreiche Leader-Connect wiederholt den ganzen Sweep von vorne
für jeden konfigurierten Slot, statt zu versuchen, nur das Übersprungene
nachzuholen.

### CatchUpSyncAsync vs. CatchUpSyncCoreAsync (Locking)

`CatchUpSyncAsync` (öffentlich) holt sich denselben Pro-Gruppe-Lock wie
`SwitchLeaderAsync`/`DisconnectGroupAsync`, damit ein von außen ausgelöster
Catch-up (z. B. "Add slot" bei bereits verbundener Gruppe) niemals eine
zweite Verbindung öffnet, während ein Sweep aus `SwitchLeaderAsync` bereits
die Ein-Verbindung-gleichzeitig-Invariante dieser Gruppe nutzt – es reiht
sich stattdessen einfach dahinter ein. Der Sweep selbst ruft direkt
`CatchUpSyncCoreAsync` auf (er hält den Lock ja schon), da `SemaphoreSlim`
nicht reentrant ist.

`CatchUpSyncCoreAsync` bricht außerdem sofort ab, wenn der Slot inzwischen
aus der Gruppen-Konfiguration entfernt wurde, egal ob er noch in der
Sweep-eigenen Liste wartete oder in einem separat eingereihten
`CatchUpSyncAsync`-Aufruf – ein entfernter Slot wird nie verbunden, Sweep
hin oder her.

### Leader-Wechsel: bewusste kurze Überlappung

`SwitchLeaderAsync` verbindet den neuen Leader zuerst und trennt den alten
erst danach – eine bewusste kurze Überlappung (beide Sessions kurz
gleichzeitig live), damit der Wechsel keine sichtbare Lücke hat, auf Kosten
einer Chat-Nachricht, die genau in diesem Fenster ankommt und dadurch
möglicherweise nicht im Log auftaucht.

### SlotSyncBatchStarting: warum erst 1, dann die Sibling-Anzahl

Beim Leader-Connect wird zunächst nur der eine Leader-Versuch angekündigt
(`RaiseSlotSyncBatchStarting(1)`) – ob er gelingt und der anschließende
Sibling-Sweep überhaupt läuft, steht zu diesem Zeitpunkt noch nicht fest,
der Fortschrittsbalken soll also nicht mehr Arbeit versprechen, als sicher
versucht wird. Erst nachdem der Leader tatsächlich verbunden hat, wird die
tatsächliche Sibling-Anzahl nachgemeldet. Ein fehlgeschlagener
Leader-Versuch zählt trotzdem als "verarbeitet" (`RaiseSlotInitialSyncCompleted`),
damit die Anzeige nicht für immer zu kurz hängen bleibt; bricht der Sweep
später vorzeitig ab, werden die übersprungenen Slots ebenfalls noch als
"verarbeitet" nachgemeldet.

### Passwort-Prompt und Retry-Flow

`NeedsPasswordPrompt` prüft `SlotProfile.Password` und
`ServerConnectionGroup.Password` bewusst getrennt (nicht den einen
`effectivePassword`-Fallback-Wert), damit ein Slot mit eigenem, aktuell
leerem Override trotzdem als "braucht eins" zählt, selbst wenn das
gemeinsame Gruppenpasswort für einen anderen, override-losen Sibling-Slot
zufällig schon gesetzt ist.

Der proaktive Passwort-Prompt läuft *vor* dem Öffnen des Sockets, nicht
danach – es wird noch nichts über den Server gebraucht (RequiresPassword +
die aktuell leeren Passwortfelder reichen), und eine beliebig lange
menschliche Pause zwischen offenem Socket und tatsächlichem Login wäre sonst
ein unnötiges Zeitfenster für einen Idle-Timeout.

Bei einer `InvalidPassword`-`LoginFailure` (laut Archipelago.MultiClient.Net
6.7.1-Doku: "indicates the wrong, or no password when it was required, was
sent") wird `RequiresPassword` gelernt, auch wenn dieser Versuch fehlschlug
– unabhängig davon, ob überhaupt jemand auf `PasswordRequested` hört. Ist
jemand registriert, wird stattdessen ein Retry mit frisch angefordertem
Passwort angeboten, statt sofort aufzugeben; dieser Retry verbraucht keinen
der `MaxTransientConnectRetries`-Versuche. Jeder andere Fehlergrund (falscher
Slot-Name, Versions-Mismatch, ...) ist eine echte, unabhängige Ablehnung und
fällt unverändert in den normalen Fehlerpfad.

Nach erfolgreichem Login wird `RequiresPassword` aus dem tatsächlich
benutzten Passwort selbst gelernt (self-healing in beide Richtungen): ein
leeres `effectivePassword`, das erfolgreich war, beweist, dass keins nötig
war; ein nicht-leeres, das erfolgreich war, beweist, dass eins nötig war
(sonst wäre eine `InvalidPassword`-Failure zurückgekommen).

### Transiente Verbindungsfehler und Retry

`ConnectSlotSessionAsync` wiederholt einen Connect/Login, der mit
`TaskCanceledException`, `OperationCanceledException` oder `TimeoutException`
fehlschlägt, bis zu `MaxTransientConnectRetries` (2) Mal mit einer komplett
neuen Session, statt es als echten Fehler zu melden. Deckt einen real
beobachteten Fehlerfall ab ("Connection failed for X: A task was canceled"
beim Leader-Wechsel kurz nachdem ein anderer Slot auf demselben Server
gerade neu verbunden hatte) – die genaue Ursache wurde nie vollständig
geklärt, aber ein gecancelter/getimeouteter Handshake lässt sich gefahrlos
wiederholen, statt für einen üblicherweise einmaligen Ausrutscher einen
beängstigenden Fehler zu zeigen. Eine echte Login-Ablehnung (falsches
Passwort etc.) wirft nie eine Exception – sie kommt als `LoginFailure`-
Ergebnis zurück und wird nie wiederholt.

Das übergebene `cancellationToken` (aus `DisconnectGroupAsync`, über den
Pro-Gruppe-Eintrag in `_connectCancellationSources`) lässt ein manuelles
Disconnect diesen Vorgang abkürzen. Die zugrunde liegende Library nimmt für
`ConnectAsync()`/`LoginAsync()` kein eigenes Token entgegen, ein bereits
laufender Aufruf kann also nicht mittendrin abgebrochen werden – die
Cancellation wird stattdessen an jedem natürlichen Prüfpunkt kontrolliert
(Beginn jedes Versuchs, direkt nach jedem der beiden Aufrufe, während der
Pause zwischen Retries), was reicht, um das Weiter-Retrien zu stoppen und
zügig abzuwickeln, sobald der aktuelle Versuch auf die eine oder andere Art
endet.

### Warum GetHintableItemsAsync/GetRoomPlayersAsync eine offene Session bevorzugen

Der Spielname-Lookup, der RoomInfo-Checksum und die DataPackage-Anfrage
selbst sind laut offiziellem Netzwerkprotokoll alle raumweite Informationen,
nicht an den anfragenden Slot gebunden – RoomInfo wird jedem verbindenden
Socket schon vor dem Login geschickt, und `GetDataPackage` "does not require
client authentication" überhaupt. Hat die Gruppe also schon irgendeine
offene Session (in der Praxis immer die des Leaders), wird sie dafür
wiederverwendet, ohne den angefragten Slot selbst auch nur kurz zu
verbinden. Nur wenn die Gruppe gar keine offene Session hat, fällt das auf
einen kurzen, offengehaltenen Probe-Connect als dieser Slot zurück.

### GetHintableLocationsAsync: Fast-Path und Re-Check

Ein Slot, der seit App-Start mindestens einmal verbunden war, wird sofort
aus `_missingLocationsBySlot` beantwortet, ohne Lock. Erst wenn nichts
gecacht ist, wird der Pro-Gruppe-Lock geholt und der Cache erneut geprüft –
ein gleichzeitiger Aufruf für denselben Slot (oder ein parallel zu Ende
gehender Sibling-Sweep) könnte ihn währenddessen gefüllt haben. Ist er
wirklich noch nie verbunden, wird einmal per `RunAsSlotAsync(...,
keepAlive: true)` verbunden, was den Cache als Seiteneffekt füllt; die
Session bleibt danach offen, damit das nächste Browse/Send für denselben
Slot im selben Hint-Picker-Durchlauf nicht erneut verbindet.

### DataPackage-Abruf per Rohpaket

Nichts in der API dieser Library-Version enumeriert das komplette
`item_name_to_id`-Mapping eines Spiels (anders als einzelne Id/Name-Lookups
wie `GetLocationNameFromId`), deshalb schickt `FetchItemNamesFromDataPackageAsync`
das rohe `GetDataPackagePacket` und wartet direkt am Socket auf das passende
`DataPackagePacket` (verifiziert gegen das offizielle Archipelago-Netzwerk-
protokoll und den gepinnten Archipelago.MultiClient.Net-Quellcode am exakten
Tag). `GetDataPackagePacket.Games` wird bewusst auf genau ein Spiel
eingeschränkt, um nicht das DataPackage jedes anderen Spiels im Raum
mitzuladen. Ergebnis ist eine leere Liste (kein Hänger), wenn der Server
innerhalb des Timeouts nicht antwortet oder den Socket vorher schließt.

### RunAsSlotAsync / ReleaseHeldSessionAsync (Hint-Picker)

`RunAsSlotAsync` läuft eine Aktion gegen eine Session für genau einen Slot –
nutzt eine schon offene wieder (Leader, laufender Catch-up-Dip, oder eine
gehaltene Hint-Picker-Probe aus einem früheren Aufruf), sonst wird kurz neu
verbunden. Rührt nie den Gruppen-Leader an, wenn der angefragte Slot nicht
der Leader ist. Aufrufer müssen den Pro-Gruppe-Lock schon halten – die
Methode holt ihn sich nicht selbst.

`keepAlive=true`: Falls dieser Aufruf eine brandneue Nicht-Leader-Session
erzeugt, bleibt sie danach offen (in `_sessions` registriert), statt sofort
wieder getrennt zu werden – vorher hat jedes Hint-Picker-Browse/Send für
einen Nicht-Leader-Slot von vorne neu verbunden, sogar mehrmals hintereinander
für denselben Slot. `ReleaseHeldSessionAsync` trennt eine so gehaltene
Session später wieder, sobald der Picker den Slot wechselt oder schließt –
ein No-Op, wenn der Slot gerade der Leader ist (der hat seinen eigenen,
unabhängigen Lifecycle) oder gar nichts gehalten wird.

### Socket-Cleanup: der Speicherleck-Workaround

`TryCloseSocketAsync` wartet tatsächlich auf das Schließen des Sockets,
statt `DisconnectAsync()` nur anzustoßen und sofort zurückzukehren (frühere
Version) – bei einem ~40-Slot-Raum blieb sonst jede jemals erstellte
`ArchipelagoSession` während der Start-Catch-up-Sync noch vollständig im
Speicher (jede mit eigenem mehrere-zehn-MB-Item/Location-Namens-Cache),
macht insgesamt mehrere hundert MB bis mehrere GB Working Set aus.

Zwei unabhängige, gegen den Quellcode von Archipelago.MultiClient.Net
(aktuell 6.7.1) verifizierte Bugs, beide mit einem Live-GC-Root-Trace
bestätigt:

1. `PollingLoop` ruft endlos `ReceiveAsync()` auf dem zugrunde liegenden
   `ClientWebSocket` ohne eigenes Cancellation-Token auf. `CloseAsync`
   aufzurufen, während anderswo noch ein `ReceiveAsync` aussteht, ist ein
   bekanntes .NET-WebSocket-Fettnäpfchen – der ausstehende Receive bleibt
   hängen, statt entsperrt zu werden. Workaround: über Reflection direkt an
   das interne `ClientWebSocket`-Feld (`FindClientWebSocket`) und `Abort()`
   darauf aufrufen, was jeden ausstehenden `ReceiveAsync`/`SendAsync` sofort
   fehlschlagen lässt und den Socket-Zustand von "Open" wegkippt.
2. `SendLoop` ruft `sendQueue.Take()` (blockierend, nicht async) auf, um auf
   das nächste ausgehende Paket zu warten. Niemand ruft beim Disconnect
   `sendQueue.CompleteAdding()` auf – sitzt `SendLoop` also in diesem
   `Take()` fest, ohne dass etwas in der Queue steht (der Normalfall für
   eine Catch-up-Session, die nach dem Login nie etwas sendet), bleibt sie
   für immer blockiert; die `while (Socket.State == WebSocketState.Open)`-
   Schleife drumherum wird erst *nach* der Rückkehr von `Take()` neu
   geprüft, Fix 1 alleine reicht also nicht. Workaround: `CompleteSendQueue`
   reflektiert auf das interne `sendQueue`-Feld und ruft dessen
   `CompleteAdding()` auf, was ein blockiertes `Take()` auf einer leeren,
   jetzt abgeschlossenen Queue eine Exception werfen statt ewig warten
   lässt.

Beide Workarounds greifen über den öffentlichen Vertrag von
`IArchipelagoSocketHelper` hinaus auf interne Implementierungsdetails zu und
sind damit von Natur aus fragil – ein künftiges Archipelago.MultiClient.Net-
Release könnte eines der Felder umbenennen oder umstrukturieren und diesen
Code stillschweigend wirkungslos machen. Deshalb ist jeder ein Best-Effort-
Versuch, der nie über diese Methode hinaus wirft, aber sichtbar geloggt wird
(Error-Event), falls er fehlschlägt – sonst würde das Speicherleck lautlos
wieder aufgehen.

`FindClientWebSocket` läuft die Typhierarchie hoch, weil das Feld
(`internal T Socket;`) auf der offenen generischen Basisklasse
`BaseArchipelagoSocketHelper<T>` deklariert ist, nicht auf dem konkreten
`ArchipelagoSocketHelper`-Typ, von dem aus Reflection startet –
`Type.GetField` allein findet nur direkt auf dem übergebenen Typ deklarierte
Member, keine geerbten, nicht-öffentlichen.

### Erwartete Exceptions durch die eigenen Workarounds

`IsExpectedSendQueueCompletionError` erkennt genau die
`InvalidOperationException`, die `CompleteSendQueue` bewusst auslöst
(BlockingCollection-Wortlaut "marked as complete with regards to additions")
– das gewollte Ergebnis des eigenen Workarounds, kein echter Fehler, wird
also nicht als "[SlotName] ..."-Fehler angezeigt.

`IsMalformedBounceDataError` erkennt eine `JsonSerializationException`, wenn
ein `Bounced`-Paket sein `data`-Feld (laut `BouncePacket` als
`Dictionary<string, JToken>` deklariert) als JSON-Array statt als Objekt
schickt – ein Protokollverstoß eines anderen Clients/Spiels im Raum, kein
hiesiger Bug. Nur erreichbar, weil die Leader-Session immer den
DeathLink-Tag anfordert (`EnableDeathLink()`) und der Server ihr deshalb
überhaupt `Bounced`-Pakete zustellt; ein Client ohne DeathLink bekommt sowas
nie, einer mit lockerem statt strikt typisiertem Parsing stolpert nicht
darüber. Harmlos – Verbindung und alle anderen Pakete bleiben unberührt.

### DeathLink

Nur die Leader-Session bekommt `EnableDeathLink()`, aus demselben Grund wie
das MessageLog-Abo (nur sie ist live). Keine Pro-Gruppe-Opt-in-Checkbox:
Anders als ein echter Spiel-Client *reagiert* diese App nie auf einen
DeathLink (kein Pausieren, kein "du bist gestorben"), sie loggt ihn nur zur
Anzeige – es gäbe also nichts, wovor ein Opt-out einen Nutzer tatsächlich
schützen würde. `EnableDeathLink()` fügt lediglich den "DeathLink"-Tag
hinzu, damit der Server dieser Session überhaupt Bounce-Pakete dafür
zustellt (das macht es zu einem Opt-in-*Protokoll*-Feature, keiner Aktion im
Namen des Nutzers). Kein Pro-Slot-Bookkeeping nötig – anders als `_sessions`
muss hier nie etwas nachgeschlagen werden, der `DeathLinkService` hält sich
über sein eigenes Socket-Abo selbst am Leben, solange die Session offen
bleibt.

### Warum die Leader-Session pro Sibling-Slot ein eigenes TrackHints braucht

Anders als Chat-/Item-Send-Zeilen wird eine Hint-Server-Benachrichtigung
NICHT an den ganzen Raum gebroadcastet – der AP-Server pusht den "Hint:
..."-Text (und das zugrunde liegende `hints_{team}_{slot}`-DataStorage-
Update) nur an die Clients des Finders und des Empfängers (siehe
MultiServer.pys `notify_hints`/`concerns`). Ein einzelnes `TrackHints`-Abo
meldet also nur Hints, bei denen genau dieser eine Slot Finder oder
Empfänger ist. Für eine kurze Catch-up-Session reicht das (die interessiert
sich nur für sich selbst), aber für den Leader – die einzige dauerhaft
offene Session – würde das sonst jeden Hint verpassen, der einen anderen
konfigurierten Sibling-Slot betrifft, solange der nicht selbst Leader ist.
Deshalb bekommt der Leader beim Connect zusätzlich ein `TrackHints`-Abo pro
anderem konfigurierten Slot über dieselbe schon offene Verbindung (kein
weiteres Login nötig) – das macht Hint-Coverage endlich "der ganze Raum",
genau wie Chat/Item-Send es über den raumweiten MessageLog-Broadcast schon
kostenlos sind.

`TrackHintsForSiblingOnLeader` deckt den Fall ab, dass ein Slot erst
*nachdem* der Leader schon verbunden ist, zur Gruppe hinzugefügt wird ("Add
slot" bei bereits verbundener Gruppe) – der Sibling-Loop in
`ConnectSlotSessionAsync` läuft nur einmal, im Moment des Leader-Connects
selbst, ein später hinzugefügter Slot würde also sonst unsichtbar bleiben,
solange er nicht selbst Leader wird.

### BuildSlotRoster und Alias-Refresh

Ordnet jedem konfigurierten Slot seine numerische Archipelago-Slot-Id im
Raum zu, per Namensabgleich (`SlotProfile.SlotName` gegen den Raum-Roster).
Wird bei jedem Aufruf neu gebaut (ein billiger linearer Scan über eine
kleine Liste) statt gecacht, damit er immer die aktuelle `Slots`-Collection
widerspiegelt, selbst wenn ein Slot nach dem Leader-Login hinzugefügt/entfernt
wurde. Nebeneffekt: hält `SlotProfile.Alias` für jeden gematchten Slot
aktuell – läuft ständig, solange der Leader verbunden ist (bei jedem
Chat/Item/Hint-Event), also ein bequemer, kostenloser Ort, um Aliase für
jeden konfigurierten Slot zu lernen/aufzufrischen, nicht nur den des
Leaders.

### ResolvePrimarySlotId

Welchem einzelnen konfigurierten Slot eine Log-/Chat-Nachricht "zugeordnet"
wird, für den Slot-Filter im Event-Log. Eine Item-Send-Nachricht löst zum
empfangenden Slot auf; alles andere zum ersten in der Nachricht erwähnten
konfigurierten Slot, falls vorhanden. `null` (immer angezeigt, unabhängig
vom Slot-Filter) für Nachrichten, die keinen konfigurierten Slot erwähnen –
raumweites Geplänkel zwischen unbeteiligten Spielern.

### Item-Backlog vs. Live bei OnItemReceived

Eine Catch-up-Session behandelt grundsätzlich alles als Backlog – ihr ganzer
Zweck ist ein einmaliger "gib mir deinen vollständigen aktuellen Stand"-
Resync, keine fortlaufende Live-Verbindung. Für die Leader-Session gilt
"live" erst, sobald `hasAnnouncedConnection` (nach der Backlog-Grace-Period)
wahr ist; davor übernimmt die Chat-Log-Zeile "X sent Y to Z" schon die
Anzeige, hier wird nur der persistierte Index weitergezählt.

### Reconnect-Scheduling nach unerwartetem Drop

`ScheduleReconnectAsync` versucht, denselben Slot wieder als Leader zu
verbinden, der gerade unerwartet abgebrochen ist – nicht zwingend
`PreferredLeaderSlotId` (das entscheidet nur, wer beim Start verbindet).
Backoff-Schema 5s/10s/30s (`ReconnectDelaysSeconds`), der letzte Wert
wiederholt sich für weitere Versuche. Bricht ab, sobald `AutoConnect` false
wird, der Slot schon wieder verbunden ist, oder das Token gecancelt wurde
(manueller Switch/Disconnect via `CancelPendingReconnect`).
