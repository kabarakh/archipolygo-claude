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

## Phase 6 – Serverweite Verbindungsbündelung (mehrere Slots, eine Verbindung)

Neue Betriebsart, zusätzlich zum bisherigen Modell: pro Host+Port besteht dauerhaft höchstens eine Verbindung, auch wenn mehrere Slot-Namen für diesen Server konfiguriert sind. Der Tab wird dadurch zum Server statt zum Slot; welcher Slot gerade "man selbst" ist (also mit wem man chattet), wird per Dropdown gewählt und löst als einzige Aktion einen Disconnect/Reconnect aus. Items und Hints aller konfigurierten Slots einer Gruppe bleiben trotzdem aktuell, weil Archipelago Item-Funde und Hint-Erzeugung ohnehin an alle verbundenen Clients im Raum broadcastet (dieselben `LogMessage`-Events, aus denen `EventSegmentBuilder`/`GetOtherConnectedSlotIds` heute schon Slot-Ids herausliest) – es muss also nicht für jeden Slot einzeln eine eigene Verbindung offengehalten werden.

1. Modelle:
   - Neues Model `ServerConnectionGroup` (Id, Name?, Host, Port, Password) – das Server-Passwort gilt raumweit, nicht pro Slot, und wandert deshalb von `ServerProfile` auf die Gruppe.
   - `ServerProfile` wird zu `SlotProfile` (Id, GroupId, SlotName, PreferredLeader-Flag) verschlankt; SlotName bleibt das einzige, was einen Slot innerhalb einer Gruppe unterscheidet. `AutoConnect` wird zu einem Flag "ist bevorzugter Start-Leader dieser Gruppe" (pro Gruppe kann nur ein Slot gleichzeitig Leader sein).
   - `ProfileSyncState` bleibt unverändert pro Slot-Id (`LastSeenItemIndex`, `SeenHintIds`) und funktioniert unverändert weiter, unabhängig davon, ob der jeweilige Slot aktuell Leader ist oder nur passiv über den Leader mitaktualisiert wird.
   - `PersistenceService`: Migration bestehender `ServerProfile`-Listen zu Gruppe+Slots beim ersten Start nach diesem Update; Profile mit identischem Host+Port werden zu einer Gruppe zusammengeführt. Bei abweichendem `Password` innerhalb einer künftigen Gruppe wird das erste als Gruppen-Password übernommen; der Verbindungs-Editor weist beim nächsten Öffnen auf Abweichungen hin.

2. `ConnectionManager`-Umbau (Kernstück):
   - Sessions werden ab jetzt pro `GroupId` statt pro Slot-Profil-Id gehalten; es gibt höchstens eine dauerhafte Session pro Gruppe (der "Leader"-Slot).
   - Neue Kernoperation `SyncSlotAsync(group, slotProfile, keepAlive: bool)`: baut eine Session für den angegebenen Slot auf, wartet Login + Item-/Hint-Backlog ab (bestehende Resync-Logik aus Phase 2/4), aktualisiert `ProfileSyncState` und die Anzeige-Listen dieses Slots; bei `keepAlive == false` wird die Session danach sofort wieder geschlossen. Eine Methode deckt drei Fälle ab:
     - a) Erstverbindung/"Leader werden" (`keepAlive: true`), wenn eine Gruppe noch keinen Leader hat.
     - b) Account-Wechsel beim Chatten (`keepAlive: true` für den neuen Slot; anschließend Schließen der alten Leader-Session, siehe unten).
     - c) Kurzer Catch-up-Sync für einen Nicht-Leader-Slot (`keepAlive: false`) – ausgelöst beim Hinzufügen eines neuen Slots zu einer Gruppe mit bestehendem Leader, und einmalig beim Programmstart für jeden bereits konfigurierten Nicht-Leader-Slot (schließt Lücken aus der Zeit, in der die App komplett geschlossen war). Der Leader bleibt währenddessen durchgehend verbunden; es bestehen kurzzeitig zwei Verbindungen zum selben Server, aber nie dauerhaft mehr als eine.
   - Passive Aktualisierung der Nicht-Leader-Slots: die Item-/Hint-Broadcast-Nachrichten, die der Leader ohnehin über `Socket.PacketReceived`/`MessageLog` empfängt, werden für jede erkannte Slot-Id, die zu einem konfigurierten Slot dieser Gruppe gehört, zusätzlich in dessen eigene Items-/Hints-Liste plus `ProfileSyncState` eingetragen (inklusive `IsNewSinceLastSession`, nach demselben Muster wie bei einer echten eigenen Verbindung). Die Zuordnung Slot-Name → Slot-Id für einen noch nie selbst verbundenen Slot erfolgt über `session.Players` des Leaders (listet alle Spieler des Raums, nicht nur den eigenen Slot). Das ersetzt einen periodischen Poll vollständig, weil die Daten ohnehin beim Leader ankommen.
   - `SwitchLeaderAsync(group, targetSlot)` für den Account-Wechsel: baut zuerst die neue Session für `targetSlot` auf; erst wenn Login+Backlog-Resync abgeschlossen sind, wird die alte Leader-Session geschlossen (bewusste kurze Überlappung, damit der Wechsel ohne spürbare Lücke wirkt). Eine Chat-Nachricht, die exakt während dieser Überlappung im Raum auftaucht, könnte dabei theoretisch verpasst werden – als Kompromiss akzeptiert.
   - Bekannte, bewusst akzeptierte Einschränkung: für Zeiträume, in denen kein einziger Slot einer Gruppe verbunden war, gibt es keine Broadcast-Historie zum Nachträglich-Auslesen (Chat/Log-Nachrichten werden vom Server nicht wie `AllItemsReceived`/Hints gepuffert). Ein Slot ohne jede bisherige eigene Verbindung hat entsprechend erst nach seinem ersten eigenen `SyncSlotAsync`-Aufruf einen vollständigen Stand – analog zum heutigen Verhalten beim allerersten Connect eines Profils.

3. UI-Umbau:
   - Tabs gruppieren nicht mehr Slot-Profile, sondern `ServerConnectionGroup`s; ein Tab = ein Server. Der bisherige `RegroupTabs`-Sortiermechanismus in `MainWindowViewModel` entfällt dadurch weitgehend – die Gruppierung ist jetzt strukturell, keine Sortier-Heuristik mehr nötig (siehe vorheriger Abschnitt "UI: Tab-Gruppierung nach Host/Port", der durch diese Phase ersetzt wird).
   - Neues Dropdown vor dem Chat-Eingabefeld im Server-Tab: Liste aller konfigurierten Slots dieser Gruppe, aktuelle Auswahl = aktueller Leader. Auswahl eines anderen Slots ruft `SwitchLeaderAsync` auf; Eingabefeld bleibt deaktiviert, solange der Wechsel noch läuft.
   - Items- und Hints-Listen zeigen standardmäßig alle konfigurierten Slots der Gruppe gemischt an; neues Filter-Dropdown "Slot: Alle / <SlotName> / …" ergänzt die bestehenden Filter (`HintFilter`, `HintRoleFilter`, `ItemCategoryFilter`, `EventCategoryFilter`, `EventRelevanceFilter`) um diese zusätzliche Dimension.
   - Neuer Slot wird weiterhin über den bestehenden Verbindungs-Editor (`ConnectionEditorViewModel`/`ConnectionEditorWindow`) angelegt, jetzt im Kontext "Slot zu Server X hinzufügen" statt "neue eigenständige Verbindung"; Host/Port/Password sind vorbelegt und nicht mehr editierbar, sobald ein Slot zu einer bestehenden Gruppe hinzugefügt wird.
   - Tab-Header zeigt weiterhin Name/HostPort und eine kombinierte Ungelesen-/Hint-Badge über alle Slots der Gruppe hinweg (Summe statt pro Slot).

4. Startverhalten:
   - Beim Programmstart wird für jede Gruppe zuerst der bevorzugte/zuletzt aktive Slot als Leader verbunden (Preferred-Leader-Flag), anschließend läuft für jeden übrigen konfigurierten Slot der Gruppe einmalig ein Catch-up-Sync (`keepAlive: false`), um Lücken aus der Zeit zu schließen, in der die App geschlossen war.
   - Ist noch kein Leader für eine Gruppe festgelegt (z. B. beim allerersten Start nach der Migration), wird der erstverbundene Slot automatisch zum Leader.

5. Testen:
   - Zwei Slots auf demselben Testserver konfigurieren, als Slot A verbinden; von Slot B aus (z. B. offizieller Client in einem zweiten Fenster) Items finden bzw. Hints erzeugen lassen; prüfen, dass Slot B's Ansicht in Archipolygo ohne eigene Verbindung aktuell bleibt.
   - Account-Wechsel A→B auslösen, währenddessen im Serverlog/Verbindungszähler prüfen, dass kurzzeitig zwei Verbindungen bestehen und danach wieder nur eine.
   - Neuen dritten Slot zur laufenden Gruppe hinzufügen, während A weiter Leader ist; prüfen, dass der kurze Catch-up-Connect für den neuen Slot dessen Backlog korrekt füllt und A dabei durchgehend verbunden bleibt.
   - App schließen, währenddessen für einen konfigurierten, nicht laufenden Slot B Items erzeugen lassen (z. B. über einen zweiten Client), App neu starten, prüfen dass der Catch-up-Sync beim Start diese Items nachträgt.
   - Migration bestehender `ServerProfile`-Daten aus Phase 1–5 gegen echte alte Profildateien prüfen.

Abhängigkeiten: baut auf Phase 2–5 auf (Backlog-Resync, Hint-Tracking, Chat-Versand, Auto-Reconnect) und ersetzt die bisherige 1:1-Zuordnung Tab↔Verbindung aus Phase 1 und 3 grundlegend – eher ein Umbau bestehender Teile als eine rein additive Phase. Sollte nach Phase 5 begonnen werden.

## UI: Tab-Gruppierung nach Host/Port (Stand vor Phase 6)

Tabs werden nicht alphabetisch oder nach Erstellungsreihenfolge angezeigt, sondern so, dass alle Verbindungen mit identischer Host/Port-Kombination immer direkt nebeneinander stehen (z. B. mehrere Slots auf demselben lokalen Server). `MainWindowViewModel` hält dazu `Tabs` weiter als flache `ObservableCollection<TabViewModel>`, ordnet sie aber nach jedem Hinzufügen/Bearbeiten eines Profils per `ObservableCollection.Move` (nicht Clear+Add, um `SelectedTab` zu erhalten) so um, dass nach (Host, Port) gruppiert wird; innerhalb einer Gruppe und zwischen Gruppen bleibt die bisherige Reihenfolge so stabil wie möglich erhalten (`GroupBy` ist stabil).

Dieser Abschnitt beschreibt das Tab-Verhalten vor Phase 6, mit einer Verbindung pro Slot-Profil. Sobald Phase 6 umgesetzt ist, entfällt die Notwendigkeit dieser Sortier-Heuristik, weil ein Tab dann direkt eine `ServerConnectionGroup` ist.

## Querschnittliche Aufgaben (während aller Phasen)

- Dependency Injection: `Microsoft.Extensions.DependencyInjection` für Services (ConnectionManager, PersistenceService, MessageHistoryService, HintService) registrieren und in ViewModels injizieren statt sie statisch zu instanziieren.
- Fehlerbehandlung: Verbindungsfehler als `EventEntry` vom Typ `Error` im jeweiligen Tab anzeigen, nicht nur loggen.
- UI-Thread-Sicherheit: Alle Updates aus Archipelago-Callbacks über `Dispatcher.UIThread.Post`/`InvokeAsync` an ObservableCollections weiterleiten.
- Unit-Tests für `PersistenceService`, `HintService`-Filterlogik und `MessageHistoryService` (xUnit-Projekt ergänzen).

## Reihenfolge / Abhängigkeiten

Phase 1 → 2 → 3 sind strikt sequenziell (Mehrfachverbindung baut auf Einzelverbindung auf). Phase 4 (Hints) kann teilweise parallel zu Phase 3 begonnen werden, sobald `ConnectionManager` aus Phase 2 steht. Phase 5 ist unabhängig und kann nach Phase 3 erfolgen. Phase 6 baut auf Phase 2–5 auf und sollte erst danach begonnen werden (siehe Abhängigkeiten-Hinweis am Ende von Phase 6).

Status: Phasen 1–5 sind implementiert. Eine ursprünglich vorgesehene andere Phase 6 (Log-Export, Desktop-Benachrichtigungen, Tray-Icon) wurde aus dem Plan gestrichen und wird nicht umgesetzt. Die jetzige Phase 6 (Serverweite Verbindungsbündelung, siehe oben) ist geplant, aber noch nicht umgesetzt.
