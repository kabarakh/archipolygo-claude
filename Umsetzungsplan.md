# Umsetzungsplan: Archipelago Multi-Connection Client (Avalonia/C#)

Dieser Plan macht aus `Archipelago_Avalonia_Projektplan_v2.md` konkrete
Schritte für die Implementierung. Die Basis ist das vorhandene Projekt
`AvaloniaApplication1` (.NET 10, Avalonia 12.0.4, CommunityToolkit.Mvvm
8.4.1).

## Benötigte zusätzliche Pakete

- `Archipelago.MultiClient.Net` (6.7.1): der offizielle .NET-Client für
  Archipelago-Server. Er macht die WebSocket-Verbindung, die Events für
  Items, Hints und Chat und die Verwaltung der Session.
- `System.Text.Json` ist schon Teil von .NET 10. Für das Speichern von
  Profilen und Hints ist kein zusätzliches Paket nötig.

## Phase 1 – Projektgerüst, MVVM, Tabs, Profilverwaltung

1. Ergänze die NuGet-Referenz `Archipelago.MultiClient.Net` im `.csproj`.
2. Erstelle die Ordner `Models/`, `Services/`, `ViewModels/` und `Views/`.
   Der Ordner `Models/` existiert schon und ist leer.
3. Erstelle die Models:
   - `ServerProfile` (Id, Name, Host, Port, SlotName, Password,
     AutoConnect). Mache es zu einer einfachen Klasse mit dem Attribut
     `[ObservableObject]` oder zu einem POCO. Das hängt davon ab, ob der
     Editor es live bindet.
   - Das Enum `ConnectionState` (Disconnected, Connecting, Connected,
     Reconnecting, Error).
   - `ProfileSyncState` (pro `ServerProfile`, separat gespeichert). Es hat
     `LastSeenItemIndex` (int, der höchste verarbeitete Item-Index) und
     `SeenHintIds` (`HashSet<int>` mit allen schon bekannten Hint-Ids).
     Phase 2 und Phase 4 füllen diese Felder (siehe dort).
4. Implementiere `PersistenceService`:
   - Er lädt und speichert `List<ServerProfile>` als JSON in einer Datei im
     AppData-Verzeichnis (`Environment.SpecialFolder.ApplicationData`).
   - Er lädt und speichert auch `ProfileSyncState` pro Profil-Id. Das geht
     in einer eigenen JSON-Datei oder in einem eigenen Bereich der
     Profildatei.
   - So bleiben `LastSeenItemIndex` und `SeenHintIds` über Programmstarts
     erhalten. Phase 2 und Phase 4 füllen diese Felder.
5. Erstelle `MainWindowViewModel` mit `ObservableCollection<TabViewModel>
   Tabs`, `AddProfileCommand` und `RemoveProfileCommand`. Beim Start lädt
   es die Profile über `PersistenceService`.
6. Erstelle das Grundgerüst von `TabViewModel`. Es hält `ServerProfile`,
   `IsConnected` und Platzhalter-Properties für spätere Phasen.
7. Erstelle in `MainWindow.axaml` ein `TabControl` mit
   `ItemsSource="{Binding Tabs}"`. Der Tab-Header zeigt
   `ServerProfile.Name`.
8. Erstelle `ConnectionEditorViewModel` und die zugehörige View (Dialog oder
   Flyout). Damit legt der Benutzer ein Profil an oder bearbeitet es (Host,
   Port, SlotName, Password, AutoConnect).
9. Teste manuell: Lege Profile an. Prüfe, dass die Tabs erscheinen. Prüfe,
   dass die Profile nach einem Neustart noch da sind.

## Phase 2 – Einzelverbindung, Eventanzeige

1. Erstelle das Model `EventEntry` (Timestamp, Text, Type).
   - `Type` ist ein Enum (Connected, Disconnected, ItemReceived,
     HintReceived, Chat, Error).
   - Ergänze das Feld `IsNewSinceLastSession` (bool) für die Markierung aus
     Schritt 3.
2. Erstelle den Service `ConnectionManager`:
   - Die Methode `ConnectAsync(ServerProfile)` erstellt eine
     `ArchipelagoSession` mit `ArchipelagoSessionFactory.CreateSession(host,
     port)`. Danach ruft sie `session.TryConnectAndLogin(...)` auf.
   - Er abonniert die wichtigen Events der Session, zum Beispiel
     `Socket.PacketReceived`, `Items.ItemReceived` und
     `MessageLog.OnMessageReceived`. Die genauen Events hängen von der API
     der Library in 6.7.1 ab.
   - Die Methode `DisconnectAsync(ConnectionTab)`.
3. Ergänze in `MessageHistoryService` das Tracking des Item-Index:
   - Nach einem erfolgreichen Connect liefert `session.Items.AllItemsReceived`
     (oder die gleichwertige API in 6.7.1) eine vollständige Liste mit
     Index. Die Liste enthält alle Items, die dieser Slot je empfangen hat.
     Das gilt auch, wenn der Client in dieser Zeit offline war.
   - Lade `ProfileSyncState.LastSeenItemIndex` für dieses Profil aus dem
     `PersistenceService`.
   - Zeige alle Items mit Index > `LastSeenItemIndex` als `EventEntry` mit
     `IsNewSinceLastSession = true`. Das gilt auch für Items, die der Slot
     während der Abwesenheit empfangen hat, nicht nur für Live-Items.
   - Erzeuge auch neue Live-Items (Event `Items.ItemReceived`) mit
     `IsNewSinceLastSession = true`. Diese Items sind per Definition neuer
     als der gespeicherte Stand.
   - Setze `LastSeenItemIndex` am Ende der Verbindung auf den höchsten
     gesehenen Index. Speichere den Wert über `PersistenceService`.
   - Du kannst den Wert auch alle paar Sekunden speichern. So gehen bei
     einem Absturz keine Daten verloren.
   - Wandle rohe Events (`Socket.PacketReceived`, Chat, Error und andere)
     wie bisher 1:1 in `EventEntry` um. Diese Events haben kein
     Index-Tracking.
4. Erweitere `TabViewModel` um `ObservableCollection<EventEntry> Events`,
   `ConnectCommand` und `DisconnectCommand`. Aktualisiere den Status
   `IsConnected` aus dem Callback von `ConnectionManager`.
5. Erstelle die View:
   - Zeige das Event-Log als `ListBox` oder `ItemsControl` mit Timestamp und
     Text.
   - Färbe jeden Eintrag nach `Type`. Nimm dafür einen `IValueConverter`
     oder einen Style-Selector.
   - Hebe Einträge mit `IsNewSinceLastSession = true` sichtbar hervor. Zum
     Beispiel: fetter Text, Punkt-Icon oder andere Hintergrundfarbe.
6. Teste so:
   1. Verbinde den Client.
   2. Lass ihn während der Verbindung einige Items empfangen.
   3. Trenne die Verbindung.
   4. Löse auf dem Server weitere Items aus, während der Client offline ist.
   5. Verbinde den Client erneut.
   6. Prüfe: Die App markiert die Items aus der Abwesenheit als "neu". Ältere,
      schon gesehene Items markiert sie nicht.

## Phase 3 – Mehrere parallele Verbindungen, Ungelesen-Markierung

1. Erweitere `ConnectionManager`: Er verwaltet mehrere gleichzeitige
   Sessions in einem `Dictionary<Guid, ArchipelagoSession>`. Das Dictionary
   muss thread-sicher sein, zum Beispiel `ConcurrentDictionary`.
2. Ergänze das Model `ConnectionTab` um `HasUnreadEvents`, `IsConnected` und
   eine Referenz auf `ArchipelagoSession`.
3. Setze `HasUnreadEvents = true` in `TabViewModel`, wenn ein nicht aktiver
   Tab neue Events empfängt. `MainWindowViewModel` setzt den Wert beim
   Tab-Wechsel (`SelectedTabChanged`) auf `false`.
4. Ergänze in der UI ein Template für den Tab-Header mit einem Badge oder
   Punkt. Binde ihn an `HasUnreadEvents`. Zum Beispiel: ein
   `IsVisible`-Binding auf einen kleinen Kreis neben dem Titel.
5. Teste so:
   1. Verbinde zwei Profile gleichzeitig.
   2. Erzeuge Events in einem inaktiven Tab.
   3. Prüfe die Markierung.
   4. Wechsle zu dem Tab und prüfe, dass die Markierung verschwindet.

## Phase 4 – Hint-System, Filterung, Badge

1. Erstelle das Model `HintEntry` (Key, ReceivingPlayer, FindingPlayer,
   ItemName, LocationName, Found, ReceivedAt).
   - Ergänze das Feld `IsNewSinceLastSession` (bool).
   - Hinweis: Laut Library (Modell `Hint`) haben Archipelago-Hints keine
     eigene Id. `Key` ist ein synthetischer Schlüssel aus `ReceivingPlayer`,
     `FindingPlayer`, `ItemId` und `LocationId`. Er identifiziert einen Hint
     eindeutig.
2. Erstelle den `HintService`:
   - Er synchronisiert die Hints über `session.Hints.TrackHints(...)` in eine
     `ObservableCollection<HintEntry>` pro Tab. `TrackHints` liefert bei
     jeder Änderung die vollständige aktuelle Hint-Liste, wie
     `AllItemsReceived`.
   - Er aktualisiert den Status `Found` bei den passenden Events. Dafür ist
     `HintEntry` ein `ObservableObject` mit `[ObservableProperty] Found`. So
     aktualisiert sich die UI ohne Reset der Collection.
   - Er speichert die Hints über `PersistenceService`. Das Format ist JSON
     pro Profil-Id, getrennt von der Profilliste.
   - Er macht ein Tracking der Hints, ähnlich wie bei Items (siehe Phase 2):
     - Beim Connect liefert der Server die vollständige aktuelle Hint-Liste
       für den Slot.
     - Lade `ProfileSyncState.SeenHintIds` für dieses Profil.
     - Markiere jeden Hint mit `IsNewSinceLastSession = true`, wenn seine
       `Id` nicht in `SeenHintIds` steht.
     - Das gilt für Hints aus der Abwesenheit und für neue Live-Hints.
   - Der Service vergleicht mit der Menge `SeenHintIds` aller schon
     gezeigten Hint-Ids. Er vergleicht nicht mit einem einzelnen
     `LastSeenHintIndex`. Der Grund: Anders als Items haben
     Archipelago-Hints keinen sicher lückenlosen Index.
   - Am Ende der Verbindung (oder periodisch) übernimmt er alle aktuell
     bekannten Hint-Ids in `SeenHintIds`. Danach speichert er die Menge über
     `PersistenceService`.
   - Die Menge kann dabei wachsen. Bei sehr langer Nutzung kannst du sie auf
     gefundene oder erledigte Hints begrenzen. So bleibt die Datei klein.
3. Erweitere `TabViewModel` um `Hints`, `VisibleHints` (gefiltert),
   `SelectedHintFilter` (Enum: All, Unfound) und `UnfoundHintCount`.
   - `VisibleHints` ist eine abgeleitete Collection.
   - Aktualisiere sie zum Beispiel über einen Refresh der
     `ObservableCollection` oder über `DynamicData`, wenn das Projekt es
     einbindet.
   - Sonst filtere die Liste bei jeder Änderung manuell neu.
4. Erstelle die View:
   - Zeige die Hint-Liste unter oder neben dem Event-Log. Nimm dafür ein
     `Grid` mit zwei Bereichen, wie im Plan skizziert.
   - Ergänze ToggleButtons oder eine ComboBox als Filter für All und
     Unfound.
   - Hebe Hints mit `IsNewSinceLastSession = true` sichtbar hervor, wie neue
     Items. Nimm den gleichen Stil wie in Phase 2.
   - Für einen einheitlichen Stil ist ein gemeinsamer Converter oder Style
     am besten.
5. Erweitere das Template des Tab-Headers um einen Badge:
   `"{ServerProfile.Name} ({UnfoundHintCount})"`.
6. Teste so:
   1. Empfange Hints.
   2. Finde Items und prüfe, dass sich der Status `Found` aktualisiert.
   3. Schalte den Filter um.
   4. Prüfe, dass die Hints nach einem Reconnect und nach einem Neustart
      noch da sind.

## Phase 5 – Nachrichten senden, Auto-Reconnect, Einstellungen

1. Erstelle in `TabViewModel` den `SendMessageCommand`. Er ruft
   `ConnectionManager.SendMessageAsync(tab, text)` auf. Dieser ruft
   `session.Say(text)` (oder die gleichwertige API) auf.
2. Ergänze in der View ein Eingabefeld und einen Senden-Button. Beide sind
   nur aktiv, wenn `IsConnected` gilt.
3. Implementiere Auto-Reconnect:
   - `ConnectionManager` reagiert auf das Disconnect-Event von `Socket`.
   - Er prüft `ServerProfile.AutoConnect`.
   - Er verbindet erneut mit Backoff, zum Beispiel nach 5s, 10s, 30s.
   - Zeige den Status über das Enum `ConnectionState`.
4. Erstelle `SettingsViewModel` und die View für globale Einstellungen. Zum
   Beispiel: Standard-AutoConnect. Bereite auch das Historie-Limit aus den
   Erweiterungen vor.
5. Teste so:
   1. Stoppe den Server kurz und starte ihn wieder.
   2. Beobachte das Reconnect-Verhalten.
   3. Sende eine Nachricht.
   4. Prüfe die Nachricht im Serverlog oder im Chat.

## UI: Tab-Gruppierung nach Host/Port

Die App sortiert Tabs nicht alphabetisch und nicht nach der Reihenfolge, in
der der Benutzer sie erstellt hat. Alle Verbindungen mit gleicher
Kombination aus Host und Port stehen immer direkt nebeneinander. Ein
Beispiel sind mehrere Slots auf demselben lokalen Server.

So funktioniert das:

- `MainWindowViewModel` hält `Tabs` weiter als flache
  `ObservableCollection<TabViewModel>`.
- Nach jedem Hinzufügen oder Bearbeiten eines Profils sortiert es die Tabs
  um. Es gruppiert sie nach (Host, Port).
- Es nimmt dafür `ObservableCollection.Move`, nicht Clear und Add. So bleibt
  `SelectedTab` erhalten.
- Innerhalb einer Gruppe und zwischen den Gruppen bleibt die bisherige
  Reihenfolge so stabil wie möglich. Der Grund: `GroupBy` ist stabil.

## Querschnittliche Aufgaben (während aller Phasen)

- Dependency Injection: Registriere die Services (ConnectionManager,
  PersistenceService, MessageHistoryService, HintService) mit
  `Microsoft.Extensions.DependencyInjection`. Injiziere sie in die
  ViewModels. Instanziiere sie nicht statisch.
- Fehlerbehandlung: Zeige Verbindungsfehler als `EventEntry` vom Typ
  `Error` im passenden Tab. Schreibe sie nicht nur in ein Log.
- UI-Thread-Sicherheit: Leite alle Updates aus Archipelago-Callbacks über
  `Dispatcher.UIThread.Post` oder `InvokeAsync` an die ObservableCollections
  weiter.
- Unit-Tests: Schreibe Tests für `PersistenceService`, die Filterlogik von
  `HintService` und `MessageHistoryService`. Ergänze dafür ein
  xUnit-Projekt.

## Reihenfolge / Abhängigkeiten

- Phase 1 → 2 → 3 sind streng sequenziell. Der Grund: Die
  Mehrfachverbindung baut auf der Einzelverbindung auf.
- Du kannst Phase 4 (Hints) zum Teil parallel zu Phase 3 beginnen. Dafür
  muss `ConnectionManager` aus Phase 2 fertig sein.
- Phase 5 ist unabhängig. Du kannst sie nach Phase 3 machen.

Status: Die Phasen 1–5 sind implementiert. Für Phase 6 waren zuerst andere
Punkte geplant: Log-Export, Desktop-Benachrichtigungen und Tray-Icon. Der
Entwickler hat diese Punkte gestrichen, und niemand hat sie umgesetzt.

**Der Name „Phase 6" bezeichnet stattdessen einen späteren, viel größeren
Umbau des Verbindungsmodells.** Der Abschnitt unten dokumentiert diesen
Umbau. Jeder Code-Kommentar mit „Phase 6" meint diesen Umbau. Er meint nicht
die ursprünglich hier geplanten und verworfenen Punkte.

## Phase 6 – Server/Slot/Leader-Umbau (tatsächlich umgesetzt)

Dieser Umbau ändert das Verbindungsmodell grundlegend. Vorher war ein
`ServerProfile` (Host, Port, SlotName, Password) eine eigene, unabhängige
Verbindung. Bei mehreren Slots im selben Raum lief also eine Session pro
Slot gleichzeitig. Das neue Modell ersetzt das so:

1. **Eigene Modelle `ServerConnectionGroup` und `SlotProfile`.**
   - `ServerConnectionGroup` ist ein Tab und ein Archipelago-Raum. Es hat
     Host, Port, gemeinsames Passwort, `AutoConnect` und
     `PreferredLeaderSlotId`.
   - `SlotProfile` hat nur noch `SlotName` und ein optionales
     Passwort-Override.
   - Ein altes `ServerProfile` entspricht jetzt einer Gruppe mit genau einem
     Slot.
2. **Migration.** `PersistenceService` migriert eine vorhandene alte
   `profiles.json` einmal automatisch in das neue Format `groups.json`
   (`MigrateLegacyProfilesIfNeeded`).
   - Profile mit gleichem Host und Port kommen in eine Gruppe.
   - Jedes Profil behält seine alte `Id` als neue `SlotProfile.Id`.
   - So bleibt der gespeicherte Sync-Status gültig, also der letzte gesehene
     Item-Index und die gesehenen Hint-Ids.
3. **Nur noch eine Live-Verbindung pro Gruppe** (`ConnectionManager`,
   `_leaderSlotByGroup`). Der aktuell verbundene Slot heißt „Leader".
   - Ein Leader-Wechsel (`SwitchLeaderAsync`) verbindet zuerst den neuen
     Leader. Erst danach trennt er den alten Leader.
   - Diese kurze Überlappung ist Absicht. So gibt es keine sichtbare Lücke.
4. **Passive Aktualisierung der Slots, die nicht Leader sind.** Die Session
   des Leaders sieht den ganzen Chat und das ganze Log des Raums.
   - Darum spiegelt die App Item-Sends und Hints für einen anderen eigenen
     Slot direkt in dessen Historie (`OnLeaderMessageReceived`,
     `OnHintsUpdated`).
   - Dafür ist keine eigene Verbindung nötig.
5. **Catch-up** (`CatchUpSyncAsync`). Manchmal hat ein Slot Daten verpasst,
   weil die App geschlossen war oder weil der Benutzer ihn neu hinzugefügt
   hat. Dann verbindet sich der Slot kurz, holt die verpassten Daten und
   trennt sich sofort wieder.
   - `InitializeGroupAsync` macht das beim Start für jeden Slot, der nicht
     Leader ist.
   - Zwischen den Gruppen wartet es eine Verzögerung ab
     (`StartupGroupSpacing`).
   - So bekommen bei einem Neustart mit mehreren Servern nicht alle Server
     gleichzeitig viele Login-Versuche.
6. **Die App speichert die Leader-Wahl automatisch.** Jeder erfolgreiche
   Leader-Connect setzt `AutoConnect=true` und `PreferredLeaderSlotId` auf
   der Gruppe.
   - Das gilt für das Account-Dropdown, für das Anlegen des ersten Slots und
     für einen Auto-Reconnect.
   - Der Connect meldet das über `IConnectionManager.GroupPersistNeeded`.
   - Auto-Connect ist also nicht mehr nur eine Checkbox beim Anlegen eines
     Servers. Es folgt automatisch jeder echten Verbindung.
   - Ein manuelles Disconnect setzt `AutoConnect` genauso automatisch
     zurück.

## Nach Phase 6 – weitere Erweiterungen des Verbindungsmodells

Diese Erweiterungen bauen direkt auf dem Umbau aus Phase 6 auf. Sie kamen
in keiner festen Reihenfolge dazu:

- **Mehrere Slots mit weniger Klicks hinzufügen.** Im Dialog „Add slot"
  stellt der Benutzer mehrere Slots aus dem Roster in eine Warteliste
  (`StagedSlots`). Jeder Slot kann ein eigenes Passwort-Override haben. Beim
  Bestätigen fügt die App alle auf einmal hinzu. Vorher war ein
  Dialog-Durchlauf pro Slot nötig.
- **Sequenzielles Catch-up beim Hinzufügen mehrerer Slots.** Wenn der
  Benutzer mehrere Slots auf einmal hinzufügt, laufen ihre Catch-ups
  nacheinander. Gleichzeitige Catch-ups überlasten den Server mit zu vielen
  parallelen Verbindungsversuchen. Heute hängt `AddSlotsToGroup` die Slots
  an die globale Catch-up-Queue an (siehe nächster Punkt).
- **Eine Connect-Sperre und eine Catch-up-Queue für alle Gruppen.** Vorher
  galt "eine Verbindung gleichzeitig" nur pro Gruppe. Beim Start lief eine
  Gruppe nach der anderen komplett durch, und der Fortschrittsbalken zeigte
  immer nur eine Gruppe. Ein manueller Connect einer anderen Gruppe lief
  parallel zu einem laufenden Catch-up. Heute gilt:
  - Beim Start verbindet die App zuerst die Leader aller Gruppen.
  - Alle Catch-ups aller Gruppen laufen nacheinander in einer Queue.
  - Der Fortschrittsbalken zeigt eine Gesamtzahl für alle Gruppen.
  - Ein Leader-Connect hat Vorrang vor der Queue.

  Details stehen im Abschnitt "Globale Connect-Sperre und Catch-up-Queue".
- **Slot-Verwaltung im Dialog „Edit server".** Der Dialog zeigt alle eigenen
  Slots eines Servers.
  - Der Benutzer wählt dort den Standard-Leader (`Make default`).
  - Er entfernt dort einzelne Slots (`RemoveSlotFromGroup`), ohne den ganzen
    Server zu löschen.
  - Wenn nötig, trennt das Entfernen zuerst die laufende Verbindung.
- **Sortierung der Slot-Listen.** Das Account-Dropdown, die drei
  Slot-Filter und die Slot-Verwaltung zeigen Slots alphabetisch. Der
  aktuelle Leader oder der Standard-Leader steht jeweils an erster Stelle
  (`GroupViewModel.RefreshSlotOrder`,
  `ConnectionEditorViewModel.SortSlotsForDisplay`).
- **Filter für den Roster.** Die App filtert zwei Arten von Einträgen aus
  jeder Spielerliste (`ConnectionManager.FilterToRealPlayers`):
  - Slot 0. Das Protokoll reserviert ihn für den Server selbst. Er heißt
    oft wörtlich „Server".
  - Einträge für Item-Link-Gruppen.

  So kann der Benutzer sie nicht aus Versehen als echten Slot
  konfigurieren.
- **Mehrfachauswahl und Kopieren mehrerer Zeilen.** Event-Log und
  Hint-Liste haben unabhängig voneinander eine Mehrfachauswahl. Strg/Cmd+C
  kopiert alle markierten Zeilen auf einmal.
- **Die App wiederholt transiente Verbindungsfehler.** Manchmal schlägt ein
  Connect oder Login mit `TaskCanceledException`,
  `OperationCanceledException` oder `TimeoutException` fehl.
  - Dann versucht `ConnectSlotSessionAsync` es bis zu zweimal erneut, mit
    einer frischen Session. Erst danach meldet es den Fehler.
  - Das deckt unter anderem einen beobachteten Fehlerfall ab: einen
    Leader-Wechsel kurz nach dem Reconnect eines anderen Slots.
  - Einen echten Login-Fehler (falsches Passwort) wiederholt die App nie.
- **Fortschrittsanzeige beim Start.** Das neue Event
  `IConnectionManager.SlotInitialSyncCompleted` meldet nach jedem Slot, den
  die App beim Start synchronisiert, den Fortschritt.
  `MainWindowViewModel` zählt mit. Währenddessen zeigt `MainWindow.axaml`
  eine Leiste „Catching up slots: N/M" mit Fortschrittsbalken.
- **Release-Workflow mit GitHub Actions**
  (`.github/workflows/release.yml`). Bei jedem veröffentlichten GitHub
  Release erzeugt der Workflow automatisch eigenständige Single-File-Builds
  für Windows, Linux und macOS (x64 und arm64). Er hängt sie als Downloads
  an das Release.

Zu dieser Zeit gab es kein eigenes Testprojekt. Niemand hatte die
Unit-Tests aus „Querschnittliche Aufgaben" oben umgesetzt. Heute gibt es
`AvaloniaApplication1.Tests` (siehe `Test-Umsetzungsplan.md`).

## „Add slot"-Dialog: Mehrfachauswahl mit Suche statt ComboBox + „Add to list"

Dieses Design entstand in einer Chat-Session. Es ist ein Vergleich mit der
Slot-Auswahl in `J:\dev\Archipelago`s `multi_slot_tracker_webui`
(`SlotPicker.vue`) und der zugehörigen apworld (`worlds\multi_slot_tracker`).

- Dieser Tracker beobachtet Slots nur passiv. Darum braucht er kein
  Passwort.
- Darum kann die App sein Muster nicht 1:1 übernehmen.
- Der Entwickler hat das Design zuerst als Prototyp im `TestHarness`
  durchgeklickt (siehe unten).
- Nach seiner Freigabe kam es direkt in
  `ConnectionEditorViewModel`/`ConnectionEditorWindow.axaml`. Es ist jetzt
  der reale Dialog „Add slot" der App.

**Ziel:** Ein Raum kann einen großen Roster haben. Der Benutzer fügt dann
oft mehrere Slots auf einmal hinzu. Er muss dafür nicht mehr jeden Eintrag
einzeln in der ComboBox auswählen. Er muss auch nicht mehr jeden Eintrag
einzeln mit „Add to list" in die Warteliste stellen.

**UI (ersetzt ComboBox + „Add to list" in `ConnectionEditorMode.AddSlot`):**

1. Ein Suchfeld für freien Text filtert die Liste der verfügbaren Slots
   live. Es filtert nach Slot-Name und Anzeigename (wie das
   `filtered`-Computed in `SlotPicker.vue`).
2. Darunter steht eine Checkbox-Liste der verfügbaren (gefilterten) Slots
   statt der ComboBox. So wählt der Benutzer mehrere Slots ohne
   Zwischenschritt.
3. Die zwei Buttons „Select visible" und „Deselect visible" wirken nur auf
   die *aktuell gefilterte* Teilmenge (1:1 aus `SlotPicker.vue`). So deckt
   ein Bedienweg zwei Fälle ab:
   - Der Benutzer sucht einige Slots gezielt und hakt sie an.
   - Der Benutzer wählt alle passenden Slots auf einmal.
4. Der Button zum Bestätigen bleibt deaktiviert, solange der Benutzer
   nichts ausgewählt hat. Dazu zeigt der Dialog einen Hinweistext (wie
   `canApply` und der Hinweis „Select at least one slot to continue" in
   `SlotPicker.vue`).

**Passwort – der Punkt, der beim Vue-Picker fehlt, weil der nur beobachtet
statt sich einzuloggen:**

- **Dieser Dialog hat kein globales Passwort-Feld für alle Slots.** Zu
  diesem Zeitpunkt kennt die App das gemeinsame Server-Passwort der Gruppe
  schon.
  - Die App fragt es beim ersten Verbinden eines Leaders für diese Gruppe
    ab. Das passiert *vor* jeder Auswahl von Slots
    (`ConnectionEditorMode.NewGroup`/erster Slot).
  - Ein zusätzliches Passwort-Feld in diesem Dialog ist darum nur ein
    zweites, redundantes Eingabefeld für denselben Wert.
- Stattdessen bleibt das, was es schon vor diesem Redesign gab: Jede Zeile
  hat ein optionales Override-Feld.
  - Das Feld ist standardmäßig eingeklappt. Ein kleines 🔒-Icon klappt es
    auf.
  - Es ist für die seltenen Ausnahme-Slots. Diese Slots brauchen auf einem
    selbst gehosteten Multiworld ein eigenes, anderes Passwort.
- Vorrangregel beim Hinzufügen:
  1. Wenn der Benutzer ein Override für den Slot eingegeben hat, gilt das
     Override.
  2. Sonst nimmt der Slot beim Verbinden das gemeinsame Passwort der
     Gruppe. Das ist schon das vorhandene Verhalten. Es ist keine neue
     Logik.

**Status:** Der Entwickler hat das Design zuerst als anklickbaren Prototyp
in `TestHarness/AddSlotPickerPrototype/` geprüft.

- Der Prototyp hatte synthetische Spielernamen statt eines echten Rosters
  (siehe `ControlPanelWindow`, früherer Abschnitt „Add-Slot Redesign
  (Prototyp)").
- Nach dem Durchklicken und der Freigabe durch den Entwickler kam das Design
  direkt in diese Dateien:
  - `Models/SelectableSlotRow.cs`
  - `ViewModels/ConnectionEditorViewModel.cs`
    (`FilteredSlotRows`/`SelectVisible`/`DeselectVisible`/`BuildSlotsToAdd`)
  - `Views/ConnectionEditorWindow.axaml`
- Das ist jetzt der reale Dialog, den `ConnectionEditorMode.AddSlot` zeigt.

## ConnectionManager: Locking, Nebenläufigkeit und Workarounds im Detail

Dieser Abschnitt sammelt die längeren Begründungen ("Warum"). Früher standen
sie als Kommentare direkt in `Services/ConnectionManager.cs`. Die Datei war
auf über 2100 Zeilen gewachsen. Fast die Hälfte davon war Kommentar. Jetzt
steht die ausführliche Begründung für jede nicht offensichtliche Stelle hier.
Im Code steht nur noch ein Verweis mit einem Satz auf den passenden
Unterabschnitt. Reine Dokumentation zu "was tut diese Methode" bleibt direkt
im Code.

### Item-Backlog-Grace-Period und Retry-Delay (Testbarkeit)

Der Konstruktor kann `_itemBacklogGracePeriod` und
`_transientConnectRetryDelay` überschreiben. Ohne Parameter gelten die Werte,
die die App schon immer verwendet: 2s Backlog-Grace und 2s Retry-Delay.

Grund: Die Fakes aus `AvaloniaApplication1.TestSupport`
(`FakeArchipelagoSession`/`FakeSessionFactory`) machen jeden Connect und Login
über eine `TaskCompletionSource` deterministisch steuerbar. Aber diese zwei
Delays sind reine `Task.Delay`-Aufrufe ohne Bezug zu einer Session. Ohne die
Möglichkeit zum Überschreiben wartet jeder Test mit einem Catch-up oder einem
transienten Retry wirklich mehrere Sekunden. Die Test-DI übergibt Werte nahe
null. `App.axaml.cs` (die echte App) übergibt die Parameter nie und bekommt
so die echten Defaults.

### Warum der Item-Backlog eine Grace-Period braucht

Der Server schickt den initialen Rückstand von `ReceivedItems` als separate
WebSocket-Nachricht. Diese Nachricht kommt *nach* dem `Connected`-Paket.
Darum kann `LoginAsync()` zurückkehren, bevor alle Backlog-Items ihr
`ItemReceived`-Event gefeuert haben. Dann läuft auch die await-Fortsetzung
schon weiter.

Wenn die App `BacklogState.HasAnnouncedConnection` sofort auf `true` setzt,
kann die Fortsetzung mitten in einem Burst von `ItemReceived`-Aufrufen
laufen. Dann stuft die App den Rest des Bursts fälschlich als "live" statt
als "Backlog" ein. Die Grace-Period von zwei Sekunden stellt sicher, dass der
synchrone Burst vollständig vorbei ist, bevor die App umschaltet. In der
Praxis dauert der Burst nur Millisekunden.

### `_sessions`: Lifecycle und maximale gleichzeitige Einträge

Der Schlüssel ist `SlotProfile.Id`. Ein Eintrag existiert, solange der Slot
irgendeine aktive Verbindung hat. Diese Verbindungen sind möglich:

- als Leader der Gruppe,
- als kurzlebige Session für einen Catch-up oder als Ziel eines
  Leader-Wechsels,
- als Probe-Session des Hint-Pickers, die die App bewusst offen hält. Das
  gilt, solange der Slot im Picker ausgewählt bleibt (siehe den Parameter
  `keepAlive` von `RunAsSlotAsync` und `ReleaseHeldSessionAsync`).

Für dieselbe Gruppe können höchstens zwei oder drei Einträge gleichzeitig
existieren:

- alter und neuer Leader während eines Wechsels,
- Leader und eine Catch-up-Session,
- Leader und eine gehaltene Probe-Session des Hint-Pickers für einen anderen
  Slot.

### `_missingLocationsBySlot`: bewusst nur im Speicher

Die App aktualisiert dieses Feld bei jedem erfolgreichen Connect (Leader und
Catch-up). Für den Leader hält `CheckedLocationsUpdated` das Feld live
aktuell. Die App speichert das Feld absichtlich nicht in `groups.json`. Die
reinen Zähler `LocationsChecked`/`LocationsTotal` auf `SlotProfile` speichert
sie dagegen. Grund: Eine vollständige Liste der Locations macht die Datei bei
einem großen Spiel erheblich größer. Außerdem kann die App die Liste beim
nächsten Connect billig neu aufbauen.

Mit diesem Feld kann `GetHintableLocationsAsync` sofort antworten. Das gilt
für jeden Slot, der seit dem Start der App mindestens einmal verbunden war.
Dafür braucht die App keinen eigenen Probe-Connect.

### `_roomInfoBySlot`

Das Feld enthält pro Slot den `RoomInfoPacket` aus dem letzten Aufruf von
`ConnectAsync()`. Das Feld existiert nur, damit `GetHintableItemsAsync` die
`DataPackageChecksums` ohne eigenen Netzwerk-Roundtrip lesen kann. Jeder
Connect und jeder Reconnect überschreibt den Eintrag. Ein veralteter Eintrag
zwischen zwei Connects ist harmlos. Grund: `GetHintableItemsAsync` liest nur
im Moment eines frischen Aufrufs von `RunAsSlotAsync`. Es liest dann nur für
genau diesen Slot, der gerade verbunden ist.

### `_connectCancellationSources`

Das Feld bildet die GroupId auf die `CancellationTokenSource` ab. Diese
gehört zu dem einen Connect-Versuch, der gerade in dieser Gruppe läuft. Der
Versuch ist entweder ein Leader-Connect aus `SwitchLeaderAsync` oder ein
kurzer Catch-up aus der Catch-up-Queue. Beide halten den Lock der Gruppe.
Darum laufen nie beide zugleich.

`DisconnectGroupAsync` cancelt dieses Token, *bevor* es auf `_groupLocks`
wartet. So unterbricht ein manuelles Disconnect einen Connect, der
feststeckt oder wiederholt. Ohne diese Reihenfolge reiht sich das Disconnect
still hinter dem Connect ein. Es wartet dann, bis die Retries von selbst
aufgeben.

### `_autoReconnectSuppressed`

Die App setzt dieses Flag unmittelbar vor einem bewussten
`DisconnectGroupAsync`. Erst ein späteres explizites `SwitchLeaderAsync`
(Account-Dropdown, Start) löscht das Flag wieder. `OnSocketClosed`
verbraucht oder entfernt das Flag bewusst *nicht*. Grund: Manche
Socket-Implementierungen feuern `SocketClosed` bei einem einzigen bewussten
Close mehrmals. Ein Flag, das sich selbst zurücksetzt, unterdrückt nur das
erste dieser Events.

### Disconnect: Reihenfolge Cancel vor Lock

`DisconnectGroupAsync` setzt zuerst das Suppression-Flag. Danach cancelt die
Methode ein laufendes Connect-Token. Beides passiert synchron, *bevor* die
Methode `_groupLocks` überhaupt anfasst. Diese Reihenfolge ist Absicht.

Grund für "Cancel vor Lock": `SwitchLeaderAsync` und ein Catch-up aus der Queue halten
diesen Lock während ihres ganzen Connects, mit allen Retry-Versuchen. Wenn
Disconnect zuerst auf den Lock wartet, reiht es sich lautlos hinter einem
feststeckenden Connect ein. Es wartet dann, bis dieser Connect von selbst
aufgibt.

Grund für "Flag vor Cancel": Die Methode setzt das Flag vor dem Cancel und
nicht erst im Body hinter dem Lock. Das schließt eine Race mit der eigenen
Prüfung von `InitializeGroupAsync` ("hat der Benutzer gerade getrennt?").
Diese Prüfung läuft erst, nachdem ein laufendes `SwitchLeaderAsync` die
Cancellation bemerkt hat und zurückgekehrt ist. Das passiert frühestens nach
der Zeile mit dem Cancel. Darum ist das Flag zu diesem Zeitpunkt garantiert
sichtbar. Das gilt unabhängig davon, wie schnell die Methode den Lock
wirklich bekommt.

### Globale Connect-Sperre und Catch-up-Queue

**Regel: In der ganzen App läuft immer nur ein Verbindungsaufbau mit Login
zur gleichen Zeit.** Das gilt über alle Gruppen hinweg, nicht nur pro
Gruppe. Viele gleichzeitige Handshakes führen zu abgelehnten oder
blockierten Logins.

Zwei Klassen setzen die Regel um. `ConnectionManager` erzeugt beide einmal:

- `ConnectGate` ist die Sperre für die ganze App. `OpenSocketAndLoginAsync`
  hält sie nur für `ConnectAsync()` und `LoginAsync()`.
- `CatchUpQueue` ist eine Liste aller wartenden Catch-ups aller Gruppen. Ein
  Worker arbeitet sie in Reihenfolge ab, einen Slot nach dem anderen.

Die Sperre kennt zwei Arten von Wartenden:

- Interaktiv: Leader-Connect, Hint-Picker, Roster-Probe.
- Hintergrund: Catch-up aus der Queue.

Wenn die Sperre frei wird, bekommt immer zuerst ein interaktiver Wartender
die Sperre. So pausiert ein Leader-Connect die Queue: Der laufende Catch-up
macht seinen Login fertig. Dann verbindet die App den Leader. Seine
Sibling-Slots kommen an das Ende der Queue. Danach läuft die Queue weiter.

Der Passwort-Prompt läuft *vor* dem Holen der Sperre. Ein offener
Passwort-Dialog für eine Gruppe hält darum die anderen Gruppen nicht auf.
Aus demselben Grund startet `MainWindowViewModel.StartSpacedAsync` die
Leader-Connects beim Start im Abstand von `StartupGroupSpacing`. Es wartet
nicht auf das Ende des vorherigen Connects.

Ein Handshake kann hängen. Dann gibt die Sperre sich nach höchstens 30
Sekunden selbst frei (`DefaultConnectGateMaxHold`). Sonst blockiert ein
einziger hängender Server alle anderen Gruppen für immer.

Der Worker holt pro Slot den Lock der Gruppe (`_groupLocks`) und danach die
Sperre. Jeder andere Aufrufer holt die beiden Sperren in derselben
Reihenfolge. Darum gibt es keinen Deadlock. Der Worker hält den Lock der
Gruppe nur für einen Slot. Zwischen zwei Slots kann darum andere Arbeit an
derselben Gruppe laufen.

Der Worker läuft auf dem Kontext des Aufrufers, der ihn startet. In der
echten App ist das der UI-Thread. In den Tests macht das den Ablauf mit
`Dispatcher.UIThread.RunJobs()` deterministisch.

### Sibling-Catch-up: wann er läuft und wann er abbricht

Die App hängt die Sibling-Slots nur dann an die Queue an, wenn ein
Leader-Connect die *Gruppe* wirklich online bringt (`groupJustCameOnline`,
das heißt `previousLeaderId is null`). Das passiert beim Start
(`InitializeGroupAsync`) oder nach einem manuellen Reconnect oder einem
unerwarteten Drop. Eine Gruppe im Offline-Zustand hat keine
Netzwerkaktivität (siehe `InitializeGroupAsync`). Darum ist der Moment des
Connects genau der Moment, in dem die App jeden eigenen Slot von Grund auf
neu aktuell machen muss. `SwitchLeaderAsync` wartet nicht auf diese
Catch-ups.

Wenn die Gruppe schon online war (`previousLeaderId is not null`), ist es nur
ein Account-Wechsel über das Dropdown "Chat as". Dann hat der ausgehende
Leader jeden anderen eigenen Slot die ganze Zeit passiv über seine
Broadcast-Abdeckung aktuell gehalten (siehe
`OnLeaderMessageReceived`/`OnHintsUpdated`). Ein neuer Catch-up bei jedem
Account-Wechsel ist darum nur eine redundante Runde Logins ohne neuen Nutzen.

Die App entfernt die wartenden Einträge einer Gruppe sofort aus der Queue,
wenn die Verbindung der Gruppe abbricht (`CatchUpQueue.RemoveGroup`). Dafür
gibt es zwei Ursachen:

- Ein manuelles Disconnect (`DisconnectGroupAsync`).
- Der Leader selbst bricht unerwartet ab (`OnSocketClosed`).

In beiden Fällen bleibt keine Verbindung der Gruppe übrig, die die
Sibling-Slots nutzen können. Der nächste erfolgreiche Leader-Connect hängt
wieder jeden Sibling-Slot an die Queue an. Ein Slot, der schon wartet, kommt
nicht zweimal in die Queue. Zur Sicherheit prüft der Worker vor jedem Slot
noch einmal, ob die Gruppe online ist (`IsOnline`). Ein Leader-Wechsel über
"Chat as" bricht die Catch-ups der Gruppe nicht ab.

### CatchUpSyncAsync vs. CatchUpSyncCoreAsync (Locking)

`CatchUpSyncAsync` (öffentlich) verbindet nicht selbst. Es hängt den Slot an
die Catch-up-Queue an und wartet, bis der Slot fertig ist. Ein Catch-up von
außen (zum Beispiel "Add slot" bei einer schon verbundenen Gruppe) reiht
sich darum hinter alle wartenden Catch-ups ein. `CatchUpSyncCoreAsync` macht
den eigentlichen Catch-up. Der Aufrufer (der Worker der Queue) hält dabei
den Lock der Gruppe.

`CatchUpSyncCoreAsync` bricht außerdem sofort ab, wenn der Slot inzwischen
nicht mehr in der Konfiguration der Gruppe ist. Die App verbindet einen
entfernten Slot nie.

### Leader-Wechsel: bewusste kurze Überlappung

`SwitchLeaderAsync` verbindet zuerst den neuen Leader und trennt den alten
erst danach. Diese kurze Überlappung ist Absicht: Beide Sessions sind kurz
gleichzeitig live. So hat der Wechsel keine sichtbare Lücke. Der Preis: Eine
Chat-Nachricht, die genau in diesem Zeitfenster ankommt, fehlt dann
vielleicht im Event-Log.

### SlotSyncBatchStarting: warum erst 1, dann die Sibling-Anzahl

Beim Leader-Connect kündigt die App zuerst nur den einen Leader-Versuch an
(`RaiseSlotSyncBatchStarting(1)`). Zu diesem Zeitpunkt steht noch nicht fest,
ob er gelingt und ob danach Sibling-Slots in die Queue kommen. Der
Fortschrittsbalken darf nicht mehr Arbeit versprechen, als die App sicher
versucht. Erst wenn der Leader wirklich verbunden ist, meldet die App die
tatsächliche Anzahl der Sibling-Slots nach. Die App meldet diese Anzahl
*vor* dem Abschluss des Leaders. Sonst zeigt der Balken kurz einen fertigen
Durchlauf und beginnt dann wieder bei null.

Alle Gruppen zählen in dieselbe Gesamtzahl. `OnSlotSyncBatchStarting` in
`MainWindowViewModel` erhöht die Gesamtzahl, solange noch Arbeit offen ist.
Erst wenn alles fertig ist, beginnt die nächste Meldung wieder bei null.

Ein fehlgeschlagener Leader-Versuch zählt trotzdem als "verarbeitet"
(`RaiseSlotInitialSyncCompleted`). Sonst bleibt die Anzeige für immer zu kurz
stehen. Wenn die App wartende Einträge aus der Queue entfernt, meldet sie
diese Slots ebenfalls als "verarbeitet".

### Passwort-Prompt und Retry-Flow

`NeedsPasswordPrompt` prüft `SlotProfile.Password` und
`ServerConnectionGroup.Password` bewusst getrennt. Es prüft nicht den einen
Fallback-Wert `effectivePassword`. So zählt ein Slot mit eigenem, aktuell
leerem Override trotzdem als "braucht eins". Das gilt auch, wenn das
gemeinsame Passwort der Gruppe für einen anderen Sibling-Slot ohne Override
zufällig schon gesetzt ist.

Der proaktive Passwort-Prompt läuft *vor* dem Öffnen des Sockets, nicht
danach. Für den Prompt braucht die App noch keine Information vom Server.
`RequiresPassword` und die aktuell leeren Passwortfelder reichen. Ein Prompt
nach dem Öffnen erzeugt außerdem eine beliebig lange Pause des Benutzers
zwischen offenem Socket und Login. Diese Pause ist ein unnötiges Zeitfenster
für einen Idle-Timeout.

Bei einer `LoginFailure` mit `InvalidPassword` lernt die App
`RequiresPassword`, auch wenn dieser Versuch fehlschlägt. Laut Doku von
Archipelago.MultiClient.Net 6.7.1 bedeutet dieser Grund: "indicates the
wrong, or no password when it was required, was sent". Die App lernt den Wert
unabhängig davon, ob überhaupt jemand auf `PasswordRequested` hört.

Wenn jemand auf `PasswordRequested` registriert ist, gibt die App nicht
sofort auf. Sie bietet stattdessen einen Retry mit einem neu angeforderten
Passwort an. Dieser Retry verbraucht keinen der Versuche aus
`MaxTransientConnectRetries`. Jeder andere Fehlergrund (falscher Slot-Name,
Versions-Mismatch, ...) ist eine echte, unabhängige Ablehnung. Dieser Fehler
geht unverändert in den normalen Fehlerpfad.

Nach einem erfolgreichen Login lernt die App `RequiresPassword` aus dem
Passwort, das sie wirklich benutzt hat. Das korrigiert sich in beide
Richtungen selbst (self-healing):

- Ein leeres `effectivePassword` war erfolgreich. Das beweist, dass kein
  Passwort nötig war.
- Ein nicht leeres `effectivePassword` war erfolgreich. Das beweist, dass ein
  Passwort nötig war. Sonst kommt eine `InvalidPassword`-Failure zurück.

### Transiente Verbindungsfehler und Retry

`ConnectSlotSessionAsync` wiederholt einen Connect oder Login, der mit
`TaskCanceledException`, `OperationCanceledException` oder
`TimeoutException` fehlschlägt. Die Methode wiederholt ihn bis zu
`MaxTransientConnectRetries` (2) Mal, jedes Mal mit einer komplett neuen
Session. Sie meldet ihn nicht als echten Fehler.

Das deckt einen real beobachteten Fehlerfall ab: "Connection failed for X: A
task was canceled". Der Fehler trat beim Leader-Wechsel auf, kurz nachdem
ein anderer Slot auf demselben Server neu verbunden hatte. Niemand hat die
genaue Ursache vollständig geklärt. Aber die App kann einen gecancelten oder
abgelaufenen Handshake gefahrlos wiederholen. So zeigt sie für einen meist
einmaligen Ausrutscher keinen beängstigenden Fehler.

Eine echte Login-Ablehnung (falsches Passwort und Ähnliches) wirft nie eine
Exception. Sie kommt als `LoginFailure`-Ergebnis zurück. Die App wiederholt
sie nie.

Das übergebene `cancellationToken` kommt aus `DisconnectGroupAsync`, über den
Eintrag der Gruppe in `_connectCancellationSources`. Mit diesem Token kann
ein manuelles Disconnect diesen Vorgang abkürzen. Die Library darunter nimmt
für `ConnectAsync()`/`LoginAsync()` kein eigenes Token an. Darum kann die App
einen laufenden Aufruf nicht mittendrin abbrechen. Stattdessen prüft sie die
Cancellation an jedem natürlichen Prüfpunkt:

- am Beginn jedes Versuchs,
- direkt nach jedem der beiden Aufrufe,
- während der Pause zwischen Retries.

Das reicht, um weitere Retries zu stoppen. Sobald der aktuelle Versuch auf
irgendeine Art endet, wickelt die App den Vorgang zügig ab.

### Warum GetHintableItemsAsync/GetRoomPlayersAsync eine offene Session bevorzugen

Laut offiziellem Netzwerkprotokoll sind diese Informationen raumweit:

- der Lookup des Spielnamens,
- die RoomInfo-Checksum,
- die DataPackage-Anfrage selbst.

Sie gehören nicht zum anfragenden Slot. Der Server schickt RoomInfo jedem
verbindenden Socket schon vor dem Login. Für `GetDataPackage` sagt das
Protokoll: "does not require client authentication".

Wenn die Gruppe schon eine offene Session hat, verwendet die App diese
Session dafür wieder. In der Praxis ist das immer die Session des Leaders.
Die App verbindet dann den angefragten Slot nicht, auch nicht kurz. Nur wenn
die Gruppe keine offene Session hat, macht die App einen kurzen Probe-Connect
als dieser Slot. Die App hält diese Probe-Session danach offen.

### GetHintableLocationsAsync: Fast-Path und Re-Check

Wenn ein Slot seit dem Start der App mindestens einmal verbunden war,
antwortet die Methode sofort aus `_missingLocationsBySlot`. Sie holt dafür
keinen Lock. Nur wenn der Cache leer ist, holt die Methode den Lock pro
Gruppe und prüft den Cache erneut. Der Grund: Ein gleichzeitiger Aufruf für
denselben Slot kann den Cache inzwischen füllen. Dasselbe gilt für einen
Catch-up aus der Queue, der gerade endet.

Wenn der Slot wirklich noch nie verbunden war, verbindet die Methode ihn
einmal mit `RunAsSlotAsync(..., keepAlive: true)`. Das füllt den Cache als
Nebeneffekt. Die Session bleibt danach offen. So verbindet das nächste
Browse oder Send für denselben Slot im selben Durchlauf des Hint-Pickers
nicht erneut.

### DataPackage-Abruf per Rohpaket

Die API dieser Library-Version hat keine Methode, die das ganze
`item_name_to_id`-Mapping eines Spiels aufzählt. Sie hat nur Lookups für
einzelne Ids oder Namen, zum Beispiel `GetLocationNameFromId`. Darum schickt
`FetchGameDataFromDataPackageAsync` das rohe `GetDataPackagePacket`. Danach
wartet die Methode direkt am Socket auf das passende `DataPackagePacket`.
Grundlage dafür sind das offizielle Archipelago-Netzwerkprotokoll und der
Quellcode von Archipelago.MultiClient.Net am exakten gepinnten Tag.

`FetchGameDataFromDataPackageAsync` beschränkt `GetDataPackagePacket.Games`
mit Absicht auf genau ein Spiel. So lädt die App nicht das DataPackage jedes
anderen Spiels im Raum mit. Wenn der Server innerhalb des Timeouts nicht
antwortet oder den Socket vorher schließt, ist das Ergebnis `null`. Die
Methode hängt dann nicht.

Seit es die Admin-Ansicht gibt, liefert derselbe Abruf Item-Namen **und**
Location-Namen (`GameDataNames`, Location-Name → Id).
`GetHintableItemsAsync` nutzt davon nur die Items. `GetGameDataAsync`
braucht beides, und zwar für das Spiel *jedes* Slots im Raum.
`GetGameDataAsync` antwortet nur über die Session des Leaders. Der Grund:
Der Benutzer kann die Admin-Ansicht sowieso nur mit Verbindung benutzen.

Beide Methoden teilen sich den Cache auf der Platte. Ein Cache-Eintrag von
vor dieser Änderung hat `LocationIds == null`. Für `GetGameDataAsync` zählt
er als Cache-Miss. Die Methode holt die Daten dann neu und überschreibt den
Eintrag mit beiden Hälften.

### Admin-Ansicht: ServerReplyReceived und LeaderConnected

Die Admin-Ansicht (`AdminPanelViewModel`, siehe `Admin-Funktionen.md` im
Feature-Plan-Archiv) schickt `!admin …`-Zeilen über das normale
`SendMessageAsync`. Dafür braucht sie zwei Signale aus `ConnectionManager`.

**`ServerReplyReceived`** meldet Zeilen des Servers, die eine Antwort sein
können. Die Grundlage ist eine zweite Subscription auf `MessageLog`, nur auf
der Session des Leaders. `SessionEventTranslator.ClassifyServerReply` ordnet
die Zeilen ein. Grundlage für die Liste unten sind MultiServer.py und der
Quellcode von MultiClient.Net 6.7.1:

- `!`-Kommandos antworten als `CommandResultLogMessage`. Dazu gehören jedes
  Ergebnis von `!admin login` und "You must first login".
- `/`-Kommandos, die fehlschlagen, antworten als
  `AdminCommandResultLogMessage`. `/option` antwortet auch bei Erfolg so.
- Die meisten *erfolgreichen* Admin-Aktionen (`/send`, `/release`,
  `/collect`) antworten nicht direkt. Sie erscheinen nur als Broadcast an
  den ganzen Raum. Das ist entweder ein PrintJSON ohne Typ oder eine
  `Release`/`CollectLogMessage`. Das PrintJSON ohne Typ liefert die Library
  als exakten Basistyp `LogMessage`.

Der Server ordnet keine Antwort ihrem Kommando zu. Darum sendet
`AdminPanelViewModel` die Kommandos strikt nacheinander. Es sammelt alles,
was in einem kurzen Zeitfenster danach ankommt. Mehrzeilige Antworten kommen
dabei als einzelne Nachrichten an, denn die Library teilt sie pro Zeile auf.
Die Zeilen erscheinen außerdem unverändert im normalen Event-Log.

**`LeaderConnected`**: `ConnectionManager` feuert dieses Event nach jedem
erfolgreichen `SwitchLeaderAsync`. Das gilt für den Wechsel über "Chat as",
für Reconnect und für die erste Verbindung. Das Event steht in der Queue
hinter dem Post von `SetLeaderStateWithoutTriggeringSwitch`. Der Grund für
das Event: Das `!admin login` gehört auf dem Server zum Socket. Darum ist
jede neue Verbindung des Leaders abgemeldet. Das ViewModel meldet sich dann
still neu an, mit dem Passwort, das es nur im Speicher hält.

Es gibt einen zweiten Weg zur Abmeldung: Ein anderer Client meldet sich als
Admin an. Diesen Fall meldet der Server nicht. Das ViewModel erkennt ihn
erst an der Antwort "You must first login" auf das nächste Kommando. Dann
meldet es sich neu an und sendet denselben Befehl erneut.

### RunAsSlotAsync / ReleaseHeldSessionAsync (Hint-Picker)

**Der Aufrufer von `RunAsSlotAsync` muss den Lock pro Gruppe schon halten.**
Die Methode holt den Lock nicht selbst.

`RunAsSlotAsync` führt eine Aktion mit einer Session für genau einen Slot
aus. Wenn für den Slot schon eine Session offen ist, nutzt die Methode diese
Session wieder. Das kann eine dieser Sessions sein:

- die Session des Leaders,
- ein laufender Catch-up-Dip,
- eine gehaltene Probe des Hint-Pickers aus einem früheren Aufruf.

Sonst verbindet die Methode kurz neu. Wenn der angefragte Slot nicht der
Leader ist, ändert die Methode nie etwas am Leader der Gruppe.

`keepAlive=true`: Wenn dieser Aufruf eine ganz neue Session für einen Slot
erzeugt, der nicht Leader ist, bleibt diese Session danach offen. Die
Methode registriert sie in `_sessions` und trennt sie nicht sofort. Früher
hat jedes Browse oder Send des Hint-Pickers für einen solchen Slot ganz neu
verbunden. Das passierte sogar mehrmals hintereinander für denselben Slot.

`ReleaseHeldSessionAsync` trennt eine so gehaltene Session später wieder.
Das passiert, sobald der Picker den Slot wechselt oder schließt. Wenn der
Slot gerade der Leader ist, tut die Methode nichts. Der Leader hat seinen
eigenen, unabhängigen Lebenszyklus. Wenn die App für den Slot keine Session
hält, tut die Methode ebenfalls nichts.

### Socket-Cleanup: der Speicherleck-Workaround

`TryCloseSocketAsync` wartet wirklich, bis der Socket geschlossen ist. Eine
frühere Version hat `DisconnectAsync()` nur angestoßen und ist sofort
zurückgekehrt. Bei einem Raum mit etwa 40 Slots blieb dann jede
`ArchipelagoSession` vollständig im Speicher, die die App während des
Catch-up beim Start je erzeugt hatte. Jede dieser Sessions hatte einen
eigenen Cache für Item- und Location-Namen von mehreren zehn MB. Zusammen
waren das mehrere hundert MB bis mehrere GB Working Set.

Der Quellcode von Archipelago.MultiClient.Net (aktuell 6.7.1) belegt zwei
unabhängige Bugs. Ein Live-GC-Root-Trace bestätigt beide:

1. `PollingLoop` ruft endlos `ReceiveAsync()` auf dem zugrunde liegenden
   `ClientWebSocket` auf. Die Schleife hat kein eigenes
   Cancellation-Token. Ein bekannter Fehler bei .NET-WebSockets: Wenn ein
   `ReceiveAsync` noch aussteht und anderer Code `CloseAsync` aufruft,
   bleibt der ausstehende Receive hängen. Er kehrt nicht zurück.
   Workaround: `FindClientWebSocket` holt per Reflection direkt das interne
   Feld vom Typ `ClientWebSocket`. Der Workaround ruft darauf `Abort()` auf.
   Danach schlägt jedes ausstehende `ReceiveAsync` oder `SendAsync` sofort
   fehl, und der Socket verlässt den Zustand "Open".
2. `SendLoop` ruft `sendQueue.Take()` auf und wartet so auf das nächste
   ausgehende Paket. `Take()` blockiert und ist nicht async. Beim Disconnect
   ruft niemand `sendQueue.CompleteAdding()` auf. Wenn die Queue leer ist,
   bleibt `SendLoop` darum für immer in `Take()` blockiert. Das ist der
   Normalfall für eine Catch-up-Session, denn sie sendet nach dem Login nie
   etwas. Die umgebende Schleife
   `while (Socket.State == WebSocketState.Open)` prüft ihre Bedingung erst
   *nach* der Rückkehr von `Take()`. Darum reicht Fix 1 allein nicht.
   Workaround: `CompleteSendQueue` holt per Reflection das interne Feld
   `sendQueue` und ruft dessen `CompleteAdding()` auf. Danach wirft ein
   blockiertes `Take()` auf der leeren, jetzt abgeschlossenen Queue eine
   Exception. Es wartet dann nicht mehr ewig.

Beide Workarounds greifen auf interne Details der Implementierung zu. Diese
Details liegen außerhalb des öffentlichen Vertrags von
`IArchipelagoSocketHelper`. Darum sind beide Workarounds von Natur aus
fragil. Ein künftiges Release von Archipelago.MultiClient.Net kann eines der
Felder umbenennen oder umbauen. Dann wirkt dieser Code still nicht mehr.

Darum ist jeder Workaround ein Best-Effort-Versuch. Er wirft nie eine
Exception über diese Methode hinaus. Wenn er fehlschlägt, erzeugt die App
ein sichtbares Error-Event. Sonst kommt das Speicherleck lautlos zurück.

`FindClientWebSocket` durchsucht die Typhierarchie nach oben. Der Grund:
Das Feld (`internal T Socket;`) steht in der offenen generischen
Basisklasse `BaseArchipelagoSocketHelper<T>`. Es steht nicht im konkreten
Typ `ArchipelagoSocketHelper`, bei dem die Reflection startet.
`Type.GetField` allein findet nur Member, die der übergebene Typ selbst
deklariert. Geerbte, nicht öffentliche Member findet es nicht.

### Erwartete Exceptions durch die eigenen Workarounds

`IsExpectedSendQueueCompletionError` erkennt genau die
`InvalidOperationException`, die `CompleteSendQueue` mit Absicht auslöst.
Ihr Text kommt von BlockingCollection: "marked as complete with regards to
additions". Diese Exception ist das gewollte Ergebnis des eigenen
Workarounds und kein echter Fehler. Darum zeigt die App sie nicht als
Fehler "[SlotName] ..." an.

`IsMalformedBounceDataError` erkennt eine `JsonSerializationException` für
diesen Fall: Ein `Bounced`-Paket schickt sein Feld `data` als JSON-Array
statt als Objekt. `BouncePacket` deklariert das Feld als
`Dictionary<string, JToken>`. Das ist ein Verstoß gegen das Protokoll durch
einen anderen Client oder ein anderes Spiel im Raum. Es ist kein Bug in
dieser App.

Der Fehler tritt nur auf, weil die Session des Leaders immer den
DeathLink-Tag anfordert (`EnableDeathLink()`). Nur darum stellt der Server
ihr überhaupt `Bounced`-Pakete zu. Ein Client ohne DeathLink bekommt solche
Pakete nie. Ein Client mit lockerem statt streng typisiertem Parsing
stolpert nicht darüber. Der Fehler ist harmlos: Die Verbindung und alle
anderen Pakete bleiben unberührt.

### DeathLink

Nur die Session des Leaders bekommt `EnableDeathLink()`. Der Grund ist
derselbe wie beim Abo auf `MessageLog`: Nur diese Session ist live. Es gibt
keine Checkbox für ein Opt-in pro Gruppe. Der Grund: Anders als ein echter
Spiel-Client *reagiert* diese App nie auf einen DeathLink. Sie pausiert
nicht und meldet nicht "du bist gestorben". Sie zeigt den DeathLink nur an.
Ein Opt-out schützt den Benutzer also vor nichts.

`EnableDeathLink()` ergänzt nur den Tag "DeathLink". Damit stellt der Server
dieser Session überhaupt Bounce-Pakete für DeathLink zu. Darum ist es ein
Opt-in-Feature des *Protokolls* und keine Aktion im Namen des Benutzers.
Die App braucht dafür kein Bookkeeping pro Slot. Anders als bei `_sessions`
muss die App hier nie etwas nachschlagen. Der `DeathLinkService` hält sich
über sein eigenes Socket-Abo selbst am Leben, solange die Session offen
bleibt.

### Warum die Leader-Session pro Sibling-Slot ein eigenes TrackHints braucht

Chat-Zeilen und Zeilen zu Item-Sends gehen als Broadcast an den ganzen
Raum. Eine Hint-Benachrichtigung des Servers geht NICHT an den ganzen Raum.
Der AP-Server pusht den Text "Hint: ..." nur an die Clients des Finders und
des Empfängers. Dasselbe gilt für das zugrunde liegende
DataStorage-Update `hints_{team}_{slot}` (siehe `notify_hints`/`concerns`
in MultiServer.py). Ein einzelnes Abo mit `TrackHints` meldet also nur
Hints, bei denen genau dieser eine Slot Finder oder Empfänger ist. Für eine
kurze Catch-up-Session reicht das, denn sie braucht nur ihre eigenen Hints.

Der Leader hat aber die einzige dauerhaft offene Session. Mit nur einem Abo
verpasst er jeden Hint, der einen anderen eigenen Slot der Gruppe betrifft
(Sibling-Slot), solange dieser Slot nicht selbst Leader ist. Darum bekommt
der Leader beim Connect zusätzlich ein Abo mit `TrackHints` für jeden
anderen eigenen Slot. Diese Abos laufen über dieselbe offene Verbindung und
brauchen keinen weiteren Login. So deckt die App Hints für "den ganzen
Raum" ab. Chat und Item-Sends deckt sie über den Broadcast von `MessageLog`
an den ganzen Raum schon ohne Zusatzkosten ab.

`TrackHintsForSiblingOnLeader` deckt diesen Fall ab: Der Benutzer ergänzt
einen Slot in der Gruppe, *nachdem* der Leader schon verbunden ist ("Add
slot" bei verbundener Gruppe). Die Schleife über die Sibling-Slots in
`TrackSiblingHintsOnLeader` (aufgerufen aus `ActivateConnectedSession`)
läuft nur einmal, im Moment des Leader-Connects. Ohne
`TrackHintsForSiblingOnLeader` bleiben Hints für einen später ergänzten
Slot unsichtbar, solange er nicht selbst Leader wird.

### BuildSlotRoster und Alias-Refresh

`BuildSlotRoster` ordnet jedem eigenen Slot seine numerische
Archipelago-Slot-Id im Raum zu. Dazu vergleicht die Methode
`SlotProfile.SlotName` mit den Namen im Roster. Die Methode baut die
Zuordnung bei jedem Aufruf neu und cacht sie nicht. Das kostet wenig, denn
es ist ein linearer Scan über eine kleine Liste. So entspricht die Zuordnung
immer der aktuellen `Slots`-Collection. Das gilt auch, wenn der Benutzer
nach dem Login des Leaders einen Slot ergänzt oder entfernt hat.

Nebeneffekt: Die Methode hält `SlotProfile.Alias` für jeden gefundenen Slot
aktuell. Sie läuft ständig, solange der Leader verbunden ist (bei jedem
Chat-, Item- und Hint-Event). Darum ist sie ein bequemer Ort ohne
Zusatzkosten, um Aliase zu lernen und aufzufrischen. Das gilt für jeden
eigenen Slot, nicht nur für den Leader.

### ResolvePrimarySlotId

`ResolvePrimarySlotId` bestimmt, zu welchem einzelnen eigenen Slot eine
Nachricht aus dem `MessageLog` gehört. Der Slot-Filter im Event-Log nutzt
diese Zuordnung. Eine Nachricht zu einem Item-Send gehört zum empfangenden
Slot. Jede andere Nachricht gehört zum ersten eigenen Slot, den die
Nachricht nennt, wenn es einen gibt.

Wenn eine Nachricht keinen eigenen Slot nennt, ist das Ergebnis `null`. Das
Event-Log zeigt solche Nachrichten immer an, unabhängig vom Slot-Filter.
Solche Nachrichten sind Geplänkel im Raum zwischen fremden Slots.

### Item-Backlog vs. Live bei OnItemReceived

Eine Catch-up-Session behandelt immer alles als Backlog. Ihr einziger Zweck
ist ein einmaliger Resync nach dem Muster "gib mir deinen vollständigen
aktuellen Stand". Sie ist keine fortlaufende Live-Verbindung.

Für die Session des Leaders gilt ein Item erst als "live", wenn
`BacklogState.HasAnnouncedConnection` wahr ist. Das ist nach der
Backlog-Grace-Period der Fall. Vorher zeigt schon die Chat-Zeile "X sent Y
to Z" das Item an. `OnItemReceived` zählt dann nur den gespeicherten Index
weiter.

### Reconnect-Scheduling nach unerwartetem Drop

`ScheduleReconnectAsync` versucht, genau den Slot wieder als Leader zu
verbinden, dessen Verbindung gerade unerwartet abgebrochen ist. Das ist
nicht unbedingt `PreferredLeaderSlotId`. Dieser Wert entscheidet nur,
welcher Slot beim Start verbindet. Das Backoff-Schema ist 5s/10s/30s
(`ReconnectDelaysSeconds`). Für weitere Versuche wiederholt sich der letzte
Wert.

Die Methode bricht ab, sobald einer dieser Fälle eintritt:

- `AutoConnect` ist false.
- Der Slot ist schon wieder verbunden.
- `CancelPendingReconnect` hat das Token gecancelt (manueller Switch oder
  Disconnect).
