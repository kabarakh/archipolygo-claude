# Feature-Idee (grobe Skizze): Tab in eigenes Fenster lösen

**Status: 🗨️ Muss noch durchdiskutiert werden** - kein Umsetzungsplan im
Sinne von `Tab-Reihenfolge.md`, sondern eine grobe erste Skizze, bereits
etwas konkretisiert im Zusammenspiel mit dem Tab-Reordering. Ursprünglich
aus der inzwischen archivierten Datei
`archipolygo_feature_ideas.md` ("Pop a tab into its
own window (for multi-monitor setups)") übernommen.

## Zusammenspiel mit manuellem Tab-Reordering (2026-09-13 Diskussion)

Naheliegende UX: dieselbe Drag-Geste wie beim
[manuellen Tab-Reordering](Archiv/Tab-Reihenfolge.md) - innerhalb der Tableiste
losgelassen = Reorder, außerhalb der `TabControl`-Bounds losgelassen = neues
Fenster. Trotzdem **bewusst als eigenes, späteres Feature** geplant, nicht
gemeinsam mit dem Reordering gebaut: Tab-Reordering ist klein
(`ObservableCollection<T>.Move`, keine Persistenzänderung), dieses Feature
braucht dagegen echte Multi-Window-Unterstützung:

- Die App ist aktuell komplett Single-Window - kein `new Window(...)`,
  keine Fenster-Liste, `App.axaml.cs` erzeugt genau eine `MainWindow`.
- `MainWindowViewModel.Groups` müsste auf mehrere Fenster aufgeteilt werden
  (z. B. je eine `WindowViewModel`, die eine Teilmenge von `Groups` hält),
  oder ein zweites `MainWindowViewModel` pro Zusatzfenster.
- `groups.json`-Persistenz müsste sich merken, welche Gruppe in welchem
  Fenster liegt, plus Fenster-Position/-Größe/-Monitor, damit das beim
  nächsten Start wiederhergestellt wird.
- Start-Sequenzierung (`InitializeGroupsAsync`, `StartupGroupSpacing`)
  müsste mehrere Fenster gleichzeitig hochfahren statt nur eins.

`Tab-Reihenfolge.md`s eigener Plan merkt an, seine
Pointer-Threshold-Erkennung und den `DoDragDrop`-Payload generisch genug zu
halten, damit dieses Feature sie später wiederverwenden kann, statt sie neu
zu bauen. Inzwischen umgesetzt: genau dieser wiederverwendbare Teil lebt
jetzt als eigene, ziel-unabhängige Klasse `GroupReorderDragDrop`
(`Views/GroupReorderDragDrop.cs`) - siehe `Tab-Reihenfolge.md`s eigenen
Status-Abschnitt.

## Cross-Referenz zu [`Fenster-Blinken.md`](Fenster-Blinken.md)

Sobald Tabs in eigenen Fenstern leben können, kann die
Taskbar-/Titelleisten-Blinken-Idee nicht mehr pauschal "das" (einzige)
App-Fenster blinken lassen - sie muss auflösen, welches `Window` gerade die
betroffene Gruppe zeigt, und genau das blinken lassen. Welches der beiden
Features zuerst gebaut wird, sollte diese Auflösungslogik von vornherein
mitdenken.

## Weitere offene Fragen vor einem echten Plan

- Wie wird ein losgelöstes Fenster wieder eingedockt (zurück in die
  Haupt-Tableiste ziehen), oder ist das für die erste Version gar nicht
  vorgesehen (nur Schließen des Zusatzfensters, Gruppe bleibt dann wo?)?
- Können mehrere Gruppen in ein und dasselbe losgelöste Fenster gezogen
  werden (eigene Mini-Tableiste dort), oder ist ein losgelöstes Fenster
  immer genau eine Gruppe?
- Verhalten beim Beenden der App, während Zusatzfenster offen sind -
  müssen alle Fenster einzeln geschlossen werden, oder schließt eins alle?
