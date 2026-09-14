# Umsetzungsplan: manuelles Tab-Reordering (Drag & Drop)

## Status: ✅ Umgesetzt (2026-09-14)

Erst grob im `TestHarness` nachgebaut (identische Views/ViewModels wie die
echte App, per `ProjectReference` - siehe `.claude/skills/ui-feature-prototyp/SKILL.md`),
dort mehrfach mit dem Entwickler durchgesprochen und korrigiert, bevor es als
fertig galt. Weicht an mehreren Stellen vom Plantext unten ab:

- **Payload-Format:** `DataFormat.CreateInProcessFormat<GroupViewModel>`
  allein reicht nicht - auf macOS crasht `DoDragDropAsync`, wenn das
  einzige `DataTransferItem` gar nichts pasteboard-schreibbares enthält
  (`NSDraggingSession`-Exception "There are 0 items on the pasteboard, but
  1 drag images"). Fix: dasselbe Item trägt zusätzlich eine reine
  `DataFormat.Text`-Repräsentation (nie zurückgelesen, nur für die native
  Drag-Brücke) - siehe `GroupReorderDragDrop.StartDragAsync`.
- **Avalonia-12.0.4-API bestätigt:** tatsächlich schon `DataTransfer`/
  `DataTransferItem`/`DragDrop.DoDragDropAsync(PointerPressedEventArgs, ...)`
  - nicht das ältere `DataObject`/`DoDragDrop`, das der Plantext unten als
  Möglichkeit offenließ. Gegen den echten 12.0.4-Quellcode verifiziert
  (nicht die main-Branch-Doku, die der Plan selbst schon als nur grobe
  Orientierung markiert hatte).
- **Visuelles Feedback ging über die "offene Detailfrage" unten hinaus:**
  statt nur Standard-Cursor (die eigene Empfehlung im Plan für v1) wurde
  eine echte Einfüge-Linie gebaut (`GroupViewModel.DropIndicator`, Enum
  `Before`/`After`/`None`, in `Models/DropIndicatorPosition.cs`) - ein
  dünner Strich an der jeweiligen Kante des Ziel-Tabs/der Ziel-Zeile, je
  nachdem auf welcher Hälfte gerade gehovert wird (Dev-Wunsch, nachdem die
  erste, einfachere Version - ein Rahmen ums ganze Ziel-Element,
  `GroupViewModel.IsDropTarget` als bool - sich im TestHarness als "klebt
  zu sehr am Tab-Text" erwies). Diese erste Rahmen-Version brachte
  außerdem den CLAUDE.md-Gotcha zu "ein lokaler XAML-Attributwert schlägt
  immer einen Style-Setter" zutage (ein lokales `BorderBrush="Transparent"`
  machte den eigentlich vorgesehenen `DodgerBlue`-Style-Setter zu totem
  Code).
- **Dashboard-Overview bekam dieselbe Funktion, nicht nur die Tableiste:**
  der Plan unten entstand vor dem Dashboard-Feature und deckt nur die
  Tableiste ab. Die Overview-Zeilen navigieren aber schon beim
  `PointerPressed` weg vom Dashboard (Avalonia wählt eine `ListBoxItem` bei
  Maus-Input beim Drücken aus, nicht erst beim Loslassen) - ein Drag hätte
  dort nie eine Chance gehabt zu starten. Lösung: ein dediziertes, kleines
  Grip-Icon (`Border.drag-handle` in `DashboardView.axaml`) startet dort
  den Drag; der Rest der Zeile bleibt reine Klick-Navigation
  (`ListBox.SelectionChanged`, wie vor diesem Feature). Bei der Tableiste
  war das nicht nötig - ein Klick auf einen Tab wählt ihn dort nur aus,
  kein Konflikt mit Drag.
- **Index-Berechnung für Vor/Nach-Ziel:** `MainWindowViewModel.ReorderGroup`
  nimmt jetzt einen dritten Parameter `insertAfter` (abgeleitet aus dem
  zuletzt beim DragOver gesetzten `DropIndicator`) und rechnet die exakte
  `ObservableCollection<T>.Move`-Zielposition entsprechend um (`Move`
  arbeitet in Post-Removal-Indizierung, siehe die Methode selbst) - im
  Plan unten noch nicht vorgesehen, der nur eine einfache "an
  Zielposition" ohne Vor/Nach-Unterscheidung kannte.
- **Padding/Margin-Umbau für die Linie:** damit die Einfüge-Linie exakt auf
  der echten Grenze zwischen zwei Tabs/Zeilen landet statt vom
  Fluent-Theme-Padding (`TabItem`/`ListBoxItem`) eingerückt zu sein, wurde
  dieses Theme-Padding auf beiden Elementen auf `0` gesetzt und stattdessen
  als `Margin` auf dem jeweiligen Inhalt nachgebildet (dev feedback: erste
  Version wirkte "sehr flach", weil dabei zunächst zu wenig Margin
  nachgebildet wurde - korrigiert).
- **Tests:** die eigentliche Drag-Geste (Press/Move-Schwellenwert,
  `DoDragDropAsync`) lässt sich unter Avalonia.Headless nicht simulieren
  (kein registrierter `IPlatformDragSource`) - ehrlich als Lücke
  dokumentiert, genau wie unten im Plan vorgesehen. Stattdessen: Kategorie
  A gegen `MainWindowViewModel.ReorderGroup`/`DashboardViewModel.ReorderGroup`
  direkt (`GroupReorderTests.cs`), Kategorie C gegen die echten
  DragOver/DragLeave/Drop-Handler per händisch gebautem `DragEventArgs`
  (`TabReorderDragDropTests.cs`).

---

Ergänzt die ursprünglich in `archipolygo_feature_ideas.md` gelistete Idee
"Manual tab reordering (drag & drop) instead of only automatic host/port
grouping" (dieser Index ist inzwischen archiviert). Nutzer-Entscheidung:
echtes Drag & Drop, keine Verschieben-Buttons.

## Wichtiger Vorbehalt zur Avalonia-Version

Die hier skizzierte `DragDrop`-API (`Avalonia.Input.DragDrop`, `AllowDrop`,
`DragEnter`/`DragLeave`/`DragOver`/`Drop`, `DoDragDrop`/`DoDragDropAsync`)
stammt aus der aktuellen Avalonia-Dokumentation (`main`-Branch) - **nicht**
gegen die im Projekt exakt gepinnte Version 12.0.4 verifiziert. Die dortige
Doku zeigt bereits neuere Typen (`DataTransfer`/`DataTransferItem`/
`DoDragDropAsync`), die in 12.0.4 möglicherweise noch `DataObject`/
`DoDragDrop` heißen.

CLAUDE.md legt dazu eine Grundregel fest, die für **jedes** in diesem
Projekt referenzierte NuGet-Paket gilt, nicht nur für
`Archipelago.MultiClient.Net` (dort nur am häufigsten genannt, weil dieses
Projekt am meisten direkt mit dieser Bibliothek arbeitet) - Avalonia selbst
eingeschlossen, auch wenn es das UI-Framework der ganzen App ist: **niemals
die lokal installierte Paket-DLL decompilen**, um an API-Details zu kommen.
Vor Implementierungsbeginn stattdessen zwingend die offizielle
Avalonia-Doku zur exakt gepinnten Version 12.0.4 (nicht die `main`-Branch-
Doku, die hier oben nur als grobe Orientierung diente) bzw., falls die Doku
nicht ausreicht, den Quellcode am passenden 12.0.4-Tag im Avalonia-Repository
konsultieren.

## Vorbereitung für ein späteres Follow-up: Tab in eigenes Fenster lösen

[`Tab-Eigenes-Fenster.md`](../Tab-Eigenes-Fenster.md) beschreibt separat die
Idee "Tab in eigenes Fenster lösen" (Multi-Monitor). Die naheliegende UX dafür ist dieselbe
Drag-Geste wie hier: innerhalb der Tableiste losgelassen = Reorder, außerhalb
der `TabControl`-Bounds losgelassen = neues Fenster. Das wird hier bewusst
**nicht** mitgebaut - Multi-Window ist ein deutlich größeres Feature (die App
ist aktuell komplett Single-Window; es bräuchte eine Aufteilung von
`MainWindowViewModel.Groups` auf mehrere Fenster, `groups.json`-Persistenz
für Fenster-Zuordnung/-Position/-Monitor, und Start-Sequenzierung für mehr
als ein Fenster). Damit dieses spätere Follow-up die Drag-Infrastruktur von
hier wiederverwenden kann, statt sie neu zu bauen: die Pointer-Threshold-
Erkennung (`PointerPressed`/`PointerMoved` ab Bewegungs-Schwellenwert) und
der `DoDragDrop`-Payload (Quell-`GroupViewModel` über das
`"ArchipolygoGroupTab"`-Format) so schreiben, dass sie nicht an "Ziel ist
eine andere Tab-Position in derselben `TabControl`" gebunden sind, sondern
generisch genug bleiben, um später um einen zusätzlichen
"außerhalb jeder `TabControl` losgelassen"-Fall erweitert zu werden.

**Umsetzung:** genau so gebaut - `GroupReorderDragDrop` (in
`Views/GroupReorderDragDrop.cs`) kennt nur den Payload (Quell-`GroupViewModel`
über das `"ArchipolygoGroupTab"`-Format) und die Press/Move-Schwellenwert-
Erkennung, nichts über "Ziel ist eine TabControl-Position" - die eigentliche
Zielauflösung (Tab-Header vs. Overview-Zeile, deren jeweilige
Vor/Nach-Bestimmung) lebt komplett in den beiden Views' eigenem Code-behind.
Ein späteres "außerhalb jeder `TabControl` losgelassen"-Fall für dieses
Follow-up kann also darauf aufsetzen, ohne die Drag-Erkennung selbst
anzufassen.

## Ansatz

`MainWindow.axaml`s `TabControl.ItemTemplate` (die `DataTemplate
DataType="vm:GroupViewModel"` mit Ellipse/Badge/Text im
`StackPanel Orientation="Horizontal"`, siehe CLAUDE.md-Gotcha zur
Vertical-Alignment-Falle in genau diesem Element) ist bereits der Tab-Header
- hier setzt das Drag & Drop an:

1. Auf das äußere `StackPanel` im `ItemTemplate`: `DragDrop.AllowDrop="True"`
   plus Pointer-Handler (`PointerPressed`/`PointerMoved`) im Code-behind, die
   ab einem kleinen Bewegungs-Schwellenwert (verhindert versehentliches
   Auslösen bei einem normalen Tab-Klick) `DragDrop.DoDragDrop(...)` starten,
   mit der Quell-`GroupViewModel`-Instanz als Payload (z. B. über ein
   `DataObject` mit eigenem Format-Key wie `"ArchipolygoGroupTab"`).
2. `DragOver`/`Drop`-Handler auf demselben Element: beim Drop die
   Zielposition ermitteln (`sender`s `DataContext` ist die Ziel-`GroupViewModel`),
   `MainWindowViewModel.Groups.Move(oldIndex, newIndex)` aufrufen -
   `ObservableCollection<T>.Move` existiert bereits eingebaut, kein eigener
   Remove+Insert nötig.
3. Nach erfolgreichem Move: `PersistGroups()` aufrufen (existiert schon auf
   `MainWindowViewModel`, wird nach jeder anderen Mutation - Add/Remove/Edit -
   bereits genauso aufgerufen). Da `TabControl.ItemsSource` direkt an
   `Groups` gebunden ist (keine zwischengeschaltete sortierte/gefilterte
   View), spiegelt sich die neue Reihenfolge sofort in der UI **und** beim
   nächsten Start (weil `groups.json` in Insertion-Reihenfolge von `Groups`
   geschrieben wird - keine zusätzliche "OrderIndex"-Property nötig).

## Was bewusst gleich bleibt

- `SelectedGroup`/das aktive Tab überlebt einen Reorder automatisch - die
  Bindung folgt der Objektreferenz, nicht dem Index.
- Kein Sonderfall für die Sortierung innerhalb `GroupViewModel.Slots` (das
  ist die "Chat as"-Dropdown-Sortierung, siehe CLAUDE.md) - hier geht es nur
  um die Reihenfolge der `ServerConnectionGroup`s selbst
  (`MainWindowViewModel.Groups`), ein komplett getrennter Mechanismus.

## Offene Detailfrage für die Umsetzung

Visuelles Feedback während des Drags (z. B. ein Einfüge-Indikator zwischen
zwei Tabs, oder nur der Standard-Cursor der `DragDropEffects`) - Avalonia
liefert dafür keine fertige Tab-spezifische Lösung, das müsste selbst
gebaut werden (z. B. `DragOver` setzt eine `bool IsDropTarget`-Property auf
der Ziel-`GroupViewModel`, an die ein `Style`-Trigger im `ItemTemplate`
einen Rahmen bindet). Für die erste Version reicht vermutlich der
Standard-Cursor; ein Indikator wäre ein optionales Polish-Follow-up.

**Umsetzung:** ein echter Einfüge-Indikator wurde gebaut (siehe Status oben)
- nicht als optionales Follow-up, sondern gleich als Teil dieser
  Implementierung, auf expliziten Dev-Wunsch.

## Tests

- **Kategorie C**: `[AvaloniaFact]`, echtes `MainWindow` mit mehreren
  Gruppen aufbauen (Muster wie in `ChatSlotComboBoxTests`/
  `EventsListAutoScrollTests` schon etabliert), Drag-Geste zwischen zwei
  Tab-Headern simulieren (Avalonia-Headless bietet dafür
  `Window.MouseDown`/`MouseMove`/`MouseUp` - kein dediziertes
  Drag-Helper-Äquivalent bekannt, ggf. reicht die reine Pointer-Sequenz, da
  `DoDragDrop` selbst synchron auf Pointer-Events reagiert; falls sich das
  in der Praxis nicht sauber simulieren lässt, ersatzweise die
  Reorder-Methode direkt aufrufen und nur `Groups.Move` + `PersistGroups`-
  Aufruf testen, nicht die Drag-Geste selbst - ehrlich als Lücke
  dokumentieren, analog zum bereits bekannten Caveat bei
  `ChatSlotComboBoxTests`), danach `Groups`-Reihenfolge und
  `FakePersistenceService`-Aufruf prüfen.

**Umsetzung:** genau der dokumentierte Fallback - `DoDragDropAsync`/die
Pointer-Gestenerkennung selbst ließ sich unter Avalonia.Headless nicht
sauber simulieren (kein registrierter `IPlatformDragSource`), ersatzweise
direkte Kategorie-A-Tests gegen die Reorder-Methoden
(`GroupReorderTests.cs`) plus Kategorie-C-Tests gegen die echten
DragOver/DragLeave/Drop-Handler per händisch gebautem `DragEventArgs`
(`TabReorderDragDropTests.cs`) - siehe Status oben.
