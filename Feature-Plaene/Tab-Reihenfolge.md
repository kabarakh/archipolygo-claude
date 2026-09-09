# Umsetzungsplan: manuelles Tab-Reordering (Drag & Drop)

Ergänzt `archipolygo_feature_ideas.md` ("Manual tab reordering (drag & drop)
instead of only automatic host/port grouping"). Nutzer-Entscheidung: echtes
Drag & Drop, keine Verschieben-Buttons.

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
