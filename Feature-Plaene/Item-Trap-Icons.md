# Feature-Idee (grobe Skizze): Bessere Item-/Trap-Icons

**Status: 🗨️ Muss noch durchdiskutiert werden** - das hier ist keine
Umsetzungsplan im Sinne von `Tab-Reihenfolge.md`, sondern nur eine grobe
erste Skizze, um die offene Diskussion festzuhalten. Ursprünglich aus der
inzwischen archivierten Datei `archipolygo_feature_ideas.md`
("Better item/trap icons") übernommen.

## Ausgangslage

Aktuell wird ein empfangenes Item (`ReceivedItemEntry`) nur über Farbe und
Text unterschieden, nicht über ein Icon:

- `ReceivedItemEntry.ItemKind` (ein `EventTextSegmentKind`) steuert
  ausschließlich die Textfarbe des Item-Namens (progression / useful / trap
  / normal) über den gemeinsam genutzten `SegmentKindToBrushConverter` - kein
  Icon-Konzept vorhanden.
- Dieselbe Klassifizierung (`ItemFlags` aus
  `Archipelago.MultiClient.Net`) taucht auch bei Hints auf
  (`HintEntry.ItemFlags`, `HintSnapshot.ItemFlags`).

## Die eigentliche offene Frage

"Spielspezifische Icons" heißt: pro Archipelago-Spiel (es gibt hunderte
verschiedene Spiele/Randomizer im Ökosystem) ein eigenes Set an Icons für
Progression-/Useful-/Trap-Items, nicht nur eine generische
Icon-pro-Kategorie-Lösung (die wäre trivial - im Kern nur ein Icon statt
Text-Farbe an derselben Stelle, wo `SegmentKindToBrushConverter` heute
schon greift). Fragen, die vor einem echten Plan geklärt werden müssen:

- Reicht eine generische Kategorie-Icon-Lösung (ein Icon pro
  progression/useful/trap/normal, unabhängig vom Spiel), oder ist wirklich
  spielspezifische Grafik gemeint (z. B. das tatsächliche Item-Sprite aus
  dem jeweiligen Randomizer)?
- Falls spielspezifisch: woher kommen die Icons? Es gibt kein zentrales,
  lizenzfreies Icon-Repository für alle Archipelago-Spiele - das wäre ein
  erheblicher Beschaffungs-/Pflegeaufwand (neue Spiele kommen laufend dazu).
  Archipelago selbst (die Website/der Webhost) hat teils Item-Icons pro
  Spiel im Multiworld-Tracker - zu prüfen, ob/wie die sich lizenzkonform
  wiederverwenden ließen.
- Wo würde die Zuordnung Item-Name → Icon gepflegt (eine Mapping-Datei pro
  Spiel? Wie wird das erweiterbar gehalten, ohne bei jedem neuen
  unterstützten Spiel Code anfassen zu müssen)?
- Fallback für Spiele ohne eigenes Icon-Set: vermutlich weiterhin die
  generische Kategorie-Färbung/-Icon.

## Betroffene Stellen (grob, noch nicht verifiziert)

- `Models/ReceivedItemEntry.cs` (`ItemKind`) und die Anzeige der Items-Liste
  in `Views/MainWindow.axaml`.
- `Models/HintEntry.cs`/`HintSnapshot.cs` (`ItemFlags`) für die
  Hints-Ansicht, falls Icons dort ebenfalls gewünscht sind.
- `SegmentKindToBrushConverter` als bestehende Stelle, die die generische
  Kategorie-Unterscheidung schon kennt.
