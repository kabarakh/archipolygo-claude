# Umsetzungsplan: Summary/Dashboard-Tab

Ergänzt `archipolygo_feature_ideas.md` ("A summary/dashboard tab — all
servers at a glance: total open hints, total unread events"). Nutzer-
Entscheidung: **umschaltbare Ansicht statt echtem TabItem** - kein Eingriff
in die bestehende `TabControl`-Struktur.

## Warum nicht als echtes TabItem

Ein Dashboard-Eintrag direkt in `TabControl.ItemsSource="{Binding Groups}"`
zu mischen würde eine gemischte Collection (`DashboardViewModel` + mehrere
`GroupViewModel`) brauchen und damit den Wechsel von einem expliziten
`TabControl.ItemTemplate`/`ContentTemplate` auf implizite, typbasierte
`DataTemplate`s erzwingen (Avalonia wählt dann automatisch das zum
Laufzeit-Typ passende Template, ähnlich der schon vorhandenen
`Application.DataTemplates`/`ViewLocator`-Registrierung in `App.axaml`, nur
lokal auf diese eine `TabControl` bezogen). `MainWindow.axaml`s
`TabControl` hat laut CLAUDE.md bereits einen dokumentierten, echten Bug
durch genau diese Art von Komplexität (Lazy-Materialization der
`ContentTemplate`, siehe `OnChatSlotComboBoxLoaded`) - ein zweiter,
heterogener Item-Typ ist ein plausibler Nährboden für eine neue Variante
desselben Bug-Musters. Deshalb: Dashboard bewusst **außerhalb** der
`TabControl`, keine Vermischung.

## Struktur

- Neuer Toggle-Button in der Toolbar (`StackPanel DockPanel.Dock="Top"` ganz
  oben in `MainWindow.axaml`, neben "Settings..."): "Dashboard" / "Zurück zu
  Tabs" (Text wechselt je nach Zustand, oder ein reiner Toggle mit
  `IsChecked`-Style).
- Neue `bool` Property auf `MainWindowViewModel`: `IsDashboardVisible`.
- Im `DockPanel` (das den Hauptbereich unter der Toolbar füllt): die
  bestehende `TabControl` bekommt `IsVisible="{Binding !IsDashboardVisible}"`,
  eine neue `DashboardView` daneben `IsVisible="{Binding IsDashboardVisible}"`
  - beide im selben Grid-Bereich übereinander, nur je eine sichtbar. Kein
  Umbau der `TabControl` selbst nötig, sie bleibt exakt wie heute.

## `DashboardViewModel`

Neue Datei `ViewModels/DashboardViewModel.cs`. Bekommt im Konstruktor die
`ObservableCollection<GroupViewModel> Groups` von `MainWindowViewModel`
(Referenz, keine Kopie) sowie eine Möglichkeit, `MainWindowViewModel.SelectedGroup`
zu setzen (z. B. ein `Action<GroupViewModel>`-Callback oder direkte Referenz
auf `MainWindowViewModel`, analog dazu, wie `ConnectionEditorViewModel`
schon Callbacks statt harter Kopplung nutzt, z. B. `_removeSlotAsync`).

Berechnete Properties, die über alle `Groups` aggregieren:

```csharp
public int TotalUnreadEvents => Groups.Sum(g => g.UnreadEventCount);
public int TotalUnfoundHints => Groups.Sum(g => g.UnfoundHintCount);
```

Reaktivität: `Groups.CollectionChanged` abonnieren (Gruppe hinzugefügt/entfernt
→ neu aggregieren, plus bei jeder hinzukommenden/wegfallenden Gruppe auch
deren `PropertyChanged` de-/abonnieren) und auf `UnreadEventCount`/
`UnfoundHintCount`-Changes jeder einzelnen Gruppe reagieren - gleiches Prinzip
wie `GroupViewModel.RefreshSlotOrder`s Reaktion auf `Group.Slots.CollectionChanged`,
nur eine Ebene höher (Gruppen statt Slots).

Pro-Gruppe-Zeile für die Liste: kein extra Row-Modell nötig, `GroupViewModel`
selbst hat schon alles (`HeaderText`, `ConnectionState`, `HasUnreadEvents`,
`UnreadEventCount`, `UnfoundHintCount`) - `DashboardViewModel.Groups`
direkt als `ItemsSource` einer `ItemsControl`/`ListBox` binden.

## `DashboardView.axaml`

Neue View, grob:

- Kopfzeile: "N Server, M ungelesene Events, K offene Hints" (die drei
  aggregierten Werte).
- Liste darunter, eine Zeile pro `GroupViewModel`: Status-Punkt (bestehender
  `ConnectionStateToBrushConverter` wiederverwenden), Servername
  (`HeaderText`), ungelesene Events (Badge wie im Tab-Header, siehe
  `TabControl.ItemTemplate` in `MainWindow.axaml` als Vorlage), offene
  Hints. Klick auf eine Zeile: `SelectedGroup` setzen **und**
  `IsDashboardVisible = false` (zurück zur `TabControl`, direkt auf dem
  gewählten Tab).
- Der Fortschrittsanzeigen-Plan ist inzwischen umgesetzt (siehe
  `Feature-Plaene/Archiv/Fortschrittsanzeigen.md`) - `GroupViewModel.RoomProgressPercent`
  existiert bereits und ist genau für diesen Zweck stehen geblieben (siehe
  dessen Doc-Kommentar). Sobald dieser Dashboard-Plan selbst umgesetzt wird:
  hier zusätzlich pro Zeile anzeigen - das Dashboard ist der naheliegende Ort
  für den dort bereits erwähnten "App-weiten Aggregat"-Wert (Summe über alle
  Gruppen hinweg). Die Tier-2-Multiworld-Anzeige (eine kombinierte 4-Segment-
  Bar pro Server, siehe Archiv-Plan) ist bewusst *nicht* Teil davon - eigene,
  unabhängige Datenquelle.

## Tests

- **Kategorie A**: `DashboardViewModel`s Aggregations-Properties (mehrere
  `GroupViewModel`s mit `FakeConnectionManager` konstruieren, wie es
  `GroupViewModelOrderingTests` schon tut, `Events`/`Hints` synthetisch
  befüllen, Summen prüfen) sowie die Reaktivität bei Hinzufügen/Entfernen
  einer Gruppe.
- **Kategorie C**: Toggle-Button schaltet zwischen `TabControl` und
  `DashboardView` sichtbar um; Klick auf eine Dashboard-Zeile setzt
  `SelectedGroup` korrekt und blendet zurück zur `TabControl`.
