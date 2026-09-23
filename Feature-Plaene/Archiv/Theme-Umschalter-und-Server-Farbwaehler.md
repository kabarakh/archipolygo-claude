# Umsetzungsplan: Theme-Umschalter (System/Hell/Dunkel) + manueller Server-Farbwähler

## Status: ✅ Umgesetzt (2026-09-23)

Auf expliziten Wunsch **ohne** den `ui-feature-prototyp`-Skill/TestHarness-
Zwischenschritt umgesetzt (siehe "Nächster Schritt" unten, der ursprünglich
davor vorgesehen war) - direkt in `AvaloniaApplication1`. Build und die
gesamte Testsuite (`dotnet test AvaloniaApplication1.sln`, 312/312) sind
grün, aber eine echte **visuelle Prüfung am laufenden Fenster steht noch
aus** - diese Sandbox-Umgebung hat kein Display, um die App tatsächlich zu
starten und anzusehen. Vor allem folgende Punkte sollten beim ersten
`dotnet run` gegengeprüft werden, bevor das als endgültig abgeschlossen gilt:

- Sitzt der Theme-Button wirklich sichtbar oben rechts, und ist der
  ◐/☀/🌙-Glyphenwechsel klar erkennbar?
- Rendert der `ColorPicker` im Connection-Editor korrekt (das
  `StyleInclude` für `Avalonia.Controls.ColorPicker` wurde nur gegen die
  offizielle Doku/den Quellcode geprüft, nie tatsächlich gerendert)?
- Wirken die berechneten Light-Werte (siehe "Konkrete Farbwerte" unten) am
  echten Fluent-Light-Hintergrund gut, oder brauchen sie noch Nachjustierung?

Abweichungen von der ursprünglichen Planung:

- **`MainWindowViewModel.AddNewGroup`/`UpdateGroup`:** der neue
  `color`-Parameter ist entgegen der ursprünglichen Absicht **optional**
  (`string? color = null`), nicht required - andernfalls hätten alle
  ~50 bestehenden Testaufrufe (die `autoConnect:`/`preferredLeaderSlotId:`
  als benannte Argumente nutzen, ohne `color`) angepasst werden müssen.
  `null`/leer fällt bei `AddNewGroup` auf die alte automatische
  `ServerColorPalette.AssignColor`-Zuweisung zurück, bei `UpdateGroup` lässt
  es die vorhandene Farbe schlicht unangetastet.
- **`EventTextSegmentKindToBrushConverter`:** liest das aktuelle Theme jetzt
  direkt bei jedem `Convert`-Aufruf (`Application.Current.ActualThemeVariant`)
  statt über Theme-Variant-Ressourcen-Dictionaries - einfacher, aber mit
  einer dokumentierten Einschränkung: eine bereits gerenderte Log-Zeile
  färbt sich bei einem Theme-Wechsel zur Laufzeit nicht rückwirkend um
  (Avalonia ruft einen Converter nur bei einer Änderung des Quellwerts neu
  auf, nicht bei einem globalen Theme-Wechsel) - nur neue Zeilen nutzen
  sofort die neuen Farben. Ein Umbau auf echte Theme-Dictionaries (der das
  löst) war bewusst außerhalb des Scopes dieses Durchlaufs.
- **`GroupViewModel.ColorBrush`:** reagiert jetzt sowohl auf
  `Group.PropertyChanged` (Farbe kann sich nach dem manuellen Farbwähler
  ändern) als auch auf `Application.Current.ActualThemeVariantChanged` -
  die alte `Server-Farben.md`-Aussage "wird nie neu zugewiesen" gilt seit
  diesem Feature nicht mehr.

Alles andere (Entscheidungen, Ansatz, Farbwerte) entspricht dem Plan unten.

---

## Ausgangslage

`Feature-Plaene/Archiv/Server-Farben.md` hat dieses Follow-up in seinem
eigenen Abschnitt "Vorbereitung für ein späteres Follow-up: Farbwähler nach
einem Light/Dark-Switch" bereits angekündigt: die feste 10-Farben-Palette
(`Models/ServerColorPalette.cs`) wurde nie gegen ein echtes Dark-Theme
geprüft, und `ServerConnectionGroup.Color` wurde bewusst als freier String
(nicht Enum/Index) modelliert, damit ein späterer manueller Override sie
einfach überschreiben kann, ohne das Persistenzformat anzufassen.

Aktuell:

- `App.axaml` setzt `RequestedThemeVariant="Default"` (folgt nur dem
  OS-Theme) - kein In-App-Umschalter.
- `Models/AppSettings.cs` + `ViewModels/SettingsViewModel.cs` +
  `Views/SettingsWindow.axaml` sind der bestehende globale
  Einstellungs-Mechanismus (`DefaultAutoConnect`, `EventHistoryLimit`),
  persistiert über `Services/PersistenceService.cs` nach
  `%AppData%/Archipolygo/settings.json`.
- `Models/ServerColorPalette.cs` (10 feste, benannte Avalonia-Brushes) weist
  jeder Gruppe beim Anlegen automatisch eine Farbe zu, kein manueller
  Override möglich (`GroupViewModel.ColorBrush` liest nur `Group.Color`).
- `Converters/EventTextSegmentKindToBrushConverter.cs` hat eine zweite,
  unabhängige feste Farbpalette für Item-Flags (Magenta/Orange/Gold/Salmon/
  Plum/SlateBlue/Cyan/Firebrick) - ebenfalls nie gegen Dark-Theme geprüft.

## Recherche: Farbschemata im offiziellen Archipelago-Projekt

Nachgeschaut in [ArchipelagoMW/Archipelago](https://github.com/ArchipelagoMW/Archipelago)
(`main`-Branch) und [ArchipelagoMW/Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net)
(Tag `v6.7.1`, exakt die in `AvaloniaApplication1.csproj` referenzierte
Version), statt zu raten:

- **`NetUtils.py`** (`JSONtoTextParser.color_codes`) und **`data/client.kv`**
  (`<TextColors>`, dort mit Kommentaren wie "typically your slot/player")
  definieren beide **exakt dieselben** Hex-Werte für die Item-/Spieler-Farben
  im Nachrichtenlog: `magenta=EE00EE` (eigener Slot), `yellow=FAFAD2`
  (andere Slots/Spieler), `cyan=00EEEE` (normales Item), `plum=AF99EF`
  (Progression-Item), `slateblue=6D8BE8` (Useful-Item), `salmon=FA8072`
  (Trap-Item), `green=00FF7F` (Location), `blue=6495ED` (Entrance),
  `orange=FF7700` (Command-Echo). Das ist die "offizielle Palette", auf die
  sich der bestehende Kommentar in
  `Converters/EventTextSegmentKindToBrushConverter.cs` bereits bezieht (bisher
  nur über ähnlich aussehende *Avalonia-Standardnamen* angenähert, nicht
  pixelgenau).
- **Wichtiger Befund, der die ursprüngliche Annahme in dieser Diskussion
  korrigiert:** Es gibt **keine** separaten Light-/Dark-Varianten dieser
  Palette. `data/client.kv` setzt `theme_style: "Dark"` nur für die
  KivyMD-Chrome (Fenster-Hintergrund, Buttons, Navigation - über
  `primary_palette`/`dynamic_scheme_name`, ebenfalls per Konfiguration
  wählbar, das sind die "mehreren fertigen Schemata"), aber die
  `TextColors`-Werte selbst (also genau die Item-/Spieler-Farben oben) werden
  unverändert für beide `theme_style`-Einstellungen verwendet - der offizielle
  Client hat für dieses konkrete Problem (Log-Textfarben gegen wechselnden
  Hintergrund) also selbst keine fertige Lösung, an der man sich für den
  Light-Fall orientieren könnte.
- Für die 10 Server-Farben (`ServerColorPalette`) gibt es ohnehin kein
  Vorbild in Archipelago selbst - das ist ein reines Feature dieser App ohne
  Gegenstück im Original.

**Konsequenz für diesen Plan:** Die Dark-Theme-Werte der Item-/Spieler-Palette
werden 1:1 von der offiziellen Archipelago-Palette übernommen (siehe Tabelle
unten) - das ist zugleich eine kleine Fidelity-Verbesserung gegenüber den
aktuellen, nur angenäherten Avalonia-Standardnamen. Die Light-Theme-Werte
mussten dagegen selbst hergeleitet werden, da Archipelago dafür keine Vorlage
liefert: gleicher Farbton (Hue) wie die jeweilige Dark-Variante, Helligkeit
so weit abgesenkt, bis ein WCAG-Kontrast von mindestens 4.5:1 gegen Weiß
erreicht ist (rechnerisch geprüft, nicht geschätzt). Der Dark-Hintergrund für
die Kontrastprüfung der umgekehrten Richtung wurde mit einem typischen
Fluent-Dark-Chrome-Ton (~`#202020`) angenommen - das ist eine Näherung, die
beim `ui-feature-prototyp`-Durchlauf gegen den tatsächlich gerenderten
Hintergrund gegengeprüft werden sollte (siehe "Nächster Schritt").

### Konkrete Farbwerte

**Item-/Spieler-Palette (`EventTextSegmentKindToBrushConverter`):**

| Segment | Dark-Theme (= offizielle AP-Palette) | Light-Theme (hergeleitet, ≥4.5:1 gg. Weiß) |
|---|---|---|
| `OwnSlotName` | `#EE00EE` (AP `magenta`) | `#D100D1` |
| `OtherSlotName` | `#FAFAD2` (AP `yellow`) **oder** aktuelles `Gold` `#FFD700` beibehalten - siehe Hinweis unten | `#8B7500` (Light-Variante von `Gold`) |
| `ConnectedSlotName` (App-eigen, kein AP-Pendant) | `#FFA500` (aktuelles `Orange`, unverändert) | `#A46A00` |
| `ItemTrap` | `#FA8072` (AP `salmon`) | `#E81F08` |
| `ItemProgression` | `#AF99EF` (AP `plum`) | `#805EE6` |
| `ItemUseful` | `#6D8BE8` (AP `slateblue`) | `#496FE2` |
| `ItemOther` (normales Item) | `#00EEEE` (AP `cyan`) | `#008484` |
| `DeathLink` (App-eigen, kein AP-Pendant) | `#E05B5B` (aufgehellte Variante von Firebrick) | `#B22222` (aktuelles `Firebrick`, unverändert - hat schon 6.7:1 gg. Weiß) |

**Hinweis zu `OtherSlotName`:** Die App nutzt heute bewusst `Gold` statt AP's
tatsächlichem, sehr blassem `yellow` (`#FAFAD2` wirkt auf dunklem Hintergrund
fast weiß/cremefarben, kaum noch als "eigene Farbe" erkennbar neben normalem
Text). Empfehlung: `Gold` beibehalten (bessere Unterscheidbarkeit von
Fließtext) statt auf AP's blasses Gelb zu wechseln, auch wenn das von
100%-AP-Treue abweicht - im Zweifel beim Prototyp-Durchlauf beide
nebeneinander ansehen und entscheiden.

**Server-Palette (`ServerColorPalette`, kein AP-Vorbild, gleiche
Herleitungsmethode):**

| Name | Dark-Theme (= aktuelle Palette, unverändert - testet bereits gut auf Dunkel) | Light-Theme (hergeleitet, ≥4.5:1 gg. Weiß) |
|---|---|---|
| DodgerBlue | `#1E90FF` | `#0074E6` |
| MediumSeaGreen | `#3CB371` | `#2D8654` |
| Tomato | `#FF6347` | `#E72300` |
| Gold | `#FFD700` | `#8B7500` |
| Orchid | `#DA70D6` | `#C633C1` |
| Turquoise | `#40E0D0` | `#168579` |
| Coral | `#FF7F50` | `#DB3B00` |
| CornflowerBlue | `#6495ED` | `#3071E7` |
| YellowGreen | `#9ACD32` | `#61811F` |
| HotPink | `#FF69B4` | `#E70074` |

Alle zehn aktuellen Server-Farben erreichen bereits 5:1-11.6:1 Kontrast gegen
einen dunklen Hintergrund (rechnerisch geprüft) - für Dark-Theme ist also
keine Änderung nötig, nur die Light-Varianten sind neu.

## Entscheidungen (aus der Diskussion)

- **Theme-Umschalter-Ort:** Eigener Button ganz oben rechts im Hauptfenster
  (nicht im Settings-Dialog) - explizite Nutzererwartung ("da erwarten Leute
  sowas"), nicht die bestehende `StackPanel`-Button-Reihe links oben.
- **Stufen:** 3 Stufen - System folgen / Hell / Dunkel. "System" bleibt als
  echte Option erhalten, nicht nur Übergangs-Default.
- **Persistenz:** Neues Feld in `AppSettings`, wie die bestehenden Felder
  über `PersistenceService` in `settings.json` gespeichert; angewendet auf
  `Application.Current!.RequestedThemeVariant` beim Start und sofort bei
  jedem Klick.
- **Farbwähler-Ort:** Im Connection-Editor, sowohl im `EditGroup`- als auch im
  `NewGroup`-Modus (nicht nur nachträglich beim Bearbeiten, sondern schon
  beim Server-Anlegen direkt mit anpassbar), neben den anderen
  server-weiten Feldern (Host/Port/Passwort) - nicht am Tab-Header, kein
  Schnellzugriff dort. Im `AddSlot`-Modus (Slot zu einem bestehenden Server
  hinzufügen) nicht sichtbar - die Farbe gehört zum Server, der bei diesem
  Modus schon feststeht.
- **Farbwähler-Typ:** Freie Farbwahl (RGB/Hex) über Avalonias
  `ColorPicker`-Control, nicht auf die 10 Palettenfarben beschränkt.
- **Duplikate:** Erlaubt - eine bewusste manuelle Wahl darf dieselbe Farbe
  wie ein anderer Server tragen, keine Validierung/Warnung dagegen.
- **Zurücksetzen:** Eigener "Auf automatisch zurücksetzen"-Button im Editor,
  der frisch `ServerColorPalette.AssignColor` aufruft (mit den Farben aller
  *anderen* aktuell konfigurierten Gruppen) - kein Aufheben eines
  History-Werts nötig, da `Color` laut `Server-Farben.md` ohnehin nur ein
  freier String ohne eigene Versionsgeschichte ist; "zurücksetzen" bedeutet
  also "wie eine frisch angelegte Gruppe neu zugewiesen bekommen", nicht
  "die exakte alte Auto-Farbe wiederherstellen".
- **Paletten & Theme:** Größerer Scope als ursprünglich in `Server-Farben.md`
  vorgesehen ("keine Theme-Unterscheidung") - sowohl die 10 Server-Farben als
  auch die Item-Flag-Farben aus `EventTextSegmentKindToBrushConverter`
  bekommen einen Blick auf Dark-Theme-Lesbarkeit und werden bei Bedarf um
  eine Dark-Variante ergänzt.

## Offene Feinheiten (bewusst nicht geraten, sondern für den Prototyp/die Umsetzung offen gelassen)

- Exaktes Icon/Label des Theme-Buttons (Sonne/Mond-Symbol vs. Text, der durch
  die drei Zustände wechselt) und ob ein Klick durchzykelt
  (System→Hell→Dunkel→System→…) oder ein kleines Flyout mit drei expliziten
  Einträgen öffnet.
- `OtherSlotName`: `Gold` beibehalten oder auf AP's tatsächliches, sehr
  blasses `yellow` wechseln - siehe Empfehlung und Hinweis unter "Konkrete
  Farbwerte" oben.
- Die berechneten Light-Werte gehen von einem angenommenen Dark-Hintergrund
  (~`#202020`) aus - beim Prototyp-Durchlauf gegen den tatsächlich
  gerenderten Fluent-Dark-Hintergrund gegenprüfen und bei Bedarf leicht
  nachjustieren.

## Ansatz

1. **`Models/AppSettings.cs`:** neue Property
   `public string ThemePreference { get; set; } = "System";` (Klartext-String
   `"System"`/`"Light"`/`"Dark"` statt Enum - `System.Text.Json` braucht für
   Enums sonst einen extra registrierten `JsonStringEnumConverter`, den
   `PersistenceService` aktuell nicht hat; ein String vermeidet den
   Sonderfall). Fehlt das Feld in einer bestehenden `settings.json` (Upgrade
   von einer älteren Version), liefert die Deserialisierung automatisch den
   Property-Default `"System"` - kein eigener Migrationscode nötig, wie bei
   jedem anderen additiven `AppSettings`-Feld auch.

2. **Neue kleine Hilfsmethode** (z. B. in `MainWindowViewModel` oder einem
   winzigen neuen `ThemeService`, je nachdem was beim Schreiben schlanker
   wirkt) zum Anwenden der Präferenz:
   `Application.Current!.RequestedThemeVariant = preference switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };`
   Aufgerufen einmal beim Start (nach dem Laden von `AppSettings`) und erneut
   bei jedem Klick auf den neuen Button.

3. **`ViewModels/MainWindowViewModel.cs`:** neue
   `[ObservableProperty] private string _themePreference` +
   `[RelayCommand]`, das zyklisch (oder aus einem Flyout heraus, siehe
   "Offene Feinheiten") auf den nächsten Wert wechselt, `AppSettings`
   aktualisiert, Persistenz auslöst (gleiches Muster wie die bestehenden
   `AppSettings`-Felder) und Punkt 2 erneut aufruft.

4. **`Views/MainWindow.axaml`:** Der bestehende Toolbar-`StackPanel`
   (Zeile ~91-132) ist direkt das `DockPanel.Dock="Top"`-Kind des äußeren
   `DockPanel`s. Um einen Button wirklich **ganz rechts im Fenster** zu
   bekommen (nicht nur rechts *innerhalb der bestehenden Button-Reihe*),
   muss dieses Top-Kind zu einem `Grid` mit zwei Spalten (`*, Auto`) werden:
   Spalte 0 die unveränderte bestehende `StackPanel` mit allen jetzigen
   Buttons, Spalte 1 der neue Theme-Button, rechtsbündig.

5. **Neues NuGet-Paket `Avalonia.Controls.ColorPicker`**, exakt passend zur
   bestehenden `Avalonia`-Version `12.0.4` (siehe
   `AvaloniaApplication1.csproj`) - laut offizieller Avalonia-Doku
   (docs.avaloniaui.net, ColorPicker-Seite) ist `ColorPicker`/`ColorView`
   **nicht** Teil der Haupt-`Avalonia`/`Avalonia.Themes.Fluent`-Pakete
   (bewusst ausgelagert wegen ressourcenbeschränkter Zielplattformen).
   Zusätzlich in `App.axaml` neben dem bestehenden
   `StyleInclude Source="avares://Archipolygo/Styles/DialogStyles.axaml"`:
   `<StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"/>`
   - ohne dieses Include bleibt der Picker ungestylt.

6. **`Models/ServerConnectionGroup.cs`:** keine Änderung - `Color` ist bereits
   ein freier String, genau wie von `Server-Farben.md` für diesen Fall
   vorbereitet.

7. **`ViewModels/ConnectionEditorViewModel.cs`** (`EditGroup`- *und*
   `NewGroup`-Modus, nicht `AddSlot`):
   - neue `public bool ShowColorPicker => Mode != ConnectionEditorMode.AddSlot;`
     - exakt dasselbe Prädikat wie die schon vorhandenen `ShowAutoConnect`/
     `ShowMultiworldTracker` (Zeile ~253/256), also kein neuer Fall, sondern
     Anschluss an ein bereits etabliertes Pattern.
   - neue `[ObservableProperty] private Color _selectedColor`, Startwert
     abhängig vom Modus:
     - `EditGroup`: aus `_targetGroup.Color` geparst (gleicher Fallback auf
       `ServerColorPalette.Colors[0]` bei leer/kaputt wie
       `GroupViewModel.ColorBrush` es schon macht, statt eine
       `FormatException` durchschlagen zu lassen).
     - `NewGroup`: vorbefüllt mit `ServerColorPalette.AssignColor(_existingGroups.Select(g => g.Color))`
       - derselbe Vorschlag, den bisher `MainWindowViewModel.AddNewGroup`
       nach dem Erzeugen der Gruppe berechnet hat (siehe
       `Server-Farben.md`, Punkt 4), jetzt nur schon *vor* dem Speichern im
       Editor sichtbar und über den `ColorPicker` direkt anpassbar, statt
       danach unsichtbar im Hintergrund zugewiesen zu werden.
   - neuer `[RelayCommand] private void ResetColorToAutomatic()`, ruft in
     beiden Modi `ServerColorPalette.AssignColor` mit den `Color`-Werten
     aller *anderen* aktuell konfigurierten Gruppen (`_existingGroups`) auf
     und schreibt das Ergebnis in `SelectedColor` - im `NewGroup`-Fall macht
     das schlicht den ursprünglichen Vorschlag wieder rückgängig, falls der
     Nutzer zwischenzeitlich selbst etwas anderes gewählt hatte.
   - `TryBuildResult`/`ConnectionEditorResult` (rund um Zeile ~682-760):
     neues Feld `Color`, gesetzt als `ShowColorPicker ? SelectedColor.ToString() : string.Empty`.

8. **`Views/ConnectionEditorWindow.axaml`:** neuer Abschnitt
   `IsVisible="{Binding ShowColorPicker}"` mit
   `<ColorPicker Color="{Binding SelectedColor}"/>` plus dem
   "Auf automatisch zurücksetzen"-Button - sichtbar sowohl im `NewGroup`- als
   auch im `EditGroup`-Layout, deshalb bei den generellen server-weiten
   Feldern (Name/Host/Port, oben im Fenster) platziert statt bei der nur in
   `EditGroup` sichtbaren Slot-Management-Sektion.

9. **`ViewModels/MainWindowViewModel.cs`:** `AddNewGroup` und `UpdateGroup`
   bekommen je einen neuen `string color`-Parameter (durchgereicht von
   `Views/MainWindow.axaml.cs`s `OnAddServerClick`/`EditServerAsync` als
   `result.Color`). `AddNewGroup`s bisheriger eigener
   `group.Color = ServerColorPalette.AssignColor(...)`-Aufruf (siehe
   `Server-Farben.md`, Punkt 4) entfällt dadurch - die Farbe kommt jetzt
   fertig vom Editor, der denselben Vorschlag schon vorab berechnet und dem
   Nutzer zur Anpassung angezeigt hat, statt ihn nach dem Speichern erneut
   und unsichtbar zu bestimmen. `UpdateGroup` setzt `group.Color = color`
   analog zu seinen anderen Feld-Updates.

10. **`Models/ServerColorPalette.cs` + `Converters/EventTextSegmentKindToBrushConverter.cs`:**
   je einen zweiten (Light-)Farbsatz gemäß der Tabellen unter "Konkrete
   Farbwerte" ergänzen und je nach `App.Current!.ActualThemeVariant` (nicht
   `RequestedThemeVariant`, da dieser bei `ThemeVariant.Default` nicht den
   tatsächlich aktiven Modus widerspiegelt) den passenden Satz zurückgeben -
   z. B. beide Sätze als zweites `IReadOnlyList<string>`/Dictionary neben dem
   bestehenden `Colors` in `ServerColorPalette`, und ein entsprechender
   Theme-abhängiger Zweig in `EventTextSegmentKindToBrushConverter.Convert`.

## Was bewusst gleich bleibt / außerhalb des Scopes

- Kein Farbwähler am Tab-Header oder per Kontextmenü - nur im
  Connection-Editor.
- Keine Validierung/Warnung bei doppelt vergebenen Farben.
- `ServerColorPalette.AssignColor`s Vertrag/Algorithmus ändert sich nicht -
  sie bleibt reine Vorschlagslogik für Erstzuweisung *und* für den neuen
  "Zurücksetzen"-Button, kein struktureller Umbau.

## Tests

- **Kategorie A** (Erweiterung von `PersistenceServiceTests.cs`):
  Round-Trip-Test für `ThemePreference` (inkl. einer vorbereiteten
  `settings.json` ohne dieses Feld, um den Upgrade-Fall einer bestehenden
  Installation nachzustellen → erwarteter Default `"System"`).
- **Kategorie A:** bestehende `ServerColorPaletteTests.cs` bleiben
  unverändert gültig (Vertrag von `AssignColor` ändert sich nicht).
- Kein dedizierter Kategorie-C-Test für den `ColorPicker` selbst (externe
  Control, keine eigene Logik) - Sichtprüfung nach dem TestHarness-Prototyp
  reicht, gleiches Vorgehen wie bei anderen reinen Bindings in diesem
  Projekt.

## Nächster Schritt

Vor der eigentlichen Umsetzung in `AvaloniaApplication1`: den
`ui-feature-prototyp`-Skill nutzen, um den Theme-Button (Platzierung/Icon/
Zyklus-vs-Flyout) und den neuen Farbwähler-Bereich im Connection-Editor grob
im `TestHarness` nachzubauen und vom Entwickler live durchklicken zu lassen -
genau der Fall, für den dieser Skill laut `CLAUDE.md` gedacht ist. Dabei auch
die berechneten Light-Werte beider Paletten (Punkt 10 oben, siehe "Konkrete
Farbwerte") gegen den tatsächlich gerenderten Dark-Hintergrund gegenprüfen
und die `OtherSlotName`-Frage (Gold vs. AP's blasses Gelb) entscheiden.
