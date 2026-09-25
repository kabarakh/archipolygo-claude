# Umsetzungsplan: Kompakteres Layout (mehr Platz für Events)

## Status: ✅ Umgesetzt (2026-09-25)

Alle vier Teile (A-D) umgesetzt, auf ausdrücklichen Wunsch direkt in
`AvaloniaApplication1` ohne TestHarness-Prototyp. Build und Testsuite
(354/354) grün, **visuelle Prüfung am echten Fenster steht noch aus** -
insbesondere, ob die Events-Filterzeile bei realer Fensterbreite wirklich
in eine Zeile passt (Headless-Tests können das mit ihrer breiten
Testschrift nicht beurteilen) und wie Chevron/Zusammenfassung wirken.
Abweichungen vom Plan unten, pro Teil:

- **Teil A: ✅ umgesetzt (2026-09-25)**, Tests grün (`DensityTests`), visuelle
  Prüfung am echten Fenster steht noch aus. Abweichungen vom Plan unten:
  - Im Settings-Dialog eine **Checkbox** "Compact layout (denser lists and
    controls)" statt ComboBox Compact/Comfortable - bei nur zwei Werten
    einfacher. `AppSettings.UiDensity` bleibt trotzdem ein String.
  - Angewendet beim Speichern in `MainWindowViewModel.SaveSettings` (nicht im
    Dialog-Code-behind), analog zu `CycleTheme`.
  - Nebenbei gefundener und behobener Bug: `SettingsViewModel.TryBuildSettings`
    baute ein frisches `AppSettings` ohne `ThemePreference` - jedes Speichern
    des Settings-Dialogs setzte das Theme auf der Platte auf "System" zurück.
    Das VM reicht nicht selbst bearbeitete Felder jetzt unverändert durch -
    Seit Teil D generisch: das VM startet von `AppSettings.Clone()` der
    ursprünglichen Einstellungen und überschreibt nur seine eigenen Felder.
  - Overview-Zeilen im Dashboard bewusst unverändert (Entscheidung 2).
- **Teil B + C: ✅ umgesetzt (2026-09-25)**, Tests grün
  (`CompactFilterRowTests`, angepasste `CopyFromHereTests`/`DashboardTabTests`),
  visuelle Prüfung steht noch aus. Abweichungen:
  - Dashboard: der Overview/Events-Umschalter teilt sich die Zeile **mit**
    den Events-Filtern (die nur bei aktivem Events-Panel sichtbar sind),
    statt eine eigene Zeile zu behalten - spart eine weitere Zeile.
  - "Unfound only" (Dashboard-Hints) als Tooltip auf der **Filterzeile**,
    nicht auf der Liste - ein Tooltip auf der Liste hätte ständig über den
    klickbaren Hint-Zeilen aufgepoppt.
  - Die `filter-toggle`-Styles zogen von `MainWindow.axaml` nach
    `Styles/FilterStyles.axaml` (app-weit in `App.axaml`). Nebenbei behoben:
    im `DetachedGroupWindow` waren aktive Filter-Buttons bisher gar nicht
    hervorgehoben. Neu dort: `WrapPanel.filter-row` (Abstand für ComboBoxen)
    und `Border.filter-separator`.
  - `IEventItemClassFilter` (von `GroupViewModel` und `DashboardViewModel`
    implementiert) ist das gemeinsame `x:DataType` für
    `Views/ItemClassFilterButton`.
  - Die Headless-Testschrift ist deutlich breiter als echte Schriften - der
    "passt in eine Zeile"-Test prüft deshalb bei 2400 px Breite nur die
    Struktur, nicht die reale Breite.
- **Teil D: ✅ umgesetzt (2026-09-25)**, Tests grün (`CollapsibleFilterBarTests`).
  Abweichungen:
  - Im Server-Tab klappt die Hints/Items-Leiste **inklusive** Slot-Filter und
    Suchfeld ein (alles, was die Liste filtert), nicht nur die Button-Zeile.
  - Dashboard-Events: Overview/Events-Umschalter bleibt außerhalb der
    Leiste (Navigation), die Leiste daneben ist nur bei aktivem Events-Panel
    sichtbar. Der "Unfound only"-Tooltip sitzt jetzt auf der ganzen
    Hints-Leiste.
  - Zusammenfassung zeigt "Filters" (gedimmt) bzw. "Filters: …" (blau/fett
    wie ein aktiver Filter-Button); Klick auf die Zusammenfassung klappt auf.
  - Die Zusammenfassungstexte hängen sich an die ohnehin gefeuerten
    `VisibleEvents`/`VisibleHints`/`VisibleReceivedItems`-Notifications
    (`OnPropertyChanged`-Override in beiden VMs), statt jeden einzelnen
    Filter-Changed-Hook zu erweitern.
  - Offene Entscheidung 6 (Default beim ersten Start) wie empfohlen:
    aufgeklappt.

## Ausgangslage (2026-09-25)

Dev-Feedback mit Screenshots: auf einem ~670 px hohen Fenster beginnt die
Events-Liste im Server-Tab erst bei y≈355 - darüber stapeln sich Toolbar,
Tab-Leiste, Verbindungszeile, Fortschrittsbalken, die Überschrift "Events",
zwei Filterzeilen (Relevanz/Kategorie-Buttons, Item-Klassen-Checkboxen) und
die "Show:"-Zeile (Slot-Filter + "Copy from here"). Sichtbar bleiben ~8
Events. Das Dashboard hat dasselbe Muster (Summary-Zeile, "Events · all
servers…"-Überschrift, Overview/Events-Umschalter, zwei Filterzeilen,
Server/Slot-Zeile). Das Hints-Panel rechts hat sogar drei Filterzeilen plus
Slot/Suche und bricht bei normaler Breite schon um ("My item (5)" rutscht in
eine eigene Zeile).

Zusätzlich sind die Listenzeilen selbst hoch: Fluents Standard-
`ListBoxItemPadding` ist `12,9,12,12` - eine einzeilige Event-Zeile ist
dadurch ~30 px hoch, obwohl der Text ~16 px braucht.

Aus einer Ideenliste von sieben Vorschlägen wurden **vier** ausgewählt
(Nummerierung aus der Diskussion in Klammern):

- **A** (1) Kompakte Dichte
- **B** (2) Überschrift "Events" entfernen
- **C** (4) Filter in eine Zeile
- **D** (5) Filterleiste einklappbar

Bewusst **nicht** Teil dieses Plans: Verbindungszeile + Fortschrittsbalken
zusammenlegen (3), seltene Server-Aktionen ins Tab-Kontextmenü (6), eigene
Titelleiste (7).

---

## Teil A: Kompakte Dichte

### Ansatz

Avalonia 12.0.4s `FluentTheme` hat eine `DensityStyle`-Property (`Normal`/
`Compact`) - verifiziert gegen den Quellcode am Tag `12.0.4`
(`src/Avalonia.Themes.Fluent/FluentTheme.xaml.cs`, `DensityStyles/Compact.xaml`),
nicht gegen die DLL. Es ist eine normale Direct-Property mit Setter, also zur
Laufzeit umschaltbar. `Compact.xaml` setzt unter anderem:

| Ressource | Compact |
|---|---|
| `ListBoxItemPadding` | `4,2` (Normal: `12,9,12,12`) - **der größte Hebel** |
| `ButtonPadding` | `6,4` |
| `ComboBoxMinHeight` / `CheckBoxMinHeight` / `TextControlThemeMinHeight` | `24` |
| `ComboBoxPadding` | `12,1,0,3` |
| `TextControlThemePadding` | `4,2` |

Allein die Listenpadding-Änderung macht jede Event-/Hint-Zeile ~17 px
niedriger - grob doppelt so viele sichtbare einzeilige Events.

### Umsetzung

- `AppSettings.UiDensity` (string, `"Compact"`/`"Normal"`, gleiches
  "string statt enum"-Muster wie `ThemePreference`, siehe dessen
  Doc-Kommentar). Default: `"Compact"` (Entscheidung 1).
- `Services/DensityService.cs` (statisch, analog `ThemeService`):
  `Apply(string)` sucht die `FluentTheme`-Instanz in
  `Application.Current.Styles` und setzt `DensityStyle`. Aufruf beim Start
  an derselben Stelle wie `ThemeService.Apply` (vor dem Hauptfenster, kein
  sichtbares Umspringen) und beim Speichern des Settings-Dialogs.
- `SettingsWindow.axaml`: neue Zeile "Layout density" mit ComboBox
  `Compact`/`Comfortable` (Anzeigetext "Comfortable" für `"Normal"`).
- **Eigene, lokal gesetzte Abstände** greift `DensityStyle` nicht - diese
  Stellen prüfen und bei Bedarf fest verkleinern (nicht density-abhängig,
  um keinen zweiten Mechanismus einzuführen):
  - `DashboardView.axaml`: `OverviewListBox` überschreibt `Padding="0"` und
    hat einen exakt auf 63 px ausgemessenen Innenabstand `8,11,8,14` (siehe
    dessen langer Kommentar). Bleibt in Compact unverändert hoch. Entweder
    so lassen (Overview ist keine lange Liste) oder bei Compact auf einen
    kleineren Wert gehen - dann per `DynamicResource` statt Literal, siehe
    offene Entscheidung 2.
  - Event-/Hint-Item-Templates: `Margin="0,2"` auf den StackPanels - okay,
    klein.
  - Root-Grid `Margin="16"` in `GroupDetailView`/`DashboardView` → `12`
    (unabhängig von der Dichte, kostet wenig).
  - `Button.filter-toggle`/`CheckBox` Margin `0,0,8,4` in
    `MainWindow.axaml`'s `Window.Styles` → `0,0,6,4`.
- `DetachedGroupWindow` erbt das automatisch (gleiche Application-Styles).

### Tests

- Logik: `DensityService.Apply` setzt `DensityStyle` auf der `FluentTheme`
  (Headless, `[AvaloniaFact]`), unbekannter Wert → `Normal`.
- View: ein Messtest wie der Overview-Zeilenhöhen-Test aus
  `Tab-Reihenfolge.md` - reale `ListBoxItem.Bounds.Height` einer
  einzeiligen Event-Zeile in Compact < Normal. Absichert, dass kein lokaler
  Wert die Theme-Ressource aushebelt (CLAUDE.md-Gotcha "lokaler Wert
  schlägt Style").

---

## Teil B: Überschrift "Events" entfernen

- `GroupDetailView.axaml` Zeile ~189: `TextBlock Text="Events"` im
  Header-Row (Grid.Row 0, Column 0) entfällt. Die Header-Row selbst bleibt
  (Column 2 hält dort den Hints/Items-Umschalter) - **in diese frei
  gewordene Zelle wandert die neue einzeilige Filterleiste aus Teil C**,
  sodass Events-Filter und Hints/Items-Umschalter auf einer Höhe stehen.
  Damit entfällt eine komplette Zeile, nicht nur ein Label.
- `DashboardView.axaml`: die "Overview"- bzw. "Events · all servers, all
  activity"-Überschrift (Row 0 der linken Spalte) entfällt; der
  Overview/Events-Umschalter darunter sagt bereits, was man sieht. Die
  Dashboard-`SummaryText`-Zeile bleibt vorerst (Idee 3 ist nicht Teil
  dieses Plans). Die "Hints · unfound only"-Überschrift rechts: ebenfalls
  entfernen, "unfound only" als Tooltip auf der ersten Filterzeile oder als
  Placeholder-Hinweis - siehe offene Entscheidung 3.

---

## Teil C: Filter in eine Zeile

### Server-Tab, Events-Spalte

Heute: Row 0 Buttons, Row 1 Checkboxen, Row 2 "Show:" + Slot + Copy.
Neu, **eine** `WrapPanel`-Zeile (bricht bei schmaler Spalte weiter
sauber um, siehe bestehender Kommentar zu WrapPanel vs. StackPanel):

```
[All|Concerns me|Found by me] ┃ [All|Hints|Items|Chat] ┃ [Classes 4/4 ▾] [All slots ▾]
```

- **Item-Klassen**: die vier Checkboxen wandern in einen `Button` mit
  `Flyout` (Checkboxen darin, Bindings unverändert auf
  `ShowProgressionItemEvents` & Co.). Button-Text aus neuer VM-Property
  `EventClassFilterButtonText` ("Classes" wenn alle an, sonst "Classes 3/4").
  Aktiv-Optik (`Classes.active`) wenn nicht alle an - dieselbe
  `filter-toggle`-Klasse wie die anderen Buttons, damit ein aktiver Filter
  auf einen Blick erkennbar bleibt. Ein `Button`+`Flyout` statt
  `DropDownButton`, weil die App Flyouts bereits nutzt (Update-Flyout in
  `MainWindow.axaml`) - kein neues Control-Muster.
- **"Show:"-Label** entfällt (der Slot-Dropdown zeigt "All slots" selbst).
- **"Copy from here"**: raus aus der Filterzeile, stattdessen als
  schwebender Button über der Liste unten links (gleiches Overlay-Muster
  wie `JumpToNewestButton` unten rechts), nur sichtbar solange eine
  Auswahl besteht (`IsVisible` statt heute `IsEnabled`, gesetzt in
  `OnEventsListSelectionChanged`). Zusätzlich als Eintrag im
  Rechtsklick-Kontextmenü der Liste. Name `CopyEventsFromHereButton` bleibt,
  damit `CopyFromHereTests` nur die Enabled→Visible-Assertion ändern muss.

### Server-Tab, Hints/Items-Spalte

Heute bis zu drei Filterzeilen + Slot/Suche/Copy. Neu:

```
Zeile 1: [All|Unfound] ┃ [All|My location (6)|My item (5)] ┃ [All|Progress|Useful|Filler|Trap]
Zeile 2: [All slots ▾] [Search...............]
```

- Die Kategorie-Buttons **bleiben Buttons** (Entscheidung 4) - kein
  Dropdown, damit Umschalten ein Klick bleibt. Stattdessen wandern Row 0
  und die Hints-only-Row 1 in **ein gemeinsames `WrapPanel`** mit
  Trennstrich (gleiches `Border Width="1"`-Muster wie zwischen den anderen
  Gruppen). Bei breiter Spalte ist das eine Zeile; bei schmaler bricht es
  wie heute um - dann greift Teil D. Die eigene Row-1-Sonderzeile (siehe
  Kommentar "list's start position shifts down slightly") entfällt
  strukturell. Das Items-Panel hat schon nur eine Button-Reihe und bleibt
  so.
- "Copy from here" (Hints): gleiches Overlay-/Kontextmenü-Muster wie bei
  Events.
- Die Spalte ist schmal (1*), Zeile 1 wird also oft trotzdem umbrechen -
  genau dafür ist Teil D da.

### Dashboard

- Events-Panel: Relevanz- und Kategorie-Buttons, `Classes ▾`-Flyout,
  Server- und Slot-Dropdown in eine `WrapPanel`-Zeile - gleiche
  Bausteine wie im Server-Tab. Overview/Events-Umschalter bleibt eine
  eigene Zeile darüber (er gehört zu beiden Panels, nicht nur zu Events).
- Hints-Spalte: Server/Slot-Dropdowns und die Kategorie-Buttons in ein
  gemeinsames `WrapPanel` (Buttons bleiben Buttons, siehe oben).

### Wiederverwendung

Die `Classes ▾`-Flyout-Schaltfläche taucht zweimal auf (Tab + Dashboard,
beide VMs haben dieselben vier `Show…ItemEvents`-Properties). Als
gemeinsames Stück in `SharedTemplates.axaml` oder als kleines
`UserControl` (`Views/ItemClassFilterButton.axaml`) - nicht zweimal
kopieren. Die Button-Text-Logik als statische Hilfsfunktion, die beide VMs
aufrufen.

### Tests

- Logik: `EventClassFilterButtonText` für 4/4, 3/4, 0/4 (Group- und
  Dashboard-VM).
- View: Filterzeile ist eine einzige Zeile bei 1000 px Breite (Höhe der
  WrapPanel ≈ eine Button-Höhe); Klick auf eine Checkbox im Flyout ändert
  `VisibleEvents`; "Copy from here" unsichtbar ohne Auswahl, sichtbar mit.
- Bestehende Tests anpassen: `CopyFromHereTests` (Enabled → Visible),
  `DashboardTabTests`, `SlotDropdownWidthTests` (Slot-Dropdown sitzt jetzt
  in anderer Zeile - Namen/Breiten bleiben gleich, sollte nur Suchen
  betreffen, falls nach Zeilen-Position gesucht wird).

---

## Teil D: Filterleiste einklappbar

### Verhalten

Links vor der (neuen, einzeiligen) Filterleiste ein kleiner Chevron-Toggle
`▾`/`▸`. Eingeklappt ersetzt eine einzeilige Zusammenfassung die
Filter-Controls:

```
▸ Concerns me · Items · 3/4 classes · Null
```

- Nur **abweichende** Filter werden genannt; sind alle auf Standard:
  `▸ Filters` (gedämpfte Opacity). So sieht man auch eingeklappt, dass
  gerade gefiltert wird - wichtig, damit niemand "fehlende" Events sucht.
- Ein Klick irgendwo auf die Zusammenfassung klappt auf.
- Pro Panel-Typ separat: Events-Filter (Tab), Hints/Items-Filter (Tab),
  Dashboard-Events, Dashboard-Hints. Zustand gilt **für alle Tabs
  gleichzeitig** (kein Umspringen beim Tab-Wechsel) und übersteht einen
  App-Neustart (Entscheidung 5).
- Der Hints/Items-Umschalter und der Overview/Events-Umschalter werden
  **nicht** mit eingeklappt - sie sind Navigation, keine Filter.

### Umsetzung

- Neues wiederverwendbares Control `Views/CollapsibleFilterBar` (
  `ContentControl`-Ableitung oder `UserControl` mit
  `IsExpanded`/`Summary`/`Content`-Properties), nicht Avalonias `Expander`:
  dessen Fluent-Template hat eine große Kopfzeile (MinHeight, Rahmen,
  Padding) und würde den gewonnenen Platz direkt wieder auffressen.
- Zusammenfassungstexte als VM-Properties (`EventFilterSummaryText`,
  `HintFilterSummaryText`, `ItemFilterSummaryText` in `GroupViewModel`;
  Pendants in `DashboardViewModel`), neu berechnet bei Änderung jedes
  beteiligten Filters (gleiches `partial void On…Changed`-Muster wie
  `VisibleEvents` heute).
- Eingeklappt-Zustand: ein kleines `ObservableObject` `FilterBarLayoutState`
  (vier bool-Properties), einmal von `MainWindowViewModel` erzeugt und an
  jeden `GroupViewModel` (optionaler Konstruktor-Parameter, wie
  `multiworldTrackerService`) sowie `DashboardViewModel` weitergereicht -
  so teilen sich alle Tabs und das Detached-Window denselben Zustand.
  Persistiert (Entscheidung 5) als vier Felder in `AppSettings`, beim Start
  geladen und bei jeder Änderung gespeichert.

### Tests

- Logik: Zusammenfassungstext für Standard-Filter ("Filters"), für
  einzelne und kombinierte Abweichungen, Slot-Filter mit Alias.
- Logik: Einklappen in einem Tab wirkt auf alle `GroupViewModel`s.
- View: eingeklappt ist die Filterzeile nicht sichtbar und die
  Zusammenfassung schon; Klick auf die Zusammenfassung klappt auf.

---

## Entscheidungen

Entschieden (2026-09-25):

1. **Default-Dichte: `Compact`** - auch für bestehende Installationen (das
   fehlende Feld in alten `settings.json` deserialisiert zum Default).
4. **Hints/Items-Kategorie bleibt eine Button-Reihe**, kein Dropdown (siehe
   Teil C).
5. **Einklapp-Zustand wird persistiert** (`AppSettings`, siehe Teil D).

Wie empfohlen entschieden (2026-09-25):

2. **Overview-Zeilen im Dashboard** in Compact mitverkleinern (dann
   `DynamicResource` statt der ausgemessenen Literal-Margin) oder so lassen?
   Empfehlung: so lassen, die Overview hat nur eine Zeile pro Server.
3. **"· unfound only"** (Dashboard-Hints) nach Wegfall der Überschrift:
   Tooltip, oder als Teil der Filterleiste sichtbar lassen? Empfehlung:
   Tooltip auf der Hints-Liste.
6. **Default beim ersten Start**: aufgeklappt (auffindbar) oder
   eingeklappt (maximaler Platz)? Empfehlung: aufgeklappt.

## Erwarteter Effekt (grob, Server-Tab, ~670 px Fensterhöhe)

- Teil B + C: Events-Liste beginnt drei Zeilen früher (~90-100 px).
- Teil D eingeklappt: spart vor allem in der schmalen Hints-Spalte und bei
  schmalen Fenstern, wo Teil C's Zeile trotzdem umbricht.
- Teil A: Zeilenhöhe ~30 → ~20 px, dazu niedrigere Buttons/ComboBoxen in
  allen Kopfzeilen.

Zusammen: grob 15-20 statt ~8 sichtbare Events.

## Reihenfolge

1. Teil A (unabhängig, sofort messbarer Effekt; danach neu anschauen, wie
   viel Druck auf C/D noch bleibt).
2. Teil B + C zusammen (B schafft die Zelle, in die C's Zeile wandert).
3. Teil D auf C aufsetzend.
