# Umsetzungsplan: Konsistente Server-Farben

## Status: ✅ Umgesetzt (2026-09-21)

Genau wie unten geplant gebaut, mit einer einzigen realen Abweichung:

- **`GroupViewModel.ColorBrush`:** der Plan ging von `Brush.TryParse` aus.
  Gegen den echten Avalonia-12.0.4-Quellcode geprüft (nicht die
  main-Branch-Doku, siehe CLAUDE.md-Regel zu NuGet-Paketen) zeigte sich,
  dass `Avalonia.Media.Brush` in dieser Version nur ein throwendes
  `Parse(string)` anbietet, kein `TryParse`. Der Fallback auf die erste
  Palettenfarbe bei leerem/kaputtem `Group.Color` läuft deshalb über ein
  `try`/`catch` um `Brush.Parse` statt über die geplante `TryParse`-Prüfung
  - gleiches Ergebnis, nur die Absicherung sitzt anders.

Alles andere (Palette, Zuweisungsalgorithmus, Migration, betroffene
Dateien/Stellen, Tests) entspricht exakt dem Plan unten.

---

Entstanden aus der Frage, ob sich jedem Server (jeder `ServerConnectionGroup`)
eine eigene Farbe zuordnen lässt, die dann überall in der App konsistent
wiederverwendet wird - Tab, konsolidiertes Event-/Chat-Log, globale Hints,
Dashboard-Overview. Design vollständig durchdiskutiert (siehe
"Entscheidungen"); dieser Plan legt zusätzlich die konkreten Dateien/Stellen
fest.

## Entscheidungen

- **Farbquelle:** Vollautomatisch, keine manuelle Wahl/Override durch den
  Nutzer. Bei der Vergabe wird sichergestellt, dass kein Duplikat entsteht,
  solange die Palette das hergibt.
- **Stabilität:** Die Farbe hängt an `ServerConnectionGroup.Id`, nicht an
  Listenposition/Tab-Reihenfolge, wird einmal bei Anlage vergeben und danach
  nie mehr automatisch neu gewürfelt - auch nicht, wenn durch das Löschen
  eines anderen Servers eine Farbe wieder frei würde. Stabilität wird höher
  gewichtet als lückenlose Dedup-Garantie.
- **Sichtbarkeit:** Akzent, kein voller Hintergrund - ein kleiner Farbpunkt
  plus der Server-Name selbst in Server-Farbe eingefärbt, an den Stellen wo
  ein Server-Name/-Label ohnehin schon angezeigt wird.
- **Palette-Verhalten:** Feste, kuratierte Palette; sind mehr Server als
  Palettenfarben konfiguriert, wiederholt sich die Palette.
- **Ebene:** Die Farbe gehört zum Server (`ServerConnectionGroup`), nicht zum
  einzelnen Slot - alle Slots eines Servers teilen sich dieselbe Farbe,
  passend zur bestehenden Modellierung (Host/Port/Password sind ohnehin
  Server-weit).
- **Palette (10 Farben, feste Reihenfolge):** `DodgerBlue`, `MediumSeaGreen`,
  `Tomato`, `Gold`, `Orchid`, `Turquoise`, `Coral`, `CornflowerBlue`,
  `YellowGreen`, `HotPink` - feste, benannte Avalonia-Brushes, dieselbe
  Konvention wie `EventTextSegmentKindToBrushConverter` (Magenta/Orange/
  Gold/Salmon/Plum/SlateBlue/Cyan/Firebrick), keine Theme-Unterscheidung
  (`App.axaml` hat ohnehin keine eigenen Farb-Ressourcen, an denen sich eine
  Light-/Dark-geprüfte Palette orientieren könnte). Überschneidungen mit der
  bestehenden Item-/Spieler-Palette sind laut Absprache okay - die
  Server-Farbe sitzt an optisch getrennten Stellen (Tab-Header,
  Zeilen-Label), nie im selben Textsegment.
- **Zuweisungsalgorithmus:** Für jede Palettenfarbe wird gezählt, wie viele
  aktuell konfigurierte Gruppen sie schon tragen; die neue Gruppe bekommt die
  Farbe mit der niedrigsten Nutzungszahl, bei Gleichstand die erste davon in
  Palettenreihenfolge. Für die ersten 10 Gruppen ist das automatisch "erste
  noch unbenutzte Farbe"; ab der 11. beginnt die Palette deterministisch
  wieder bei `DodgerBlue`.
- **Persistenz:** Die vergebene Farbe wird als konkreter Brush-Name-String
  (nicht als Palette-Index) in `groups.json` abgelegt - bleibt stabil, falls
  die Palette später erweitert/umsortiert wird.
- **Migration bestehender Gruppen:** Kein separater Migrationspfad - derselbe
  Zuweisungsalgorithmus läuft beim Laden einmal für jede noch farblose Gruppe,
  in der Reihenfolge, in der sie in `groups.json` stehen.

## Vorbereitung für ein späteres Follow-up: Farbwähler nach einem Light/Dark-Switch

Aktuell folgt die App nur dem System-Theme (`App.axaml`s
`RequestedThemeVariant="Default"`) - es gibt weder einen In-App-Umschalter
noch ein Feature-Plan-Dokument dafür. Die "Farbquelle: vollautomatisch, kein
manuelles Override"-Entscheidung oben gilt nur für **diesen** Plan. Sobald es
einen echten In-App Light/Dark-Mode-Switch gibt (separates, noch nicht
existierendes Feature), sollte als Folge-Feature ein manueller Farbwähler pro
Server ergänzt werden: die feste Palette ist bewusst so gewählt, dass sie auf
*beiden* System-Themes einigermaßen funktioniert, aber niemand hat sie gegen
eine echte, vom Nutzer aktiv gewählte Dark-Palette geprüft - sobald der
Nutzer das Theme selbst umschalten kann (statt es nur passiv vom OS zu
übernehmen), sollte er auch selbst nachjustieren können, falls eine
Palettenfarbe in seinem gewählten Theme schlecht lesbar ist, statt auf eine
von uns geratene Kontrastprüfung angewiesen zu sein. Damit dieses Follow-up
diesen Plan hier nicht nachträglich umbauen muss:

- `ServerConnectionGroup.Color` bleibt ein freier String (Brush-Name oder
  Hex), keine Enum/kein Palette-Index - ein Farbwähler kann also später
  einfach einen beliebigen anderen Wert hineinschreiben, ohne das
  Persistenz-Format zu ändern.
- `ServerColorPalette.AssignColor` bleibt reine Vorschlagslogik für die
  automatische Erstzuweisung, kein Aufruf-Zwang - ein späterer manueller
  Override ruft sie einfach nicht mehr auf, statt sie umbauen zu müssen.
- `GroupViewModel.ColorBrush` liest ohnehin nur `Group.Color` aus, unabhängig
  davon, ob der Wert automatisch oder manuell gesetzt wurde - Views müssen
  für den Override-Fall nicht angefasst werden.

## Ansatz

1. **`Models/ServerColorPalette.cs` (neu):** statische Klasse mit der festen
   Palette (`IReadOnlyList<string> Colors`) und
   `string AssignColor(IEnumerable<string> alreadyAssignedColors)` -
   implementiert exakt den oben beschriebenen Algorithmus
   (Nutzungszahl je Palettenfarbe zählen, niedrigste Zahl gewinnt, Gleichstand
   nach Palettenreihenfolge). Reine Funktion, keine Abhängigkeit auf
   `ServerConnectionGroup`/`GroupViewModel`, damit sie sowohl von
   `MainWindowViewModel` als auch von `PersistenceService` ohne Umweg
   aufgerufen werden kann (siehe 3./4.).

2. **`Models/ServerConnectionGroup.cs`:** neue persistierte Eigenschaft
   `[ObservableProperty] private string _color = string.Empty;` (kein
   `[JsonIgnore]`, im Gegensatz zu `Password` - die Farbe soll ja gerade
   dauerhaft in `groups.json` landen). Leerer String bedeutet "noch nicht
   zugewiesen" und wird nur unmittelbar nach dem Laden/Anlegen kurzzeitig
   auftreten (siehe 3./4.), nie in einer schon initialisierten
   `GroupViewModel`.

3. **`Services/PersistenceService.cs`:** neue private Methode
   `AssignMissingColors(List<ServerConnectionGroup> groups)`, analog zu
   `ApplyLegacyPasswordRequirement` direkt daneben - iteriert `groups` in
   Reihenfolge, ruft für jede Gruppe mit leerem `Color`
   `ServerColorPalette.AssignColor(groups.Select(g => g.Color))` auf (nur die
   bereits verarbeiteten/schon farbigen Gruppen zählen dabei tatsächlich mit,
   da alle anderen noch `""` sind). Aufgerufen in `LoadGroups()`
   unmittelbar vor jedem `return` mit einer nichtleeren Liste - also nach der
   erfolgreichen `Deserialize` in Zeile ~97 und nach der aus
   `MigrateLegacyProfilesIfNeeded()` zurückgegebenen `migrated`-Liste in
   Zeile ~85 (beide Male vor dem jeweiligen `return`, im Migrations-Fall vor
   dem `SaveGroups(migrated)`-Aufruf, damit die zugewiesenen Farben gleich
   mitgeschrieben werden). Der leere-Liste-Fall (frische Installation) bleibt
   unangetastet - keine Gruppen, nichts zuzuweisen.

4. **`ViewModels/MainWindowViewModel.cs`, `AddNewGroup` (Zeile ~468-495):**
   direkt nach dem Erzeugen von `group` (Zeile ~470-479), vor
   `group.Slots.Add(slot)`, `group.Color = ServerColorPalette.AssignColor(Groups.Select(g => g.Group.Color));`
   setzen - `Groups` ist die bestehende `ObservableCollection<GroupViewModel>`
   mit allen aktuell konfigurierten Servern. Die Farbe steht damit schon fest,
   bevor Zeile ~489 die `GroupViewModel`-Hülle drumherum baut - kein
   nachträgliches Neu-Auslösen von Property-Changed-Benachrichtigungen nötig
   (siehe Punkt 5).

5. **`ViewModels/GroupViewModel.cs`:** neue Eigenschaft direkt neben
   `HeaderText` (Zeile ~336): `public IBrush ColorBrush => ...` - liest
   `Group.Color` über eine kleine private Hilfsmethode aus, die
   `Brush.Parse` aufruft und bei leerem/kaputtem `Group.Color` auf die erste
   Palettenfarbe (`ServerColorPalette.Colors[0]`) zurückfällt, statt eine
   `FormatException` bis in die View durchschlagen zu lassen - reine
   Absicherung, in der Praxis erreicht ein leeres `Color` nie eine bereits
   konstruierte `GroupViewModel` (siehe Punkte 3./4.). **Kein**
   `OnPropertyChanged(nameof(ColorBrush))` in `OnGroupChanged`/
   `OnGroupPropertyChanged` (anders als `HeaderText`, das auf
   `ServerConnectionGroup.Name`-Änderungen reagieren muss, weil der Name im
   Connection-Editor bearbeitbar ist) - die Farbe ändert sich laut
   Entscheidung nie nach der Erstzuweisung, die immer schon abgeschlossen ist
   bevor eine `GroupViewModel`-Instanz sie zum ersten Mal liest.

6. **`Views/MainWindow.axaml`, Tab-Header-`DataTemplate`
   (`DataType="vm:GroupViewModel"`, Zeile ~207-223):** im bestehenden
   `StackPanel Orientation="Horizontal"` einen kleinen Farbpunkt ergänzen
   (`<Ellipse Width="6" Height="6" Fill="{Binding ColorBrush}"/>`, vor der
   bereits vorhandenen Connection-State-`Ellipse`) und
   `TextBlock Text="{Binding HeaderText}"` (Zeile ~216) um
   `Foreground="{Binding ColorBrush}"` ergänzen.

7. **`Views/DashboardView.axaml`:**
   - Overview-Zeile (Zeile ~255-258, `DataTemplate DataType="vm:GroupViewModel"`):
     gleiches Muster wie 6. - Farbpunkt vor der Connection-State-`Ellipse`,
     `Foreground="{Binding ColorBrush}"` auf dem `HeaderText`-`TextBlock`.
   - Konsolidierte Events-Zeile (Zeile ~456, `DataTemplate DataType="m:DashboardEventRow"`):
     `Foreground="#185fa5"` (fest verdrahtetes Blau) durch
     `Foreground="{Binding Group.ColorBrush}"` ersetzen - `Group` ist hier
     bereits eine `GroupViewModel`-Instanz, also direkter Zugriff auf die
     neue Eigenschaft aus Punkt 5 ohne weitere Anpassung.
   - Konsolidierte Hints-Zeile (Zeile ~595, `DataTemplate DataType="m:DashboardHintRow"`):
     dieselbe Änderung, `Foreground="#185fa5"` → `Foreground="{Binding Group.ColorBrush}"`.

## Was bewusst gleich bleibt / außerhalb des Scopes

- Kein manuelles Überschreiben der Farbe im Connection-Editor (siehe
  Entscheidungen) - `ConnectionEditorViewModel`/`ConnectionEditorWindow.axaml`
  bleiben unangetastet.
- Die Filter-Dropdowns (Server-Auswahl in den Events-/Hints-/Send-Filtern,
  z. B. `DashboardView.axaml` Zeile ~412-435/487-504/543-568) bekommen keine
  Farbmarkierung - reine Zusatz-Politur ohne funktionalen Nutzen, kann bei
  Bedarf später separat ergänzt werden.
- Kein per-Zeile-Farbpunkt in den konsolidierten Events-/Hints-Listen (nur
  Tab-Header und Dashboard-Overview bekommen einen echten Punkt) - der
  eingefärbte Server-Name-Text in jeder Zeile ist dort Akzent genug, ohne bei
  jeder einzelnen Log-Zeile ein zusätzliches visuelles Element einzuführen.

## Tests

- **Kategorie A** (neue Datei, z. B. `ServerColorPaletteTests.cs`): reine
  Logiktests gegen `ServerColorPalette.AssignColor` - leere Eingabe liefert
  die erste Palettenfarbe; 9 bereits vergebene, alle unterschiedliche Farben
  liefert die 10. noch unbenutzte; 10 bereits vergebene (jede Palettenfarbe
  genau einmal) liefert wieder die erste Palettenfarbe (Wrap-Around);
  unausgewogene Verteilung (z. B. eine Farbe doppelt, eine dritte noch gar
  nicht vergeben) liefert die noch unbenutzte, nicht einfach "die mit
  insgesamt niedrigstem Index".
- **Kategorie A** (Erweiterung von `PersistenceServiceTests.cs`):
  Round-Trip-Test, der mehrere `ServerConnectionGroup`s ohne `Color` über
  `SaveGroups`/`LoadGroups` schickt (bzw. eine vorbereitete
  `groups.json`-Testdatei ohne `"Color"`-Feld lädt, um den Migrationsfall
  einer schon existierenden Installation realistisch nachzustellen) und
  prüft, dass jede Gruppe danach eine nichtleere, gemäß Zuweisungsalgorithmus
  korrekte Farbe hat.
- Kein dedizierter Kategorie-C-View-Test geplant - die neuen Bindings
  (`ColorBrush` auf vorhandene `Foreground`/`Fill`-Properties) sind reine
  Ein-Zeiler-Bindings ohne eigene Logik, im gleichen Rahmen wie das bereits
  ungetestete `HeaderText`-Binding. Manuelle Sichtprüfung über
  `dotnet run --project AvaloniaApplication1` mit mehreren angelegten
  Servern reicht, um das Ergebnis (Tab-Punkte, Dashboard-Overview,
  konsolidierte Events/Hints) visuell zu bestätigen.
