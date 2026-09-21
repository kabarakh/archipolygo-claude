# Umsetzungsplan: Event-Log-Filter (Relevanz/Kategorie/Item-Art) fürs Dashboard übernehmen

## Status: ✅ Umgesetzt (2026-09-21)

Genau wie geplant umgesetzt, ohne Abweichungen - die beiden "Entschiedenen
Detailfragen" (Item-Art-Checkboxen mitnehmen; Platzierung oberhalb der
Server/Slot-Zeile) wurden vor der Umsetzung mit dem Entwickler geklärt und
danach 1:1 übernommen.

- `ViewModels/DashboardViewModel.cs`: `SelectedEventRelevanceFilter`/
  `SelectedEventCategoryFilter`/die vier `Show...ItemEvents`-Bools plus
  Commands, exakt wie unten skizziert; `VisibleEvents` um dieselbe
  Filter-Kette erweitert wie `GroupViewModel.VisibleEvents`.
- `Views/DashboardView.axaml`: neue `WrapPanel`-Zeilen (Relevanz+Kategorie,
  Item-Art-Checkboxen) oberhalb der bestehenden Server/Slot-`ComboBox`-Zeile
  im Events-Panel; `Grid.RowDefinitions` von `Auto,*,Auto,Auto` auf
  `Auto,Auto,Auto,*,Auto,Auto` erweitert, restliche Zeilen entsprechend
  verschoben (Send-Zeilen jetzt `Grid.Row="4"`/`"5"`).
- Tests: 8 neue Kategorie-A-Tests (`DashboardViewModelTests.cs`, inkl. eines
  expliziten Isolations-Tests in beide Richtungen) und 3 neue Kategorie-C-Tests
  (`DashboardTabTests.cs`, echte Button-/Checkbox-Klicks gegen die reale
  `DashboardView`) - genau wie im Tests-Abschnitt unten vorgesehen. Gesamte
  Suite (295 Tests) grün, Solution baut sauber.

---

Entstanden aus der Frage, ob die Filter-Buttons des Tab-eigenen Events-Logs
1:1 auch fürs Dashboard-Events-Panel übernommen werden können - mit
komplett eigenem, vom jeweiligen Tab unabhängigem Filterzustand (gleiche
Buttons, keine gegenseitige Beeinflussung).

## Ausgangslage

Ein Tab-eigenes Events-Log (`GroupViewModel.VisibleEvents`,
`ViewModels/GroupViewModel.cs:268-296`) filtert über drei unabhängige,
nicht gegenseitig exklusive Achsen:

1. **Relevanz** (`EventRelevanceFilter`: `All`/`ConcernsMe`) - Property
   `SelectedEventRelevanceFilter`, Commands `ShowAllEventsRelevance`/
   `ShowOwnEventsOnly`.
2. **Kategorie** (`EventCategoryFilter`: `All`/`Hints`/`Items`/`Chat`) -
   Property `SelectedEventCategoryFilter`, Commands
   `ShowAllEventCategories`/`ShowHintEventsOnly`/`ShowItemEventsOnly`/
   `ShowChatEventsOnly`.
3. **Item-Art** (unabhängige Checkboxen, nicht Teil eines Enums):
   `ShowProgressionItemEvents`/`ShowUsefulItemEvents`/
   `ShowFillerItemEvents`/`ShowTrapItemEvents` - Einträge ohne Item-Kategorie
   (Connect/Disconnect/Chat/Fehler) bleiben davon immer unberührt.

Alle drei sind in `Views/MainWindow.axaml:415-442` als `WrapPanel` mit dem
gemeinsamen, global registrierten `Button.filter-toggle`-Style
(`MainWindow.axaml:55-75`, `Window.Styles`, app-weit gültig) plus einer
Checkbox-Reihe umgesetzt.

Das Dashboard hat bereits ein eigenes, server-übergreifendes Events-Panel
(`DashboardViewModel.VisibleEvents`, `ViewModels/DashboardViewModel.cs:329-347`,
Anzeige in `Views/DashboardView.axaml:396-452`), das alle Gruppen mergt
(`groups.SelectMany(g => g.Events...)`, bewusst die *ungefilterten* rohen
`Events` jeder Gruppe, nicht deren je eigene `VisibleEvents` - siehe
Doc-Kommentare dort). Es hat aber **nur** den bereits vorhandenen
`EventsFilter` (`DashboardServerSlotFilter`, Server→Slot-Kaskade) - keine
Relevanz-/Kategorie-Toggle-Buttons, keine Item-Art-Checkboxen. Das
Hints-Panel des Dashboards hat zum Vergleich bereits eine eigene
`SelectedHintItemCategoryFilter` (`ItemCategoryFilter`) neben seinem
eigenen `HintFilter` - das Isolationsprinzip (jedes Panel/jeder Tab trägt
seinen eigenen Filterzustand, nie eine geteilte Instanz) ist im Dashboard
also bereits etablierte Praxis, siehe auch
[`Dashboard-Tab.md`](Dashboard-Tab.md)s "Warum nebeneinander"-Abschnitt
zum Server+Slot-Filter.

`EventEntry.ConcernsOwnSlot` (`Models/EventEntry.cs:62`) wird pro Eintrag
schon bei dessen Erzeugung gesetzt (`HintService.cs:100`,
`MessageHistoryService.cs:68/136`) - der Wert ist also unabhängig vom
jeweils anzeigenden Tab/Panel und überträgt sich unverändert auf die
Dashboard-Ansicht; keine Neuberechnung nötig.

## Ansatz

1. **Neue, eigenständige Filter-Properties auf `DashboardViewModel`** -
   keine Referenz auf ein `GroupViewModel`, sondern eigene Instanzen:
   - `SelectedEventRelevanceFilter` (`EventRelevanceFilter`, wiederverwendeter
     Enum-Typ)
   - `SelectedEventCategoryFilter` (`EventCategoryFilter`, wiederverwendeter
     Enum-Typ)
   - `ShowProgressionItemEvents`/`ShowUsefulItemEvents`/
     `ShowFillerItemEvents`/`ShowTrapItemEvents` (vier eigene bool-Properties,
     Default `true` wie im Tab)

   Jeweils als `[ObservableProperty]` mit `partial void OnXChanged(...)`,
   die `OnPropertyChanged(nameof(VisibleEvents))` auslösen - analog zu
   `GroupViewModel.cs:917-919`.
2. **Commands** analog zu `GroupViewModel`s `ShowAllEventsRelevance` etc.
   (`[RelayCommand]`, `GroupViewModel.cs:1183-1198`) - eigene, gleich
   benannte Methoden auf `DashboardViewModel`.
3. **`VisibleEvents`-Getter erweitern**
   (`DashboardViewModel.cs:329-347`): dieselbe Filter-Kette wie
   `GroupViewModel.VisibleEvents` (Relevanz → Kategorie → Item-Art) vor/nach
   der bestehenden `EventsFilter`-Narrowing einfügen.
4. **XAML**: in `DashboardView.axaml`s Events-Panel
   (`Grid Grid.Row="2"`, ab Zeile 403) eine zusätzliche Zeile mit derselben
   `WrapPanel`+`Button.filter-toggle`-Struktur wie
   `MainWindow.axaml:416-430` (Relevanz- und Kategorie-Buttons) sowie eine
   Checkbox-Zeile wie `MainWindow.axaml:437-442` einfügen - Style/Converter
   (`EnumEqualsConverter`) sind bereits global registriert, keine
   Neu-Definition nötig. Platzierung: oberhalb der bestehenden
   Server/Slot-`ComboBox`-Zeile, damit die Reihenfolge "Relevanz → Kategorie
   → Item-Art → Server/Slot → Liste" für beide Ansichten (Tab und
   Dashboard) gleich liest.

## Was bewusst gleich bleibt / isoliert bleibt

- `GroupViewModel` und seine Filter-Properties werden nicht angefasst -
  keine gemeinsame Instanz, kein gemeinsamer Zustand mit dem Dashboard.
- Die Dashboard-Filter sind reine Anzeigefilter, genau wie die bestehenden
  (`EventsFilter`, `HintFilter`) - keine Nebenwirkung auf
  Leader/Connection-Zustand.
- Kein Persistieren des Dashboard-Filterzustands über einen App-Neustart
  hinweg - die Tab-eigenen Filter werden aktuell ebenfalls nicht
  persistiert (reiner In-Memory-UI-Zustand), also bleibt das Verhalten
  konsistent.

## Entschiedene Detailfragen

- **Item-Art-Checkboxen:** werden mitgenommen (alle drei Filter-Achsen wie im
  Tab-Log, nicht nur die beiden Toggle-Button-Reihen) - Konsistenz zwischen
  Tab- und Dashboard-Ansicht war wichtiger als die kleinere UI-Änderung.
- **Platzierung:** oberhalb der bestehenden Server/Slot-Dropdown-Zeile -
  Reihenfolge Relevanz → Kategorie → Item-Art → Server/Slot → Liste,
  identisch zur Lesereihenfolge im Tab-Log.

## Tests

- **Kategorie A** (`[Fact]` oder `[AvaloniaFact]` ohne echtes Fenster,
  analog zu bestehenden `DashboardViewModel`-Tests): jede der drei
  Filter-Achsen einzeln und in Kombination gegen `VisibleEvents` prüfen -
  insbesondere dass ein Wechsel eines Dashboard-Filters die `VisibleEvents`
  eines `GroupViewModel`-Tabs unverändert lässt (und umgekehrt), um die
  Isolation explizit abzusichern.
- **Kategorie C** (`[AvaloniaFact]`, echte `DashboardView`): Button-Klicks
  lösen die erwarteten Commands aus, `Classes.active`-Bindings zeigen den
  richtigen Button als aktiv - Muster wie in den bestehenden
  `DashboardTab`-Tests zum Server/Slot-Filter.
