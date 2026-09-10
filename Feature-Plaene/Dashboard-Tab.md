# Umsetzungsplan: Summary/Dashboard-Tab

Ergänzt `archipolygo_feature_ideas.md` ("A summary/dashboard tab — all
servers at a glance: total open hints, total unread events"). Nutzer-
Entscheidung: **umschaltbare Ansicht statt echtem TabItem** - kein Eingriff
in die bestehende `TabControl`-Struktur.

## Mockup

[`Claude outputs/dashboard-tab-mockup.html`](../Claude%20outputs/dashboard-tab-mockup.html)
zeigt den optischen Zielzustand dieses Plans (Overview links, geteilte
Hints-Übersicht rechts, Splitter dazwischen, Kategorie-Filter statt
Found/Unfound-Toggle) - **die echte `DashboardView.axaml` soll optisch
tatsächlich so gebaut werden**, nicht nur als Diskussionsgrundlage. Layout,
Spaltenaufteilung, Badges/Fortschrittsbalken-Anordnung und die Filterleiste
der Hints-Spalte aus dem Mockup sind bewusst konkret, nicht nur illustrativ -
weicht die spätere Umsetzung optisch davon ab, sollte das ein bewusster
Schritt sein, keiner aus Nachlässigkeit.

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
- Darunter **nebeneinander**, nicht umschaltbar - Server-Übersicht links,
  geteilte Hints-Übersicht rechts, per `GridSplitter` getrennt: exakt dasselbe
  Layout-Muster wie Events | Hints/Items innerhalb eines Tabs (siehe
  `MainWindow.axaml`s `Grid Grid.Row="1"` mit `ColumnDefinitions="2*,8,1*"`
  und `MinWidth` auf beiden äußeren Spalten). Übernimmt hier 1:1 dieselben
  Spaltenbreiten/-verhältnisse (Overview 2\*, Hints 1\*, live per Splitter
  verschiebbar, Verhältnis nicht persistiert, setzt bei jedem App-Start
  zurück) statt einer neuen `DashboardSubView`-Umschalt-Logik - kein Toggle,
  kein zusätzlicher `IsVisible`-Zustand, beide Bereiche sind immer gleichzeitig
  sichtbar.
- **Links: Overview**. Liste, eine Zeile pro `GroupViewModel`: Status-Punkt
  (bestehender `ConnectionStateToBrushConverter` wiederverwenden), Servername
  (`HeaderText`), ungelesene Events (Badge wie im Tab-Header, siehe
  `TabControl.ItemTemplate` in `MainWindow.axaml` als Vorlage), offene
  Hints. Klick auf eine Zeile: `SelectedGroup` setzen **und**
  `IsDashboardVisible = false` (zurück zur `TabControl`, direkt auf dem
  gewählten Tab).
- Der Fortschrittsanzeigen-Plan ist inzwischen umgesetzt (siehe
  `Feature-Plaene/Archiv/Fortschrittsanzeigen.md`) - `GroupViewModel.RoomProgressPercent`
  existiert bereits und ist genau für diesen Zweck stehen geblieben (siehe
  dessen Doc-Kommentar). In der Overview-Liste zusätzlich pro Zeile anzeigen -
  das Dashboard ist der naheliegende Ort für den dort bereits erwähnten
  "App-weiten Aggregat"-Wert (Summe über alle Gruppen hinweg). Die
  Tier-2-Multiworld-Anzeige (eine kombinierte 4-Segment-Bar pro Server, siehe
  Archiv-Plan) ist bewusst *nicht* Teil davon - eigene, unabhängige
  Datenquelle.
- **Rechts: Hints** (neu, siehe eigener Abschnitt unten): die geteilte,
  serverübergreifende Hints-Übersicht mit Server-/Slot-Filter.

## Geteilte Hints-Übersicht (Server- und Slot-Filter)

Nutzer-Wunsch: eine einzige Hints-Liste über *alle* Server hinweg, mit einer
zweistufigen Filterkaskade - erst nach Server, dann (nur für den gewählten
Server) nach Slot -, verankert direkt im Dashboard-Tab, **nebeneinander**
mit der Server-Übersicht (siehe oben) statt als umschaltbare Unteransicht -
kein eigener Toolbar-Toggle, kein `IsVisible`-Wechsel.

### Warum nebeneinander statt umschaltbar oder einer bloßen Erweiterung der Overview-Zeilen

Ursprünglich in diesem Plan als zweite, per Button umschaltbare Unteransicht
vorgesehen - Nutzer-Feedback: lieber wie die schon etablierte
Events|Hints/Items-Aufteilung pro Tab nebeneinander, nicht hinter einem
zusätzlichen Toggle versteckt. Passt auch inhaltlich besser: man will beim
Blick übers Dashboard oft genau beides gleichzeitig sehen (welcher Server
braucht Aufmerksamkeit, und was für Hints stehen konkret an), nicht erst
umschalten müssen. Die Overview-Zeile pro Server bleibt trotzdem eine reine
Zahl (`UnfoundHintCount`) - das genügt fürs "auf einen Blick"; die Hints-Liste
rechts liefert dazu die Details, ohne dass beides in eine Zeile gequetscht
werden müsste.

### Datengrundlage

Jedes `GroupViewModel` hat schon eine eigene, lokal gefilterte
`VisibleHints` (Found/Unfound, Rolle, Item-Kategorie, eigener Slot-Filter,
Suchtext - siehe `GroupViewModel.cs`). Die geteilte Übersicht bindet
**nicht** an diese bereits gefilterten Listen (sonst würde ein enger Filter
auf einem Tab, den der Nutzer vielleicht vergessen hat, in der geteilten
Übersicht stillschweigend Hints verschwinden lassen) und teilt sich auch
nicht deren Filter-State (ein in der Dashboard-Hints-Ansicht gewählter
Slot-Filter darf nicht nebenbei den Hints-Tab dieses Servers verändern,
und umgekehrt) - stattdessen liest sie roh aus jeder Gruppe `Hints` (die
ungefilterte Quelle) und wendet ihre eigenen, komplett unabhängigen
Filter-Properties an.

Da `HintEntry` nur eine `SlotId` trägt, aber keinen Verweis auf ihre
Gruppe, braucht jede Zeile der geteilten Liste einen kleinen Wrapper, der
beides zusammenhält - analog zu den schon vorhandenen kleinen UI-Hilfs-
Records für Dialoge (`StagedSlot`/`ConfiguredSlotRow`/`PlayerChoice`, siehe
CLAUDE.md "Models/"):

```csharp
// Models/DashboardHintRow.cs
public sealed record DashboardHintRow(GroupViewModel Group, HintEntry Hint);
```

### Zweistufiger Filter auf `DashboardViewModel`

```csharp
public ObservableCollection<GroupViewModel?> HintServerFilterOptions { get; } = new() { null };
[ObservableProperty] private GroupViewModel? _selectedHintServerFilter;

public ObservableCollection<SlotProfile?> HintSlotFilterOptions { get; } = new() { null };
[ObservableProperty] private SlotProfile? _selectedHintSlotFilter;

[ObservableProperty] private ItemCategoryFilter _selectedHintItemCategoryFilter = ItemCategoryFilter.All;
```

- `HintServerFilterOptions`: führender `null`-Eintrag = "All servers", danach
  `Groups` - exakt dasselbe "führender Null-Sentinel + Converter für die
  Anzeige"-Muster wie `GroupViewModel.SlotFilterOptions`
  (`Converters.SlotFilterDisplayConverter` als Vorlage für einen neuen,
  analogen Converter, der bei `null` "All servers" statt "All slots" zeigt
  und sonst `GroupViewModel.HeaderText`). Rebuild bei
  `Groups.CollectionChanged` (dieselbe Subscription, die
  `DashboardViewModel` für die Aggregations-Properties schon braucht).
- `HintSlotFilterOptions`: solange `SelectedHintServerFilter == null` ("All
  servers") bleibt sie auf `{ null }` beschränkt - eine flache
  Slot-Dropdown-Liste über mehrere Server hinweg wäre nicht eindeutig
  einordenbar (welcher Slot gehört zu welchem Server?). Sobald ein
  konkreter Server gewählt wird: Inhalt aus dessen eigener, schon
  gepflegter `GroupViewModel.SlotFilterOptions` übernehmen (kopieren, nicht
  dieselbe Collection-Instanz binden - sonst würde ein Insert/Remove dort
  beide ItemsSources gleichzeitig mutieren und ggf. doppelte Change-
  Events auslösen). `OnSelectedHintServerFilterChanged`: alte Subscription
  auf die vorher gewählte Gruppe's `SlotFilterOptions.CollectionChanged`
  abmelden, `HintSlotFilterOptions` neu befüllen, `SelectedHintSlotFilter`
  auf `null` zurücksetzen (kann nach einem Serverwechsel ohnehin nicht mehr
  zum selben Slot gehören), neue Subscription anmelden - dasselbe
  Ab-/Anmelde-Muster wie `RefreshSlotOrder` für einzelne Slots, nur einmal
  auf Gruppenebene angewendet.
- **Found/Unfound ist hier bewusst kein Filter, sondern fest verdrahtet**:
  die Dashboard-Hints-Ansicht zeigt grundsätzlich **nur nicht gefundene**
  Hints (`!Hint.Found`), ohne umschaltbaren Zustand und ohne eigene
  `HintFilter`-Property - anders als im Hints-Panel jedes einzelnen Tabs, wo
  "alle inkl. gefundener" als Rückblick Sinn ergibt. Für einen
  "auf-einen-Blick"-Überblick über offene Arbeit ist ein bereits gefundener
  Hint reines Rauschen, und der schmalen rechten Spalte fehlt ohnehin der
  Platz für einen weiteren Toggle neben dem Item-Kategorie-Filter unten.
- `SelectedHintItemCategoryFilter` wiederverwendet den schon vorhandenen
  `ItemCategoryFilter`-Enum-Typ (Progress/Useful/Normal/Trap/All) und das
  gleiche Single-Select-Filter-Toggle-Button-Muster wie
  `GroupViewModel.SelectedHintItemCategoryFilter`, aber als **eigene**
  Property, nicht dieselbe Instanz wie irgendein
  `GroupViewModel.SelectedHintItemCategoryFilter` - aus demselben
  Isolations-Grund wie beim Slot-Filter oben (ein in der
  Dashboard-Hints-Ansicht gewählter Kategorie-Filter darf nicht nebenbei
  den Hints-Tab eines Servers verändern, und umgekehrt). Der Rollen-Filter
  (`HintRoleFilter`, "ich finde"/"ich bekomme") ist hier bewusst **weiterhin
  nicht** Teil von V1 - lässt sich aber, falls später gewünscht, nach exakt
  demselben Muster ergänzen (siehe `GroupViewModel.VisibleHints`).

### `VisibleHints` (Dashboard)

```csharp
public IEnumerable<DashboardHintRow> VisibleHints
{
    get
    {
        var groups = SelectedHintServerFilter is null
            ? (IEnumerable<GroupViewModel>)Groups
            : new[] { SelectedHintServerFilter };

        var rows = groups.SelectMany(g => g.Hints.Select(h => new DashboardHintRow(g, h)))
                         .Where(r => !r.Hint.Found); // fest verdrahtet, kein Toggle - siehe oben.

        rows = SelectedHintItemCategoryFilter switch
        {
            ItemCategoryFilter.Progress => rows.Where(r => r.Hint.ItemKind == EventTextSegmentKind.ItemProgression),
            ItemCategoryFilter.Useful   => rows.Where(r => r.Hint.ItemKind == EventTextSegmentKind.ItemUseful),
            ItemCategoryFilter.Normal   => rows.Where(r => r.Hint.ItemKind == EventTextSegmentKind.ItemOther),
            ItemCategoryFilter.Trap     => rows.Where(r => r.Hint.ItemKind == EventTextSegmentKind.ItemTrap),
            _                           => rows,
        };

        if (SelectedHintSlotFilter is not null)
            rows = rows.Where(r => r.Hint.SlotId == SelectedHintSlotFilter.Id);

        return rows;
    }
}
```

### Reaktivität

Erweitert die im Abschnitt `DashboardViewModel` oben schon beschriebene
Subscription-Kette (`Groups.CollectionChanged` → pro Gruppe `PropertyChanged`
ab-/anmelden) um zwei weitere Ebenen, die schon für die Overview-Aggregate
nötig waren, jetzt zusätzlich für die Zeilenliste selbst gebraucht: pro
Gruppe zusätzlich `Hints.CollectionChanged` abonnieren (neuer Hint kommt
rein/verschwindet - dieselbe Reaktion wie `GroupViewModel.OnHintsCollectionChanged`,
nur eine Ebene höher), und pro `HintEntry` darin `PropertyChanged` auf
`Found` abonnieren - hier fällt der Hint dadurch komplett aus `VisibleHints`
heraus (kein Statuswechsel wie im Tab-Panel, siehe oben: die Ansicht zeigt
nur offene Hints) - gleiches Ab-/Anmelde-Muster wie
`GroupViewModel.OnHintEntryPropertyChanged`. Jede Änderung löst
`OnPropertyChanged(nameof(VisibleHints))` aus.

### UI (`DashboardView.axaml`, rechte Spalte)

- Zwei ComboBoxen nebeneinander über der Liste: Server-Filter
  (`HintServerFilterOptions`/`SelectedHintServerFilter`), danach Slot-Filter
  (`HintSlotFilterOptions`/`SelectedHintSlotFilter`) - Slot-ComboBox
  `IsEnabled="{Binding SelectedHintServerFilter, Converter=...IsNotNull}"`,
  damit optisch klar ist, dass sie erst nach einer Serverwahl greift (statt
  einfach still wirkungslos zu bleiben, wenn "All servers" gewählt ist).
  Darunter die vier Item-Kategorie-Filter-Buttons (All/Progression/Useful/
  Normal/Trap, `SelectedHintItemCategoryFilter`) - gleiche
  Single-Select-Toggle-Button-Gruppe wie im Hints-Panel jedes Tabs (siehe
  `ShowAllHintItemCategoriesCommand`/`ShowProgressHintItemsCommand`/... als
  Vorlage). **Kein** Found/Unfound-Toggle - die Ansicht zeigt immer nur
  offene Hints, siehe oben.
- Liste darunter, eine Zeile pro `DashboardHintRow`: **Server-Spalte**
  (`Group.HeaderText`, neu - im Tab-eigenen Hints-Panel implizit durch den
  Tab selbst gegeben, hier nicht mehr) davor, dann dieselben Spalten wie im
  bestehenden Hints-Panel (`ReceivingPlayerName`, `ItemName`,
  `LocationName`, `FindingPlayerName`) - ohne die Found-Spalte, die wäre
  hier immer "nein" und damit reine Platzverschwendung - gleiche
  `DataTemplate`-Bausteine wiederverwenden, kein neues Zeilen-Layout von
  Grund auf.
- Klick auf eine Zeile (oder eigens auf die Server-Spalte): `SelectedGroup`
  auf `Row.Group` setzen und `IsDashboardVisible = false` - identisches
  Navigations-Verhalten wie ein Klick auf eine Overview-Zeile.

## Tests

- **Kategorie A**: `DashboardViewModel`s Aggregations-Properties (mehrere
  `GroupViewModel`s mit `FakeConnectionManager` konstruieren, wie es
  `GroupViewModelOrderingTests` schon tut, `Events`/`Hints` synthetisch
  befüllen, Summen prüfen) sowie die Reaktivität bei Hinzufügen/Entfernen
  einer Gruppe.
- **Kategorie A** (neu, Hints-Übersicht): `VisibleHints` gegen mehrere
  `GroupViewModel`s mit synthetisch befüllten `Hints`-Collections (Mischung
  aus gefundenen und offenen Hints über verschiedene Item-Kategorien) -
  gefundene Hints tauchen **nie** in `VisibleHints` auf, unabhängig von
  jedem anderen Filter (kein `SelectedHintFilter` mehr vorhanden, der das
  umgehen könnte); `SelectedHintServerFilter == null` zeigt alle Gruppen
  gemischt, ein gewählter Server zeigt nur dessen Hints;
  `HintSlotFilterOptions` wird beim Serverwechsel korrekt neu befüllt (und
  bleibt bei "All servers" auf `{ null }`); `SelectedHintSlotFilter` wird
  beim Serverwechsel zurückgesetzt; `SelectedHintItemCategoryFilter`
  kombiniert korrekt mit Server- und Slot-Filter (z. B. "Progress" +
  konkreter Server zeigt nur offene Progression-Hints dieses Servers); ein
  neu ankommender Hint (`Hints.Add`) bzw. ein auf `Found` wechselnder
  bestehender Hint (verschwindet aus der Liste) aktualisiert `VisibleHints`,
  während eine bestimmte Serveransicht aktiv ist.
- **Kategorie C**: Toggle-Button schaltet zwischen `TabControl` und
  `DashboardView` sichtbar um; Klick auf eine Dashboard-Zeile (Overview,
  links) setzt `SelectedGroup` korrekt und blendet zurück zur `TabControl`;
  beide Spalten (Overview/Hints) sind nach einem echten Layout-Pass
  gleichzeitig sichtbar (kein versehentlich wieder eingeführter
  `IsVisible`-Umschalter); Klick auf eine Zeile in der rechten
  Hints-Spalte navigiert zum richtigen Server-Tab, auch wenn "All servers"
  gewählt war (Zeile trägt ihre eigene `Group` mit).
