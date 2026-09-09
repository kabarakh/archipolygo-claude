# Umsetzungsplan: besseres Feld zum Hint-Absenden

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

Nur Hints für **den eigenen, aktuell verbundenen Leader-Slot** - genau das,
was heute per Chat-Text sowieso schon geht. Hints für andere, nicht-führende
konfigurierte Slots wären nur über eine zusätzliche, kurze Verbindung für
diesen Slot möglich (ähnlich einem Catch-up-Dip) - das protokollseitige
Verhalten von `CreateHints(player: ..., ...)` für einen *anderen* Spieler als
den eingeloggten ist nicht verifiziert und wird hier bewusst nicht
mitgeplant; bei Bedarf ein eigener, kleinerer Folge-Plan.

## Anbindung

Neues Mitglied auf `IConnectionManager` (Muster wie `SendMessageAsync`):

```csharp
Task<IReadOnlyList<HintableLocation>> GetHintableLocationsAsync(GroupViewModel group);
Task SendHintAsync(GroupViewModel group, long locationId);
```

`HintableLocation` ein neuer, kleiner Record in `Models/`
(`LocationId`, `Name`) - Namensauflösung via `GetLocationNameFromId(session.ConnectionInfo.Game, id)`.

- `GetHintableLocationsAsync`: gleiches Muster wie das bestehende
  `GetRoomPlayersAsync` - bevorzugt eine schon offene Leader-Session, sonst
  kurzer Probe-Connect, dann `Locations.AllMissingLocations` auflisten und
  benennen. Kein neuer Verbindungspfad, nur Wiederverwendung des etablierten
  "vorhandene Session bevorzugen, sonst kurz verbinden"-Musters.
- `SendHintAsync`: `_sessions[leaderId].Hints.CreateHints(locationId)` -
  No-op ohne verbundenen Leader, gleiches Verhalten wie `SendMessageAsync`.
  Das Ergebnis (der neue Hint) kommt danach ganz normal über die schon
  bestehende `OnHintsUpdated`/`TrackHints`-Pipeline zurück - **keine
  Sonderbehandlung nötig**, der selbst ausgelöste Hint taucht in
  `GroupViewModel.Hints` genauso auf wie jeder andere.

## UI

Bestehendes Muster wiederverwenden statt neu erfinden: `ConnectionEditorViewModel`/
`ConnectionEditorWindow.axaml` hat mit `SearchText` + gefilterter, scrollbarer
Liste (`FilteredSlotRows`, `ScrollViewer` mit `Classes="dialog-list"`) für die
"Add slot"-Spielerauswahl schon exakt das gesuchte Interaktionsmuster.

- Ein "Hint..."-Button in der Status-/Nachrichtenzeile (`Grid.Row="2"` im
  Tab-Content, neben dem "Chat as"-Dropdown), der ein `Flyout`/`Popup` öffnet
  (spart dauerhaften vertikalen Platz - die Spalten sind laut CLAUDE.md
  ohnehin schon eng, siehe das WrapPanel-Verhalten der Filter-Buttons).
- Im Flyout: Suchfeld + gefilterte Liste der `HintableLocation`s (analog zum
  Add-slot-Picker), Klick auf eine Zeile ruft `SendHintAsync` auf und
  schließt das Flyout.
- Die bestehende Chat-Textbox bleibt unverändert erhalten - das ist additiv,
  kein Ersatz, falls jemand weiterhin andere Befehle (`!getitem`,
  `!collect`, ...) manuell tippen will.

## Tests

- **Kategorie B**: `GetHintableLocationsAsync` liefert die per
  `FakePlayerHelper`/einer neuen `FakeLocationCheckHelper` (siehe auch
  Fortschrittsanzeigen-Plan - dieselbe Fake-Ergänzung wird dort ebenfalls
  gebraucht, nicht doppelt bauen) hinterlegten offenen Locations korrekt
  benannt zurück; `SendHintAsync` ruft `FakeHintsHelper.CreateHints` mit der
  richtigen Location-ID auf (dafür `FakeHintsHelper.TrackHintsCallCount`-
  Muster um eine `CreateHintsCalls`-Liste ergänzen) und ist ein No-op ohne
  Leader.
- **Kategorie C**: Flyout öffnet sich, Sucheingabe filtert die Liste korrekt,
  Klick auf eine Zeile triggert `SendHintAsync` und schließt das Flyout -
  gleiches Testmuster wie die bestehenden `RemoveConfiguredSlotButtonTests`
  (echter simulierter Klick, kein reiner Command-Aufruf).
