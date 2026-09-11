# Umsetzungsplan: Summary/Dashboard-Tab

## Status: ✅ Umgesetzt (2026-09-11)

Umgesetzt in drei Schritten (Aggregate → geteilte Hints-Übersicht → UI),
jeweils mit eigenen Tests (Kategorie A für `DashboardViewModel`, Kategorie C
gegen das echte `DashboardView.axaml`) - siehe `DashboardViewModelTests.cs`
und `DashboardTabTests.cs`. Ein Punkt wich bewusst vom unten stehenden
Plantext ab, nach Rückfrage beim Entwickler:

- **Fortschrittsbalken pro Server-Zeile in der Overview**: der Plantext
  weiter unten sagt explizit, die Tier-2-4-Segment-Bar sei "bewusst nicht
  Teil davon" und nur `RoomProgressPercent` solle erscheinen - das
  Mockup-Bild zeigt aber pro Zeile den vollen 4-Segment-Balken (own/other
  done/open) inklusive Legende, identisch zum bestehenden Tab-eigenen
  kombinierten Balken. Entscheidung: **das Mockup-Bild gewinnt** - jede
  Overview-Zeile zeigt denselben 4-Segment-Balken samt Tooltip-Legende wie
  der Tab selbst (siehe `DashboardView.axaml`), plus zusätzlich
  `RoomProgressPercent` als Prozentzahl daneben. Der Rest des Plans
  (Struktur, geteilte Hints-Übersicht, Filter-Kaskade, Navigation) wurde wie
  unten beschrieben umgesetzt.

Neue Dateien: `ViewModels/DashboardViewModel.cs`, `Views/DashboardView.axaml(.cs)`,
`Models/DashboardHintRow.cs`, `Converters/DashboardServerFilterDisplayConverter.cs`.
Geänderte Dateien: `ViewModels/MainWindowViewModel.cs` (`IsDashboardVisible`,
`Dashboard`, `ToggleDashboardCommand`), `Views/MainWindow.axaml` (Toolbar-
Toggle-Button, `TabControl`/`DashboardView` im selben Grid-Bereich).

### Nachbesserungen (2026-09-11, nach erstem Review durch den Entwickler)

- **Overview-Zeilen nutzen jetzt die volle Spaltenbreite**: `ListBox`-Items
  maßen sich ursprünglich nur an ihrem eigenen Inhalt (Name+Badges), der
  4-Segment-Balken blieb dadurch auf feste 120px begrenzt, obwohl die
  2\*-Spalte oft deutlich breiter ist. Fix: `HorizontalContentAlignment=
  Stretch` auf `ListBoxItem` (per Style, da `ListBox` selbst diese Property
  in der verwendeten Avalonia-Version nicht direkt exponiert) plus
  `Grid ColumnDefinitions="*,Auto"` statt `StackPanel` für die Balken-Zeile,
  damit der Balken den frei gewordenen Platz tatsächlich beansprucht.
- **Toolbar**: Dashboard/Back-to-tabs-Button steht jetzt zuerst, per
  `Border`-Trennlinie vom Rest abgesetzt (Moduswechsel fürs ganze Fenster,
  nicht eine weitere Pro-Server-Aktion). "Add slot..."/"Edit server..."/
  "Remove server" (alle drei wirken auf `SelectedGroup`) sind ausgeblendet,
  solange das Dashboard sichtbar ist - dort ist nicht erkennbar, welcher
  Server gemeint wäre. "Add server..."/"Disconnect all" hängen nicht von
  `SelectedGroup` ab und bleiben sichtbar.
- **Pro-Zeile-Icons im Dashboard**: Connect/Disconnect, Add slot, Edit
  server, Remove server jetzt zusätzlich als kompakte Icon-Buttons direkt in
  jeder Overview-Zeile (rechts neben Name/Badges), mit Tooltip. Technische
  Entscheidung nach Rückfrage: **inline Avalonia `Path`/`Geometry`**, keine
  echten `.svg`-Dateien (kein `Avalonia.Svg.Skia`-Paket nötig) und keine
  generierten PNGs (auf dieser Maschine stand keine SVG→PNG-Toolchain zur
  Verfügung). Connect/Disconnect binden direkt an
  `GroupViewModel.ConnectCommand`/`DisconnectCommand` (identisch zum
  Tab-eigenen Button-Paar); Add slot/Edit server/Remove server lösen
  stattdessen ein `DashboardView`-Event aus (`AddSlotRequested`/
  `EditServerRequested`/`RemoveServerRequested`), da das Öffnen ihrer Dialoge
  ein `Window` und `MainWindowViewModel` braucht, worauf `DashboardView`
  selbst keinen Zugriff hat - `MainWindow.axaml.cs` abonniert diese Events
  und ruft dieselbe (jetzt parametrisierte statt fest auf `SelectedGroup`
  bezogene) Dialog-Logik wie die Toolbar-Buttons.
  `MainWindowViewModel.RemoveSelectedGroupAsync` wurde dafür in eine
  öffentliche `RemoveGroupAsync(GroupViewModel)` (ändert `SelectedGroup` nur,
  wenn die entfernte Gruppe tatsächlich die ausgewählte war) plus einen
  dünnen, weiterhin `SelectedGroup`-basierten Command aufgeteilt. "Add slot"
  ist dabei nur **ausgegraut**, nicht versteckt, solange der Server getrennt
  ist (`IsLeaderConnected`) - das Konzept ergibt auch ohne Live-Verbindung
  Sinn, es kann nur ohne eine nicht ausgeführt werden. Ein Icon-Klick
  navigiert nicht zusätzlich vom Dashboard weg (verifiziert, nicht
  angenommen - siehe `DashboardTabTests.RowIconClick_DoesNotAlsoNavigateAwayFromTheDashboard`).
- **Hints-Spalte**: Hinweistext von "open only, no found/unfound toggle" zu
  "unfound only" gekürzt und leicht vergrößert (11px → 12px).
- **Icon-Hover**: war ein halbtransparentes Schwarz (`#22000000`, ~13%) -
  dunkelte ab statt aufzuhellen, wirkte "sehr dezent". Jetzt ein
  halbtransparentes DodgerBlue (`#4D1E90FF`, ~30%, dieselbe Akzentfarbe wie
  die aktiven Filter-Buttons im Rest der App) plus `CornerRadius="4"`.
- **`IsDashboardVisible` startet jetzt auf `true`** statt `false` (Dev-Wunsch
  2026-09-11): die App öffnet direkt im Dashboard statt im ersten Server-Tab.
  Das legte eine latente Testschwäche offen - mehrere Kategorie-C-Tests
  gingen implizit davon aus, dass `TabControl`'s Content beim Fenster-Start
  bereits materialisiert ist (siehe CLAUDE.mds "TabControl.ContentTemplate
  materialisiert lazy"-Hinweis: das gilt jetzt nicht mehr nur pro Tab,
  sondern für die ganze `TabControl`, solange das Dashboard aktiv ist). Alle
  betroffenen Tests (`HorizontalStackPanelAlignmentTests`,
  `EventsListAutoScrollTests`, `ChatSlotComboBoxTests`,
  `MainWindowProgressTests`, `SlotDropdownWidthTests`) schalten jetzt vor
  dem eigentlichen Test explizit auf die Tab-Ansicht um. Zusätzlich musste
  `MainWindowProgressTests.FindCombinedBar` auf die `TabControl` eingegrenzt
  werden - der 4-Segment-Balken existiert jetzt strukturell identisch auch
  pro Dashboard-Zeile (gleicher `GroupViewModel`-DataContext, gleiche
  4-Spalten-Form), eine ungezielte Suche über das ganze Fenster fand sonst
  je nach Sichtbarkeitszustand mal den falschen, mal ambigen Treffer.

### Follow-up: geteilte Event-Ansicht (2026-09-11)

Nach Diskussion (Freund-Idee: gemeinsame Eventanzeige mit Textfeld, um an
einen wählbaren Server/Slot zu senden, ohne zur Tab-Ansicht wechseln zu
müssen) umgesetzt als **zweites Panel in der linken Spalte**, per
Button+`IsVisible`-Toggle zwischen "Overview" und "Events" - **keine echte
verschachtelte `TabControl`**, aus demselben Grund wie oben (lazy
`ContentTemplate`-Materialisierung). Die Hints-Spalte rechts bleibt davon
komplett unberührt, immer sichtbar, wie gewünscht.

Wichtige Entscheidungen aus der Diskussion:
- **Priorität: Lesen vor Senden.** Kein Ein-Zeiler-Quick-Send aus der
  Overview-Zeile - der geteilte Event-Log ist der Zweck, das Senden hängt
  nur unten dran.
- **Alle Event-Typen**, nicht nur Chat (`GroupViewModel.Events` roh, wie bei
  `VisibleHints`) - "eher als geteilte Event-Ansicht zu sehen als als Chat".
- **Zwei Dropdowns oben (Filter) + zwei Dropdowns unten (Senden), komplett
  getrennt** (Revision 2026-09-12, Entwickler-Feedback: "die Dropdowns oben
  als Filter sind eine gute Idee, aber die Dropdowns für die Auswahl des
  Chatters sollten unten bei der Texteingabe sein und losgelöst von der
  Filterung"). Ursprünglich war der obere Slot-Dropdown mit dem Senden
  überladen (Filter *und* Leader-Wechsel in einem Control) - jetzt sauber
  getrennt:
  - **Oben** (`SelectedEventsServerFilter`/`EventsServerFilterOptions` +
    NEU `SelectedEventsSlotFilter`/`EventsSlotFilterOptions`): reiner
    Anzeige-Filter, exakt dieselbe Zwei-Stufen-Kaskade wie bei Hints
    (Copy-not-share der `SlotFilterOptions`, kein Leader-Wechsel, ein
    room-weiter Event ohne `SlotId` passiert den Slot-Filter immer).
  - **Unten** (NEU `SelectedSendServerGroup`, keine "All servers"-Option,
    `ItemsSource` direkt an `Groups`): "Send as"-Zeile bei der
    Texteingabe. Der zugehörige Slot-Dropdown bindet weiterhin per nested
    Binding direkt an `SelectedSendServerGroup.Slots`/`.SelectedChatSlot` -
    dieselbe Live-Instanz wie das Tab-eigene "Chat as"-Dropdown, echter
    Leader-Wechsel beim Auswählen.
  - Beide Auswahl-Paare sind komplett unabhängig - Filter ändern lässt den
    Sende-Server unberührt und umgekehrt.
- **Dropdown-Layout**: `Grid ColumnDefinitions="*,*"` ließ die zwei
  Dropdowns (sowohl bei Hints als auch bei Events) sehr weit auseinander
  wirken, da jede Box auf 50% der Spalte gestreckt wurde (Entwickler-
  Feedback: "sehr weit auseinander"). Fix: `StackPanel Orientation=
  "Horizontal" Spacing="8"` mit fester `Width="130"` pro ComboBox, exakt wie
  die Slot-Filter-Dropdowns in den Server-Tabs selbst - für beide
  Filterzeilen (Hints und Events).
- **Priorität: Lesen vor Senden.** Kein Ein-Zeiler-Quick-Send aus der
  Overview-Zeile - der geteilte Event-Log ist der Zweck, das Senden hängt
  nur unten dran.
- **Alle Event-Typen**, nicht nur Chat (`GroupViewModel.Events` roh, wie bei
  `VisibleHints`) - "eher als geteilte Event-Ansicht zu sehen als als Chat".
- Overview startet weiterhin als Default-Panel.

Neue Dateien: `Models/DashboardEventRow.cs`, `Models/DashboardLeftPanel.cs`.
`DashboardViewModel` neu: `SelectedLeftPanel`/`ShowOverviewPanelCommand`/
`ShowEventsPanelCommand`, `EventsServerFilterOptions`/`SelectedEventsServerFilter`,
`EventsSlotFilterOptions`/`SelectedEventsSlotFilter`, `SelectedSendServerGroup`,
`VisibleEvents`, `CanSendMessage`.

**Stolperstein**: `IsEnabled="{Binding SelectedSendServerGroup.IsLeaderConnected}"`
(nested Binding durch ein nullable `GroupViewModel?`) fällt bei null-Zwischenwert
NICHT zuverlässig auf `false` zurück - der Send-Button blieb aktiv, obwohl kein
Server gewählt war (aufgedeckt durch
`DashboardTabTests.SendRow_SlotAndSendControls_DisabledUntilASendServerIsPicked`,
damals noch unter altem Namen). Gefixt mit einer echten, eigens
benachrichtigten `CanSendMessage`-Property auf `DashboardViewModel` statt der
verschachtelten Bindung - Lehre für zukünftige nested Bindings durch
nullable Objekt-Properties auf einen bool.

Tests: `DashboardViewModelTests.cs` (Kategorie A, 14 neue Tests: `VisibleEvents`-
Aggregation/Server-/Slot-Filter-Kombination/Reaktivität,
`EventsServerFilterOptions`/`EventsSlotFilterOptions`-Rebuild/Reset,
`CanSendMessage`, Beleg dass der Filter-Slot-Dropdown nie den Leader
anfasst), `DashboardTabTests.cs` (Kategorie C, 9 neue Tests: Panel-Toggle
inkl. "Hints bleibt immer sichtbar", Filter-Slot-Dropdown filtert ohne
Leader-Wechsel, Send-Slot-Dropdown wechselt echt den Leader, Filter und
Sende-Server sind unabhängig voneinander, Send/Slot-Controls deaktiviert
ohne gewählten Sende-Server, Send erreicht die richtige Gruppe,
Event-Anzeige zeigt die richtigen Einträge).


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

### Refactoring: Wiederverwendung statt Neubau (2026-09-12)

Nach expliziter Nachfrage des Entwicklers durchgesehen, wo neue statt
wiederverwendeter Komponenten entstanden waren, und behoben:

- **`ViewModels/DashboardServerSlotFilter.cs`** (neu): die Server→Slot-
  Filterkaskade (`RebuildServerOptions`/`OnSelectedServerChanged`/
  `OnSelectedServerSlotFilterOptionsChanged`/`RebuildSlotOptions`) war für
  Hints und Events zweimal fast identisch implementiert, nur mit anderen
  Property-Namen. Jetzt eine `ObservableObject`-Klasse, zweimal instanziiert
  (`DashboardViewModel.HintFilter`/`EventsFilter`). XAML-Bindungen entsprechend
  auf `HintFilter.ServerOptions`/`.SelectedServer`/`.SlotOptions`/`.SelectedSlot`
  bzw. `EventsFilter.*` umgestellt (vorher `HintServerFilterOptions`/
  `SelectedHintServerFilter`/... als flache Properties direkt auf
  `DashboardViewModel`).
- **`Views/ClipboardCopyHelper.cs`** (neu): die Ctrl/Cmd+C-"Kopiere
  ausgewählte Zeilen"-Logik (Selektion → Zeilentext → Zwischenablage) war in
  `MainWindow.axaml.cs` (Events/Hints) und `DashboardView.axaml.cs` (Events)
  dreimal fast identisch. Jetzt eine statische Hilfsklasse; nur "was ist der
  Kopier-Shortcut" (trivial) und "welcher Text pro Zeile" (pro Liste
  unterschiedlich, bleibt es auch) sind noch Aufrufer-eigen.
- **`Views/SharedTemplates.axaml`** (neu, in `App.axaml` global gemerged):
  das Rendering eines einzelnen `EventTextSegment` (farbiger `TextBlock` je
  nach `Kind`) existierte identisch in `MainWindow.axaml`s Tab-eigener
  Events-Liste und `DashboardView.axaml`s geteilter Events-Liste - jetzt ein
  gemeinsames `DataTemplate x:Key="EventTextSegmentTemplate"` samt dem
  zugehörigen `SegmentKindToBrushConverter`, per `ItemTemplate="{StaticResource
  EventTextSegmentTemplate}"` in beiden referenziert statt inline dupliziert.

**Bewusst nicht vereinheitlicht**: `DashboardHintRow`/`DashboardEventRow`
bleiben zwei separate Ein-Zeilen-Records (`Group` + eine Entry), kein
gemeinsamer generischer `DashboardRow<T>`-Wrapper. Ein generischer Typ als
`x:DataType` in einem Avalonia-`DataTemplate` ist fragil/unüblich (kein
sauberes XAML-Markup für geschlossene generische Typen) - das Risiko stand
in keinem Verhältnis zum Nutzen bei zwei Ein-Zeilen-Records. Ebenso blieb
die Events-Zeilen-Darstellung selbst (Dashboard zeigt zusätzlich den
Servernamen, der Tab zeigt zusätzlich "NEW"-Badge/Event-Typ) je eigenes
Layout - nur der gemeinsame Kern (die Segment-Liste) wurde geteilt.

Kein Verhaltensunterschied durch dieses Refactoring - alle 240 Tests liefen
vorher wie nachher unverändert durch (Test-Dateien wurden nur mechanisch auf
die neuen Property-Pfade umgestellt, keine neue Testlogik).
