# Feature-Idee (grobe Skizze): Bessere Item-/Trap-Icons

**Status: 🗨️ Diskutieren** - Diese Datei ist kein Umsetzungsplan wie
`Tab-Reihenfolge.md`. Sie ist nur eine grobe erste Skizze. Sie hält die
offene Diskussion fest. Die Idee kommt aus der Datei
`archipolygo_feature_ideas.md` ("Better item/trap icons"). Diese Datei ist
jetzt archiviert.

## Ausgangslage

Heute unterscheidet die App ein empfangenes Item (`ReceivedItemEntry`) nur
durch Farbe und Text. Sie zeigt kein Icon:

- `ReceivedItemEntry.ItemKind` (ein `EventTextSegmentKind`) steuert nur die
  Textfarbe des Item-Namens (progression / useful / trap / normal). Das
  macht der gemeinsame `SegmentKindToBrushConverter`. Ein Konzept für Icons
  gibt es nicht.
- Hints haben dieselbe Einteilung (`ItemFlags` aus
  `Archipelago.MultiClient.Net`), in `HintEntry.ItemFlags` und
  `HintSnapshot.ItemFlags`.

## Die eigentliche offene Frage

"Spielspezifische Icons" bedeutet: Jedes Archipelago-Spiel hat ein eigenes
Set an Icons für Progression-, Useful- und Trap-Items. Im Ökosystem gibt es
hunderte Spiele und Randomizer. Eine generische Lösung mit einem Icon pro
Kategorie ist einfach. Sie zeigt im Kern ein Icon statt einer Textfarbe. Das
passiert an derselben Stelle, an der heute `SegmentKindToBrushConverter`
arbeitet.

Kläre diese Fragen vor einem echten Plan:

- Reicht eine generische Lösung mit einem Icon pro Kategorie
  (progression/useful/trap/normal, unabhängig vom Spiel)? Oder sind wirklich
  Grafiken pro Spiel gemeint, zum Beispiel das echte Item-Sprite aus dem
  Randomizer?
- Wenn die Icons pro Spiel sind: Woher kommen sie?
  - Es gibt kein zentrales, lizenzfreies Repository mit Icons für alle
    Archipelago-Spiele.
  - Die Icons zu beschaffen und zu pflegen ist darum viel Arbeit. Es kommen
    ständig neue Spiele dazu.
  - Archipelago selbst (die Website oder der Webhost) hat für manche Spiele
    Item-Icons im Multiworld-Tracker. Prüfe, ob und wie die App diese Icons
    mit passender Lizenz wiederverwenden darf.
- Wo pflegt das Projekt die Zuordnung Item-Name → Icon? Ist das eine
  Mapping-Datei pro Spiel? Wie bleibt die Zuordnung erweiterbar, ohne
  Änderung am Code für jedes neue unterstützte Spiel?
- Fallback für Spiele ohne eigenes Set an Icons: Wahrscheinlich bleibt dann
  die generische Farbe oder das generische Icon pro Kategorie.

## Betroffene Stellen (grob, noch nicht verifiziert)

- `Models/ReceivedItemEntry.cs` (`ItemKind`) und die Anzeige der Items-Liste
  in `Views/MainWindow.axaml`.
- `Models/HintEntry.cs`/`HintSnapshot.cs` (`ItemFlags`) für die Hint-Ansicht,
  wenn der Entwickler dort auch Icons will.
- `SegmentKindToBrushConverter`. Diese Stelle kennt die generische
  Unterscheidung der Kategorien schon.
