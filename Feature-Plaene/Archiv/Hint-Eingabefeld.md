# Umsetzungsplan: besseres Feld zum Hint-Absenden

## Status: ✅ Umgesetzt (2026-09-10)

In der echten App umgesetzt und getestet (User verifiziert zusätzlich noch
laufend beim Spielen in echten Multiworlds - bislang keine weiteren
Rückmeldungen). Der Rest des Dokuments unten ist die Entstehungsgeschichte
und weiterhin akkurat; diese Sektion fasst nur zusammen, wie sich die echte
Umsetzung gegenüber dem ursprünglichen Plan verschoben hat.

**Zwei UI-Abweichungen vom ursprünglichen Plan, beide dev feedback nach dem
TestHarness-Prototyp:**
- Ein echtes, betiteltes `Window` (`Views/HintPickerWindow.axaml`, wie "Add
  slot"/"Edit server") statt eines `Flyout`s.
- Der "Hint..."-Button sitzt in der Info-/Statuszeile jedes Tabs (neben
  Connect/Disconnect/Refresh), nicht in der Nachrichtenzeile.

**Der Prototyp selbst (`TestHarness/HintInputPrototype/`) ist inzwischen
entfernt** - sobald die echten `Models.HintPickerRow`/`HintTargetMode`
existierten, kollidierten sie im Namensraum mit den gleichnamigen
Prototyp-Typen in `TestHarness`; Code sauber überführt statt beide
parallel zu pflegen (siehe `.claude/skills/ui-feature-prototyp`, Schritt 4).
Der "## Prototyp"-Abschnitt unten beschreibt entsprechend nur noch, was
*war*, nicht was noch existiert.

**Dev-gemeldeter Bug, gefunden und gefixt:** `HintEntry.SlotId` allein
reicht nicht, um "das ist mein Item" zu erkennen - siehe
`HintPickerViewModel.IsReceivingPlayer`/`BuildItemRows` für die Details
(kurz: `SlotId` wird auf den *Finder* zurückgesetzt, wenn der eigentliche
Empfänger kein hier konfigurierter Slot ist, was ohne den Fix fremde Items
wie "Memory of a Distant World" im eigenen Item-Pool eines Kirby-Super-
Star-Slots auftauchen ließ).

Alle drei Testkategorien (`HintPickerViewModelTests.cs`,
`ConnectionManagerHintTests.cs`, `HintPickerWindowTests.cs` - 28 Tests)
sind geschrieben und grün (166/166 in der Gesamt-Suite).

Ergänzt `archipolygo_feature_ideas.md` ("One-click `!hint` — a button that
sends the hint chat command instead of typing it manually").

## Ziel

Statt `!hint <itemname>` (oder `!hint_location <locationname>`) blind in die
Chat-Textbox zu tippen: eine durchsuchbare Auswahl der eigenen, noch nicht
gefundenen Locations, plus ein Button, der den Hint über die strukturierte
API auslöst - Tippfehler in Item-/Locationnamen (heute eine stille
Fehlerquelle: der Server versteht den Namen nicht und tut nichts) fallen
damit komplett weg.

## Kernbefund: `IHintsHelper.CreateHints` ersetzt den Chat-Befehl vollständig

`IHintsHelper` (bereits Teil des Kategorie-B-Seams, `FakeHintsHelper`
existiert schon in `AvaloniaApplication1.TestSupport`) bietet direkt:

```csharp
void CreateHints(int player, HintStatus hintStatus = HintStatus.Unspecified, params long[] locationIds);
void CreateHints(HintStatus hintStatus = HintStatus.Unspecified, params long[] locationIds); // impliziert den eigenen Spieler
```

Das ist der Server-Weg, den `!hint`/`!hint_location` unter der Haube auch
nehmen - nur ohne Text-Parsing und ohne Tippfehlerrisiko. Für die
Location-Auswahl selbst liefert `ILocationCheckHelper` alles Nötige:

```csharp
ReadOnlyCollection<long> AllMissingLocations { get; } // nur die noch nicht gefundenen - schon serverseitig gefiltert
long GetLocationIdFromName(string game, string locationName);
string GetLocationNameFromId(long locationId, string game = null);
```

## Scope für die erste Version

**Update (verifiziert, siehe unten):** Ursprünglich war hier nur der eigene
Leader-Slot vorgesehen, weil das protokollseitige Verhalten von
`CreateHints(player: ..., ...)` für einen anderen Slot als den eingeloggten
unklar war. Das ist inzwischen anhand der offiziellen
[Archipelago Network Protocol-Doku](https://github.com/ArchipelagoMW/Archipelago/blob/main/docs/network%20protocol.md#createhints)
geklärt - `CreateHints`s `player`-Feld sagt *in wessen Welt* die
Location liegt, nicht *für wen stellvertretend gesendet wird*: *"the packet
will fail if any of those locations don't contain items for the requesting
slot"* (`requesting slot` = die gerade eingeloggte Session). Ein Hint für den
eigenen, noch nicht gefundenen Check eines *anderen* konfigurierten Slots
lässt sich also nur zuverlässig auslösen, indem man tatsächlich **als dieser
Slot** eingeloggt ist (dann greift der Default "Defaults to the requesting
slot") - nicht, indem man beim Leader `player: <anderer Slot>` übergibt.

Damit ist die erste Version jetzt doch nicht auf den Leader beschränkt, denn
`ConnectionManager` hat mit `CatchUpSyncAsync`/`CatchUpSyncCoreAsync` schon
genau das etablierte Muster für "kurz als ein anderer, nicht-führender Slot
einloggen, ohne den Leader anzutasten" (siehe `CLAUDE.md`). Für Hints gilt
dieselbe Idee, nur aktiv statt passiv:

- Der Leader bleibt die ganze Zeit unangetastet verbunden - kein
  `SwitchLeaderAsync`, keine Lücke, kein `AutoConnect`/`PreferredLeaderSlotId`-
  Update, weil sich an der Leader-Frage nichts ändert.
- Für einen *anderen* Slot wird eine zusätzliche, eigene, temporäre
  `ArchipelagoSession` aufgemacht (nicht in `_leaderSlotByGroup` eingetragen,
  nicht `_sessions[leaderId]`) - lange genug, um die Locations zu lesen bzw.
  den Hint abzusetzen, dann sofort wieder getrennt.
- Diese temporäre Verbindung serialisiert sich über dasselbe per-Gruppe
  `_groupLocks`-Gate wie `SwitchLeaderAsync`/`CatchUpSyncAsync`/
  `DisconnectGroupAsync` - sonst würde sie einer laufenden Sweep/Switch/
  Disconnect-Operation für dieselbe Gruppe ins Gehege kommen (siehe
  "Multiple slots must never connect concurrently" in `CLAUDE.md`).
- Für den **Leader selbst** entfällt der Probe-Connect komplett - dessen
  Session ist ja schon offen (siehe `GetHintableLocationsAsync` unten).

## Anbindung

Neues Mitglied auf `IConnectionManager` (Muster wie `SendMessageAsync`, jetzt
mit explizitem Ziel-Slot statt implizit "der Leader"):

```csharp
Task<IReadOnlyList<HintableLocation>> GetHintableLocationsAsync(GroupViewModel group, SlotProfile slot);
Task SendHintAsync(GroupViewModel group, SlotProfile slot, long locationId);
```

`HintableLocation` ein neuer, kleiner Record in `Models/`
(`LocationId`, `Name`) - Namensauflösung via `GetLocationNameFromId(session.ConnectionInfo.Game, id)`.

- **Wenn `slot` der aktuelle Leader ist:** gleiches Muster wie das
  bestehende `GetRoomPlayersAsync` - die schon offene Leader-Session
  wiederverwenden, `Locations.AllMissingLocations` auflisten/benennen bzw.
  `Hints.CreateHints(locationId)` (ohne `player`-Parameter, defaultet auf den
  eingeloggten Slot) direkt aufrufen. Kein neuer Verbindungspfad.
- **Wenn `slot` *nicht* der Leader ist:** ein kurzer, eigener Probe-Connect
  nur für `slot` (analog `CatchUpSyncAsync`, aber aktiv statt passiv) -
  `Locations.AllMissingLocations` lesen bzw. `Hints.CreateHints(locationId)`
  über *diese* temporäre Session aufrufen, danach sofort wieder trennen. Der
  Leader bleibt währenddessen vollständig unangetastet verbunden - siehe
  "Scope für die erste Version" oben für die Begründung, warum das jetzt
  doch geht.
- In beiden Fällen: No-op ohne jede Verbindungsmöglichkeit (z. B. Server
  down), gleiches Verhalten wie `SendMessageAsync`. Das Ergebnis (der neue
  Hint) kommt danach ganz normal über die schon bestehende
  `OnHintsUpdated`/`TrackHints`-Pipeline zurück - **keine Sonderbehandlung
  nötig**, der selbst ausgelöste Hint taucht in `GroupViewModel.Hints` genauso
  auf wie jeder andere, unabhängig davon ob er über die Leader-Session oder
  einen Probe-Connect ausgelöst wurde.

## UI

**Update (echte Umsetzung wich hier vom ursprünglichen Plan ab):** Statt
eines `Flyout`s ist es ein echtes, betiteltes `Window`
(`Views/HintPickerWindow.axaml`) geworden - dev feedback: soll aussehen wie
"Add slot"/"Edit server", nicht wie ein Flyout. Der Button sitzt außerdem in
der Info-/Statuszeile jedes Tabs (neben Connect/Disconnect/Refresh), nicht
in der Nachrichtenzeile - dev feedback ebenfalls. Der Rest dieses Abschnitts
(Umschalter, Slot-Dropdown, Exclude-Checkbox, WrapPanel, Leer-Zustände,
Verhalten beim Senden) ist weiterhin akkurat, nur eben im Fenster statt im
Flyout.

Bestehendes Muster wiederverwenden statt neu erfinden: `ConnectionEditorViewModel`/
`ConnectionEditorWindow.axaml` hat mit `SearchText` + gefilterter, scrollbarer
Liste (`FilteredSlotRows`, `ScrollViewer` mit `Classes="dialog-list"`) für die
"Add slot"-Spielerauswahl schon exakt das gesuchte Interaktionsmuster.

- Ein "Hint..."-Button in der Info-/Statuszeile jedes Tabs (`Grid.Row="0"`,
  neben Connect/Disconnect/Refresh - siehe Update oben), der ein initial
  breiteres, aber frei skalierbares Fenster öffnet (dev feedback: die
  Zeilenliste soll sich beim Vergrößern "fluid" verhalten - siehe unten).
- Ein Umschalter **"Hint for item" (Default) / "Hint for location"** (dev
  feedback) - entspricht den zwei bestehenden Chat-Befehlen `!hint
  <itemname>` bzw. `!hint_location <locationname>`. Beide laufen serverseitig
  auf denselben `CreateHints(locationIds)`-Aufruf hinaus (siehe
  "Kernbefund"); im Item-Modus muss vorher noch der Item-Name auf eine oder
  mehrere Location-IDs aufgelöst werden - **wie genau, ist noch offen** und
  sollte vor der echten Umsetzung geklärt werden (vermutlich über
  `GetLocationIdFromName`, aber mit doppelten Item-Namen im selben Spiel ist
  das nicht zwangsläufig eindeutig).
- Im Item-Modus zeigt die Liste standardmäßig **alle** Items des gewählten
  Slots, auch bereits gefundene/bereits gehintete Kopien (dev feedback: ein
  Spiel kann mehrere Kopien desselben benannten Items an verschiedenen
  Locations platzieren - "eine Kopie schon gefunden/gehintet" heißt nicht,
  dass jede andere Kopie damit erledigt ist). Eine Checkbox "Exclude already
  found/hinted" blendet diese Kopien optional aus, ist aber standardmäßig
  aus. Für den Location-Modus gilt weiterhin die alte Regel ohne Checkbox:
  bereits gecheckte/gehintete Locations werden immer ausgeblendet (davon
  gibt's ja nur je eine Kopie).
  **Für die echte Umsetzung wichtig** (dev-gemeldeter Bug im Prototyp, siehe
  unten): "bereits gehintet" pro Item-Kopie muss aus einem Live-Abgleich mit
  `GroupViewModel.Hints` für den gewählten Slot kommen (Location-Name/-Id
  einer bestehenden `HintEntry`), nicht aus einem einmal beim Laden
  berechneten, statischen Flag - sonst zeigt die Checkbox einen gerade erst
  über den Picker selbst gesendeten Hint nicht als erledigt an.
- Im Flyout, darüber: ein Slot-Dropdown (dev feedback beim Durchklicken des
  TestHarness-Prototyps, siehe `TestHarness/HintInputPrototype/`) - zeigt
  standardmäßig den Leader, erlaubt aber den Wechsel zu jedem anderen
  konfigurierten Slot der Gruppe, *ohne* dabei den Leader zu wechseln. Rein
  UI-seitig unproblematisch; das eigentliche Senden für einen Nicht-Leader-
  Slot hängt am Probe-Connect aus "Anbindung" oben.
- Darunter: Suchfeld + gefilterte, **fluid** umbrechende Liste (dev
  feedback: ein `WrapPanel` statt einer Ein-Eintrag-pro-Zeile-Liste - die
  einzelnen Zeilen/Buttons ordnen sich abhängig von der Fensterbreite
  nebeneinander an, ohne dass der Text *innerhalb* eines Buttons umbricht)
  der `HintableLocation`s bzw. `HintableItem`s des gerade im Dropdown
  gewählten Slots (analog zum Add-slot-Picker für Suchfeld + Filterung).
  Items werden dabei **nach Name dedupliziert** (dev feedback) - eine Zeile
  pro Namen, nicht eine pro Kopie; ein Name verschwindet nur, wenn (mit
  aktivierter Exclude-Checkbox) *jede* Kopie bereits gefunden/gehintet ist.
  Klick auf eine Zeile ruft `SendHintAsync(group, gewählterSlot, ...)` auf -
  das Flyout schließt sich dabei **nicht** (dev feedback: mehrere Hints
  sollen in einer Sitzung sendbar sein, ohne es jedes Mal neu zu öffnen); im
  Location-Modus verschwindet die gerade gehintete Location sofort aus der
  Liste, im Item-Modus bleibt die (deduplizierte) Zeile stehen, da sich
  nicht unterscheiden lässt, welche der ggf. mehreren Kopien gemeint war.
- Zwei getrennte Leer-Zustände (ebenfalls aus dem Prototyp-Feedback): "für
  diesen Slot/Modus ist nichts verfügbar" vs. "Sucheingabe matcht nichts" -
  lesen sich unterschiedlich und sollten nicht denselben Text teilen.
- Ein "Close"-Button (nicht "Cancel" - dev feedback, es gibt ja nichts mehr
  abzubrechen, da Senden das Flyout nicht mehr schließt) bleibt unabhängig
  von der Fensterhöhe immer unten rechts fixiert.
- Die bestehende Chat-Textbox bleibt unverändert erhalten - das ist additiv,
  kein Ersatz, falls jemand weiterhin andere Befehle (`!getitem`,
  `!collect`, ...) manuell tippen will.

## Prototyp (entfernt, siehe Status oben)

Ein anklickbarer Prototyp dieses Flyouts lag in
`TestHarness/HintInputPrototype/` (`HintInputPrototypeViewModel`,
`HintInputPrototypeWindow.axaml`, `HintableLocationRow`/`HintableItemRow`/
`HintableSlotOption`/`HintPickerRow`/`HintTargetMode`), aufrufbar über den
"Open Hint picker (prototype)"-Button in `ControlPanelWindow` - siehe
`.claude/skills/ui-feature-prototyp`. Bildet Item-/Location-Umschalter,
Slot-Dropdown, die "Exclude already found/hinted"-Checkbox, Item-Dedup, das
fluide WrapPanel-Layout, den unten rechts fixierten Close-Button und beide
Leer-Zustände bereits nach; sendet aber noch nichts über
`IConnectionManager` - ein Klick auf eine Zeile speist nur eine synthetische
`HintEntry`/`EventEntry` direkt in die Demo-Gruppe ein (mit einem
Platzhalternamen für die jeweils andere Hälfte des Hints, da der Sinn einer
Anfrage ja gerade ist, die noch nicht zu kennen) und schließt dabei bewusst
nicht das Fenster (`HintInputPrototypeWindow.HintRequested`-Event statt
`ShowDialog`-Rückgabewert - siehe `ControlPanelWindow.OpenHintInputPrototypeAsync`).

## Tests

- **Kategorie A** (`AvaloniaApplication1.Tests/HintPickerViewModelTests.cs`,
  ✅ geschrieben): `HintPickerViewModel`s Item-Modus dedupliziert nach Namen,
  vereint `ReceivedItems` + `Hints`, schließt Hints aus, bei denen der Slot
  nur Finder (nicht Empfänger) ist (Regressionstest für den unten
  beschriebenen Bug, per Name **und** Alias), die Exclude-Checkbox blendet
  nur vollständig aufgelöste Namen aus. Location-Modus (über
  `FakeConnectionManager.SetHintableLocations` - bereits abgeschlossene
  Tasks, kein Avalonia-Dispatcher nötig) schließt bereits gehintete
  Locations aus. Beide Leer-Zustände, Slot-Wechsel, Such-Filter,
  `SendCommand`-Verhalten (Location entfernt Zeile sofort, Item lässt sie
  stehen) sind abgedeckt.
- **Kategorie B** (`AvaloniaApplication1.Tests/ConnectionManagerHintTests.cs`,
  ✅ geschrieben): `GetHintableLocationsAsync` liefert für den Leader-Slot
  (Session wiederverwendet, kein Extra-Connect) korrekt benannte
  `AllMissingLocations` zurück, inkl. Fallback-Namen für unbekannte IDs; für
  einen Nicht-Leader-Slot per kurzem Probe-Connect, der den Leader
  nachweislich unangetastet lässt (`Socket.DisconnectCallCount == 0`,
  `LeaderSlotId` unverändert) und sich danach selbst wieder trennt.
  `SendHintAsync` prüft `FakeHintsHelper.CreateHintsCalls` ebenso für
  Leader- und Probe-Connect-Fall. `SendItemHintAsync` prüft den
  `!hint <name>`-Text in `FakeArchipelagoSession.SentMessages` und ist ein
  No-op bei leerem Namen. Ein Test mit zwei Probe-Connects für
  unterschiedliche Slots derselben Gruppe belegt die Serialisierung über
  `_groupLocks` (kein zweiter Verbindungsversuch, bevor der erste fertig
  ist). Dafür ergänzt: `FakeLocationCheckHelper.GetLocationNameFromId`
  (settable `LocationNames`-Dictionary), `FakeHintsHelper.CreateHints`
  (`CreateHintsCalls`-Liste, wie im ursprünglichen Plan vorgesehen),
  `FakePlayerHelper.GetPlayerInfo(int slot)` (Lookup über `AllPlayers`) -
  alle drei waren zuvor unbenutzte `NotImplementedException`-Stubs.
- **Kategorie C** (`AvaloniaApplication1.Tests/HintPickerWindowTests.cs`,
  ✅ geschrieben): echte `HintPickerWindow`-Instanz, echter simulierter Klick
  (Muster wie `RemoveConfiguredSlotButtonTests`, nicht nur ein
  Command-Aufruf). Deckt ab: Item-Modus-Zeilen beim Öffnen, Slot-ComboBox-
  Wechsel über die echte Control-Instanz zeigt die richtige Slot-eigene
  Zeilenmenge, Klick auf "Hint for location" schaltet um, Sucheingabe
  filtert die sichtbaren Zeilen-Buttons, Klick auf eine Location-Zeile
  sendet den Hint und entfernt die Zeile sofort **ohne** das Fenster zu
  schließen, Klick auf eine Item-Zeile sendet den Hint und lässt die Zeile
  stehen, Klick auf "Close" schließt das Fenster tatsächlich, und der
  "Nothing available"-Leerzustand erscheint korrekt wenn nichts verfügbar
  ist.
