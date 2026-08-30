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
`ControlPanelWindow`, Abschnitt „Add-Slot Redesign (Prototyp)" – der
Prototyp bleibt als Referenz im Repo, wird aber nicht mehr weiterentwickelt).
Nach Durchklicken/Freigabe durch den Entwickler direkt umgesetzt in
`Models/SelectableSlotRow.cs`, `ViewModels/ConnectionEditorViewModel.cs`
(`FilteredSlotRows`/`SelectVisible`/`DeselectVisible`/`BuildSlotsToAdd`) und
`Views/ConnectionEditorWindow.axaml` – das ist jetzt der reale Dialog, den
`ConnectionEditorMode.AddSlot` anzeigt.
