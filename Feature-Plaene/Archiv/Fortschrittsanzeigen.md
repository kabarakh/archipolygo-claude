# Umsetzungsplan: Fortschrittsanzeigen (pro Slot und Multiworld)

## Status: ✅ Umgesetzt (2026-09-09, Commit `9be4e7a`)

Beide Stufen sind umgesetzt und getestet. Abweichungen gegenüber dem Plan
unten - der Rest des Dokuments (Kernbefunde, API-Recherche, Datenmodell) ist
weiterhin akkurat und war die Grundlage der Umsetzung.

**Tier 2 UI komplett anders als geplant.** Der Plan sah eine Liste/einen
Balken pro Spieler vor ("eigener Abschnitt im Dashboard oder ausklappbarer
Bereich"). Nutzer-Feedback nach einer ersten Umsetzung mit genau so einer
Liste: bei großen Rooms wären das "hunderte Bars" gewesen. Tatsächlich
umgesetzt: **eine einzige Progressbar pro Server**, in vier Segmente
gesplittet (eigene erfüllt / eigene offen / fremde erfüllt / fremde offen -
in dieser Reihenfolge, nicht nach erfüllt/offen gruppiert), über
`CountToStarWidthConverter` (vier Star-gewichtete Grid-Spalten - Avalonias
`ProgressBar` kann selbst nur eine Füllfarbe). Zahlen/Prozente stehen nicht
mehr auf der Bar, sondern in einem Hover-Tooltip (eigene Legende + Hinweis
"Tracker hinzufügen" als Fallback ohne Tracker). Farbschema mehrfach nach
direktem Nutzer-Feedback angepasst (aktuell: Grün = eigene, Blau = fremde,
gesättigt = erfüllt, dunkles Grau-in-Farbton = offen).

**"Eigene vs. fremde" ohne Player-Matching.** Der Plan ließ offen, wie Tier 2
(Tracker-API, nummerierte Player) einem eigenen konfigurierten Slot
zugeordnet werden soll. Tatsächliche Lösung: gar nicht - "eigene" kommt
unverändert aus Tier 1 (`RoomChecksCompleted`/`RoomChecksTotal`, aus der
eigenen Live-Verbindung), "fremde" ist die Tier-2-Gesamtsumme minus diese
eigenen Zahlen (auf 0 geklemmt, falls der bis zu 60-300s alte Tracker kurz
hinter der live aktuellen eigenen Verbindung zurückliegt). Siehe
`GroupViewModel.OwnChecksDone`s Doc-Kommentar.

**Alias-Handling war nicht Teil dieses Plans, kam aber in derselben Arbeit
dazu:** `SlotProfile.Alias`/`DisplayName` ("SlotName (Alias)", identisch zu
Archipelagos eigener Chat-Konvention) an jeder Stelle, die vorher nur den
rohen Slotnamen zeigte, plus ein Fix für ComboBoxen, die bei langen
kombinierten Namen breiter wurden. Ebenso das Verschieben der
Slot-Entfernung im "Edit Server"-Dialog auf "erst bei Save anwenden" (vorher
sofort bei Klick auf "✕", was zu einem ungewollten Disconnect mitten in der
Bearbeitung führen konnte). Beide sind eigenständige Fixes, keine
Fortschrittsanzeigen-Funktionalität im engeren Sinn - der Vollständigkeit
halber hier erwähnt, weil sie im selben Commit/derselben Session entstanden.


Ergänzt `archipolygo_feature_ideas.md` ("Check progress per slot" +
Nutzeranfrage "progress-bars pro slot und multiworld"). Nutzer-Entscheidung:
**beide** Stufen umsetzen - eigene konfigurierte Slots (Tier 1, sofort
machbar mit vorhandenen Client-Daten) *und* das ganze Multiworld/alle
Spieler im Room (Tier 2, braucht eine externe Datenquelle).

## Tier 1: eigene konfigurierte Slots (Client-Daten, kein externer Dienst)

### Kernbefund

`IArchipelagoSession.Locations` (`ILocationCheckHelper`, bereits Teil des
Kategorie-B-Seams) liefert direkt, was für "X von Y Checks" nötig ist:

```csharp
ReadOnlyCollection<long> AllLocations { get; }
ReadOnlyCollection<long> AllLocationsChecked { get; }
ReadOnlyCollection<long> AllMissingLocations { get; }
event LocationCheckHelper.CheckedLocationsUpdatedHandler CheckedLocationsUpdated;
```

`AllLocationsChecked.Count` / `AllLocations.Count` ist bereits die fertige
Kennzahl - kein eigenes Tracking der Location-IDs nötig.

### Wo diese Daten pro Slot herkommen

Nur der Leader hat eine dauerhaft offene Session - alle anderen konfigurierten
Slots bekommen ihre Daten (Items, Hints) schon heute nur stoßweise, während
eines Catch-up-Dips (`ConnectionManager.CatchUpSyncCoreAsync`). Fortschritt
folgt demselben, bereits etablierten Aktualitätsmodell - **kein neues
Frische-Konzept**, sondern dieselbe "so aktuell wie der letzte Catch-up"-
Garantie wie bei Items/Hints:

- In `ConnectSlotSessionAsync`, nach erfolgreichem Login (Leader **und**
  Catch-up-Sessions gleichermaßen): `session.Locations.AllLocationsChecked.Count`/
  `AllLocations.Count` lesen, auf `slot.LocationsChecked`/`slot.LocationsTotal`
  schreiben.
- Nur für den Leader zusätzlich `CheckedLocationsUpdated` abonnieren, damit der
  Wert *live* mitläuft, während die Session offen bleibt (nicht nur beim
  Connect einmalig).

### Datenmodell

`SlotProfile` um zwei `[ObservableProperty]`-Felder erweitern:

```csharp
private int? _locationsChecked;
private int? _locationsTotal;
```

`null` = "noch nie erfolgreich synchronisiert" (frischer Slot, oder noch nie
verbunden) - UI zeigt dann keinen Balken statt einem irreführenden 0/0.
Da `SlotProfile` schon Teil von `ServerConnectionGroup.Slots` ist, werden
diese zwei Felder automatisch mit `groups.json` mitgespeichert - **keine
Migration nötig**, `System.Text.Json` ignoriert neue optionale Properties in
alten Dateien beim Deserialisieren einfach (Default `null`).

### UI

- **Pro Slot**: In der Slot-Verwaltung von `ConnectionEditorWindow.axaml`
  ("Edit server", `ShowSlotManagement`) neben jedem Slotnamen eine kompakte
  Fortschrittsanzeige (`ProgressBar` + Text "143/210"), analog zur
  bestehenden "Default leader"-Markierung in derselben Zeile.
- **Pro Room ("Multiworld" im engeren, bereits erreichbaren Sinn)**: eine
  aggregierte `ProgressBar` in der Status-Zeile jedes Tabs (`Grid.Row="0"`),
  Summe über `group.Group.Slots` mit nicht-`null` Werten. Neue berechnete
  Properties auf `GroupViewModel`: `RoomChecksCompleted`, `RoomChecksTotal`,
  `RoomProgressPercent` - müssen auf `SlotProfile.PropertyChanged` jedes
  konfigurierten Slots reagieren (Ab-/Anmeldung analog zu bestehenden
  Mustern wie `RefreshSlotOrder`s Reaktion auf `Group.Slots.CollectionChanged`).

### Tests

- **Kategorie A**: die Aggregations-Arithmetik (`RoomChecksCompleted`/`Total`/
  `Percent` über eine Liste von `SlotProfile`s mit gemischten `null`/gesetzten
  Werten) - reine Logik, kein Avalonia nötig.
- **Kategorie B**: `ConnectionManager` befüllt `SlotProfile.LocationsChecked`/
  `Total` nach erfolgreichem (Fake-)Login korrekt; `CheckedLocationsUpdated`
  aktualisiert den Wert live beim Leader. Braucht eine kleine Ergänzung an
  `FakeArchipelagoSession`: aktuell wirft `Locations` `NotImplementedException`
  - eine `FakeLocationCheckHelper` mit settable `AllLocations`/
  `AllLocationsChecked` und einem manuell auslösbaren `CheckedLocationsUpdated`
  ergänzen (gleiches "nur was gebraucht wird"-Prinzip wie die übrigen Fakes).
- **Kategorie C**: `ProgressBar`-Werte/-Breite nach echtem Layout-Pass für
  gegebene `RoomChecksCompleted`/`Total`.

## Tier 2: ganzes Multiworld (alle Spieler im Room)

### Warum das grundsätzlich anders ist

Das Archipelago-Client-Protokoll (und damit `Archipelago.MultiClient.Net`)
gibt einem eingeloggten Client **nur seinen eigenen** Location-Fortschritt -
das ist by design so (jeder Slot ist ein eigener Login), verifiziert gegen
`docs/network protocol.md` im `ArchipelagoMW/Archipelago`-Repository: das
`RoomInfo`-Paket (die erste Nachricht, die ein Client nach dem Verbinden
bekommt) enthält weder eine Room-ID noch eine Tracker-ID noch sonst einen
Verweis auf den Webhost - nur `version`, `generator_version`, `tags`,
`password`, `permissions`, `hint_cost`, `location_check_points`, `games`,
`datapackage_checksums`, `seed_name`, `time`. Der rohe Spielserver
(Host:Port, das, was Archipolygo heute schon speichert) und der Webhost
(archipelago.gg bzw. eine selbst-gehostete Instanz davon) sind zwei getrennte
Dinge - Fortschritt aller Spieler lässt sich also **nicht** aus der
bestehenden `IArchipelagoSession`-Verbindung ableiten, sondern nur über eine
zusätzliche, komplett unabhängige HTTP-Anfrage an den Webhost.

### Die offizielle Webhost-API (verifiziert gegen `docs/webhost api.md`)

Der Webhost bietet dafür ein dokumentiertes, öffentliches JSON-API - **kein
HTML-Scraping nötig**, anders als ursprünglich in diesem Plan vermutet:

- **`GET /room_status/<suuid:room_id>`** (kein Cache-Timer dokumentiert)
  liefert u. a. `"tracker"` - die Tracker-SUUID dieses Rooms - sowie
  `"players"` (Liste von `[SlotName, Game]`), `"last_port"`, `"last_activity"`,
  `"timeout"`, `"downloads"`.
- **`GET /tracker/<suuid:tracker>`** (Cache-Timer: 60s - **nicht öfter
  pollen**) liefert je Spieler `player_checks_done` (Liste erledigter
  Location-IDs), `total_checks_done` (Team-Summe), plus `aliases`,
  `player_items_received`, `hints`, `activity_timers`, `connection_timers`,
  `player_status`.
- **`GET /static_tracker/<suuid:tracker>`** (Cache-Timer: 300s) liefert
  `player_locations_total` (Gesamtzahl Locations je Spieler - das fehlende
  "von Y" zu `player_checks_done`s "X"), `player_game`, `groups`
  (Item-Link-Gruppen), `datapackage`-Checksums.
- **`GET /slot_data_tracker/<suuid:tracker>`** (Cache-Timer: 300s) - für
  dieses Feature nicht gebraucht.

„X von Y" pro Spieler ergibt sich also aus
`len(player_checks_done[player].locations)` (aus `/tracker/...`) über
`player_locations_total[player].total_locations` (aus `/static_tracker/...`)
- beide Aufrufe kombiniert nötig, aber **reine Zahlen, keine Namensauflösung
  notwendig** für eine Fortschrittsanzeige.

### Konsequenzen für das Datenmodell

Die Tracker-SUUID ist **nicht** Teil des normalen Login-Handshakes (siehe
oben) - Archipolygo kennt sie nicht automatisch und der Nutzer muss sie
einmalig manuell hinterlegen. Nutzer-Vorgabe: **ein einziges Eingabefeld**,
das alle drei Formen akzeptiert, die ein Nutzer plausibel zur Hand haben
könnte - die nackte Tracker-ID, die Room-URL (die der Room-Ersteller ohnehin
zum Einladen teilt) oder direkt die Tracker-URL (die z. B. auf der Room-Seite
selbst verlinkt ist). Das Feld muss also selbst erkennen, welche Form
eingegeben wurde:

```csharp
// Models/ oder Services/ - reine, testbare Parsing-Logik ohne Netzwerkzugriff.
public enum TrackerReferenceKind { TrackerId, RoomId }

public static bool TryParseTrackerReference(string input, out TrackerReferenceKind kind, out string value)
```

Erkennung anhand der Pfadstruktur, **domain-unabhängig** (funktioniert damit
automatisch auch für selbst gehostete Webhost-Instanzen, nicht nur
archipelago.gg):

1. Eingabe trimmen, führende/folgende `/` und einen eventuellen Query-/
   Fragment-Teil (`?...`, `#...`) abschneiden, an `/` in Segmente zerlegen.
2. Endet die Eingabe auf `.../tracker/<segment>` (case-insensitive) →
   `TrackerReferenceKind.TrackerId`, `value` = `<segment>` - direkt
   verwendbar, keine weitere Auflösung nötig.
3. Endet die Eingabe auf `.../room/<segment>` (case-insensitive) →
   `TrackerReferenceKind.RoomId`, `value` = `<segment>` - braucht noch den
   `/room_status/<room_id>`-Aufruf, um daraus die Tracker-ID zu bekommen.
4. Enthält die Eingabe **kein** `/` überhaupt (also weder URL noch Pfad,
   nur die nackte ID) → `TrackerReferenceKind.TrackerId`, `value` = die
   getrimmte Eingabe selbst.
5. Alles andere (z. B. eine URL mit unbekanntem/nicht unterstütztem Pfad
   wie `/sphere_tracker/...`) → `false`, Validierungsfehler in der UI statt
   stillem Fehlverhalten.

`ConnectionEditorViewModel` löst das beim Speichern auf: bei
`TrackerReferenceKind.RoomId` wird `/room_status/<room_id>` einmalig
aufgerufen (async, mit sichtbarem Ladezustand/Fehlermeldung im Dialog, falls
die Anfrage fehlschlägt - Speichern erst erlauben, wenn entweder eine gültige
Tracker-ID feststeht oder das Feld leer gelassen wurde), bei
`TrackerReferenceKind.TrackerId` direkt übernommen. Gespeichert werden **zwei**
Felder auf `ServerConnectionGroup`:

```csharp
private string? _trackerReferenceInput; // exakt das, was der Nutzer eingetippt hat - für die erneute Anzeige im Editor
private string? _trackerId;             // aufgelöste, tatsächlich für Polling verwendete Tracker-SUUID
```

Getrennt, weil ein einzelnes `TrackerId`-Feld beim erneuten Öffnen des
Editors nicht mehr erkennen ließe, ob der Nutzer ursprünglich eine Room-URL
oder direkt eine Tracker-ID eingegeben hatte - `TrackerReferenceInput` ist
rein fürs erneute Anzeigen/Editieren da, `TrackerId` ist das einzige Feld,
das `MultiworldTrackerService` tatsächlich für die Polling-Aufrufe liest.
Ändert der Nutzer `TrackerReferenceInput`, wird `TrackerId` beim nächsten
Speichern neu aufgelöst.

**Wichtige Einschränkung, die in der UI/Doku klar kommuniziert werden muss**:
das funktioniert nur für Rooms, die über einen Archipelago-Webhost laufen
(archipelago.gg oder eine selbst gehostete Webhost-Instanz). Ein reiner
`MultiServer.py`-Server ohne Webhost-Komponente (custom-hostete Rooms, die
Archipolygo laut `ConnectionEditorWindow.axaml`/CLAUDE.md explizit
unterstützt - beliebiges Host:Port, optionales Passwort) hat gar keine
Room-URL/Tracker-API; das Eingabefeld bleibt für solche Gruppen einfach leer,
Tier 2 ist dann No-op, kein Fehler. Ob Tracking je Room grundsätzlich aktiv
ist (z. B. ein Room-Ersteller-Opt-out), ist in der Doku nicht spezifiziert -
in der Praxis scheint jeder über den Webhost erstellte Room einen Tracker zu
bekommen; das aber am realen Verhalten nochmal gegenprüfen, sobald
implementiert wird, nicht nur der Doku vertrauen.

### Service-Design

`Services/IMultiworldTrackerService.cs`/`MultiworldTrackerService.cs`:

```csharp
Task<string?> ResolveTrackerIdAsync(string roomId); // ruft /room_status/<room_id> auf, gibt "tracker" zurück - bekommt die schon per TryParseTrackerReference extrahierte Room-ID, keine rohe URL
Task<RoomProgressSnapshot?> GetProgressAsync(string trackerId); // kombiniert /tracker/... + /static_tracker/...
```

- Eigener `HttpClient` (nicht der `Archipelago.MultiClient.Net`-Socket - das
  ist ein komplett separater REST-Aufruf), Basis-URL konfigurierbar (Default
  `https://archipelago.gg`, überschreibbar für selbst gehostete Webhost-
  Instanzen - gleiches Prinzip wie Host/Port bei der eigentlichen Verbindung).
- **Cache-Timer der Doku strikt einhalten**: `/tracker/...` höchstens alle
  60s, `/static_tracker/...` höchstens alle 300s pollen (letzteres ändert
  sich ohnehin so gut wie nie innerhalb eines Rooms - einmal pro
  Verbindung/Öffnen des Tabs holen reicht praktisch aus, nicht im
  60s-Rhythmus mitpollen).
- Fehlerfall (Tracker-URL nicht hinterlegt, Endpoint nicht erreichbar,
  JSON-Form hat sich geändert) muss **stumm scheitern** - höchstens einmalig
  als normaler Error-Event geloggt, niemals die App blockieren. Gleiche
  Grundhaltung wie `ConnectionManager`s bestehende Fehlerbehandlung überall
  sonst.

### UI

Pro Room eine zusätzliche, klar als "aus dem Webtracker, nicht aus der
eigenen Verbindung" gekennzeichnete Liste/Balken-Gruppe (nicht mit den
eigenen Slots aus Tier 1 vermischen, auch wenn beide am Ende eine
Fortschrittsanzeige sind - unterschiedliche Aktualität/Datenquelle) - z. B.
als eigener Abschnitt im (separat geplanten) Dashboard-Tab oder als
ausklappbarer Bereich im Tab selbst.

Ein **einzelnes** neues Textfeld in `ConnectionEditorViewModel`/
`ConnectionEditorWindow.axaml` (NewGroup/EditGroup-Modus, optional, leer
lassen = Tier 2 für diese Gruppe deaktiviert) - bindet an
`TrackerReferenceInput`, nicht direkt an `TrackerId`. Watermark/Platzhalter-
Text erklärt knapp, was akzeptiert wird (z. B. `"Room- oder Tracker-URL, oder Tracker-ID"`,
gleiche Watermark-Konvention wie das bestehende `HostPortInput`-Feld mit
`Watermark="archipelago.gg:38281"`). Validierung beim Verlassen des Felds/
beim Speichern über `TryParseTrackerReference` (siehe oben); bei
`TrackerReferenceKind.RoomId` einen kurzen Ladezustand zeigen, während
`ResolveTrackerIdAsync` läuft, und bei Fehlschlag (Room nicht gefunden,
kein Webhost, Netzwerkfehler) denselben `ValidationError`-Mechanismus
verwenden, den der Dialog für andere Felder schon hat
(`ConnectionEditorViewModel.ValidationError`/`TryBuildResult`).

### Tests für Tier 2

- **Kategorie A**:
  - `TryParseTrackerReference` gegen alle drei akzeptierten Formen (nackte
    Tracker-ID, Room-URL, Tracker-URL - mit und ohne `https://`, mit und
    ohne trailing slash, mit Query-/Fragment-Anhang) sowie gegen erkennbar
    ungültige Eingaben (`/sphere_tracker/...`, leerer String, wirres
    Freitext) - reine Logik, kein Netzwerk, kein Avalonia.
  - `ResolveTrackerIdAsync`/`GetProgressAsync`s JSON-Parsing gegen die oben
    zitierten Beispiel-Antworten (fest eingebettete Test-Fixtures, keine
    echte Netzwerkanfrage - gleicher Grundsatz wie `Test-Umsetzungsplan.md`s
    Ausschluss von echtem Netzwerk-Timing), plus die "X von Y"-
    Kombinationslogik aus `player_checks_done` + `player_locations_total`.
    `IMultiworldTrackerService` dafür fakeable machen (Interface + Fake,
    DI-Registrierung analog zu allen anderen Services in `App.axaml.cs`).
- **Kategorie B/C**: nicht zutreffend - kein `IArchipelagoSession`-Bezug,
  kein spezifisches View-Verhalten jenseits einer gewöhnlichen
  Werte-Anzeige/Validierungsmeldung im Dialog.

## Reihenfolge

Tier 1 zuerst umsetzen und ausliefern - eigenständig nützlich, ohne
Abhängigkeit von externen Recherchen. Tier 2 als klar getrennter,
nachgelagerter Schritt, der mit der Tracker-Formatrecherche beginnt.
