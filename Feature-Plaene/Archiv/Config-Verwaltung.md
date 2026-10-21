# Feature: Config-Verwaltung (Export/Import + Portable-Modus)

## Status: ✅ Umgesetzt (2026-10-21)

Die App setzt den Plan um. Der Rest des Dokuments ist der Plan vom
2026-10-20. In diesen Punkten weicht die Umsetzung vom Plan ab oder geht
über ihn hinaus:

- **Überschreiben an Ort und Stelle.** Die App ersetzt eine überschriebene
  Gruppe nicht durch ein neues `GroupViewModel`. Sie ändert die vorhandene
  Gruppe. Darum bleiben Tab, Position, ein abgelöstes Fenster und der
  Verlauf der behaltenen Slots erhalten.
- **Eigene Partial-Datei.** Die Übernahme liegt in
  `MainWindowViewModel.ConfigImport.cs`, nicht in
  `MainWindowViewModel.Servers.cs`.
- **Streams statt Pfade.** Die Dateidialoge von Avalonia liefern laut Doku
  nur Streams. Darum hat `ConfigExportService` zusätzlich `ExportToJson`
  und `ImportFromJson`.
- **Kollidierende Ids.** Wenn eine importierte Id schon zu einer fremden
  lokalen Gruppe oder einem fremden lokalen Slot gehört, bekommt sie eine
  neue Guid.
- **Passwörter im Speicher.** Ein überschriebener Slot behält ein Passwort,
  das der Benutzer in dieser Sitzung eingegeben hat.
- **"Import app settings" ist am Anfang abgewählt**, wie "Include app
  settings" beim Export.
- **Settings-Dialog nach dem Import.** Wenn ein Import auch Settings
  übernimmt, schließt die App den Settings-Dialog ohne Speichern. Sonst
  überschreibt "Save" die importierten Settings mit den alten Werten.
- **Fenster-X im Portable-Dialog** bedeutet "Quit". Die App ändert dann
  nichts auf der Festplatte.
- **Fehlgeschlagene Kopie beim ersten Start.** Die App löscht die halbe
  Kopie, zeigt den Fehler und beendet sich. Sonst ist der Ordner `data`
  nicht mehr leer, und der nächste Start bietet die Übernahme nicht mehr an.
- **Velopack-Portable-Zip geprüft.** Im Quellcode von Velopack 1.2.0 ist
  `IsInstalled` auch für das Portable-Zip von Velopack true. Darum
  ignoriert die App dort `portable.txt` richtig.
- **Nicht von Hand geprüft:** die nativen Dateidialoge, "Open data folder"
  und der Portable-Modus auf echtem Windows und Linux. Headless-Tests decken
  nur die View-Models und die Dialoge ohne native Teile ab.

## Ausgangslage

`PersistenceService` schreibt diese Daten unter `%AppData%/Archipolygo`:

- `groups.json` (Gruppen und eigene Slots)
- `sync-state/<slotId>.json`
- `settings.json`
- `datapackage-cache/<groupId>/`

`DiagnosticLogger` berechnet denselben AppData-Pfad noch einmal selbst. Die
App führt den Benutzer heute nicht beim Umzug. Er muss die Dateien selbst
finden und von Hand kopieren.

Das Datenverzeichnis ist schon austauschbar. `PersistenceService` hat einen
internen Konstruktor mit dem Verzeichnis als Parameter. Bisher nutzen nur
Tests diesen Konstruktor.

Seit [Passwort-Speicherung](Passwort-Speicherung.md) (umgesetzt
2026-09-15) enthält `groups.json` keine Passwörter mehr. Die Datei hat nur
ein `RequiresPassword`-Flag pro Slot. Darum ist eine Export-Datei
unbedenklich. Eine eigene Verschlüsselung ist nicht nötig.

## Teil 1: Export/Import

### Einstieg

Der Settings-Dialog (`Views/SettingsWindow.axaml`) bekommt drei Buttons:

- "Export servers..."
- "Import servers..."
- "Open data folder"

"Open data folder" öffnet den echten Datenordner. Der Button funktioniert im
normalen Modus und im Portable-Modus.

### Export

1. Der Benutzer klickt "Export servers...".
2. Ein Dialog listet alle Gruppen mit einer Checkbox pro Gruppe. Alle Gruppen
   sind am Anfang angewählt.
3. Der Dialog hat zusätzlich die Checkbox "Include app settings". Sie ist am
   Anfang abgewählt.
4. Der Benutzer klickt "Export...". Ein Save-As-Dialog fragt nach dem
   Dateinamen.
5. Die App schreibt eine einzelne JSON-Datei.

Die Export-Datei enthält diese Teile:

- Ein Feld für die Formatversion.
- Die angewählten Gruppen im selben Format wie `groups.json`, mit allen
  Feldern, auch `AutoConnect`.
- `settings.json`, nur wenn "Include app settings" angewählt ist.

Die Export-Datei enthält nicht `sync-state/`, `datapackage-cache/` und das
Diagnose-Log. Ohne `sync-state/` holt Catch-up den Verlauf auf dem neuen
Rechner neu. Der Entwickler akzeptiert, dass die App diesen Verlauf
vielleicht noch einmal als "neu" zeigt.

### Import

1. Der Benutzer klickt "Import servers...". Ein Open-Dialog fragt nach der
   Datei.
2. Die App liest die Datei. Für die Gruppen nutzt sie denselben Ladeweg wie
   `LoadGroups()`, mit derselben Migration.
3. Ein Dialog listet alle Gruppen aus der Datei mit einer Checkbox pro
   Gruppe. Alle Gruppen sind am Anfang angewählt.
4. Der Dialog markiert jede Gruppe, die es lokal schon gibt (gleicher Host
   und Port). Die Markierung sagt: Der Import überschreibt diese Gruppe.
5. Wenn die Datei Settings enthält, zeigt der Dialog die Checkbox "Import
   app settings".
6. Der Benutzer klickt "Import". Die App übernimmt nur die angewählten
   Gruppen.

**Vergleiche Host und Slot-Namen immer mit
`StringComparison.OrdinalIgnoreCase`.** Das ist die Regel im ganzen Projekt.

### Überschreiben einer vorhandenen Gruppe

Wenn eine importierte Gruppe eine lokale Gruppe überschreibt, gelten diese
Regeln:

- Die Gruppe behält ihre lokale `Id`. Darum bleibt ihr `datapackage-cache/`
  gültig.
- Die App ordnet Slots über den Slot-Namen zu. Ein Slot mit gleichem Namen
  behält seine lokale `Id`. Darum bleibt sein lokaler sync-state gültig. Alle
  anderen Daten des Slots kommen aus dem Import.
- Ein Slot, den es nur lokal gibt, fällt weg. Die App löscht dann auch seine
  Datei in `sync-state/`.
- Ein Slot, den es nur im Import gibt, kommt mit seiner `Id` aus der Datei
  dazu.
- `PreferredLeaderSlotId` zeigt nach dem Zuordnen auf die lokale `Id` des
  passenden Slots.

**Wenn die lokale Gruppe gerade verbunden ist, trennt die App sie vor dem
Überschreiben.** Danach ersetzt die App die Konfiguration. Dann verbindet die
App die Gruppe mit der neuen Konfiguration wieder. Nutze dafür
`DisconnectGroupAsync` und `SwitchLeaderAsync` aus `ConnectionManager`. Lege
keinen eigenen Verbindungsweg an.

### AutoConnect nach dem Import

Der Import übernimmt `AutoConnect` aus der Datei. Wenn `AutoConnect` an ist,
verbindet die App die Gruppe sofort nach dem Import. Mehrere Gruppen
verbindet die App nacheinander, mit demselben `StartupGroupSpacing` wie beim
App-Start (`MainWindowViewModel.InitializeGroupsAsync`). So bekommt der
Server keinen Schwall von Logins. Eine Gruppe mit `AutoConnect` aus bleibt
offline.

### Settings importieren

Wenn der Benutzer "Import app settings" anwählt, überschreiben die Settings
aus der Datei die lokalen Settings. Die App wendet sie sofort an, zum
Beispiel das Theme.

## Teil 2: Portable-Modus

Der Portable-Modus legt alle Daten neben die ausführbare Datei. Dann heißt
Umziehen nur noch "Ordner kopieren". Benutzer des Installers brauchen
weiter Export und Import aus Teil 1.

### Wann der Modus gilt

**Der Portable-Modus gilt nur, wenn alle drei Bedingungen erfüllt sind:**

- Neben der ausführbaren Datei liegt eine Datei `portable.txt`. Ihr Inhalt
  ist egal.
- `IUpdateService.IsManagedInstall` ist false. Velopack tauscht bei jedem
  Update den App-Ordner aus, und die Daten wären danach weg.
- Das Betriebssystem ist Windows oder Linux.

Wenn eine Bedingung fehlt, nutzt die App AppData wie bisher. Die App schaltet
den Modus nie automatisch ein. Sonst "verlieren" vorhandene Benutzer der
Zip-Version ohne Hinweis ihre Gruppen.

macOS ist in der ersten Version ausgenommen. Gatekeeper und App Translocation
können den Ort "neben der App" unzuverlässig machen. Prüfe das später auf
echter Mac-Hardware, wie beim Auto-Update.

Wenn eine Velopack-Installation eine `portable.txt` hat, ignoriert die App
den Marker. Der Settings-Dialog zeigt dann einen Hinweis.

### Datenordner

Im Portable-Modus liegen alle Daten im Ordner `data` neben der ausführbaren
Datei: `groups.json`, `sync-state/`, `settings.json`, `datapackage-cache/`
und das Diagnose-Log.

**Berechne den Datenordner an genau einer Stelle beim Start.**
`PersistenceService` und `DiagnosticLogger` bekommen den Pfad von dort. Heute
berechnen beide den AppData-Pfad selbst. Ein zweiter Ort für die Berechnung
erzeugt genau die Verwirrung, die der Modus beheben muss.

### Fehlende Schreibrechte

**Die App darf nie still auf AppData ausweichen.** Wenn der Marker gilt und
die App nicht in den Ordner `data` schreiben kann, zeigt sie einen Dialog.
Der Dialog nennt den Pfad und sagt, dass der Ordner nicht beschreibbar ist.
Danach beendet sich die App. Ein typischer Fall ist eine entpackte
Zip-Version unter `C:\Program Files`.

### Erster Start

Wenn der Ordner `data` leer ist und in AppData Daten liegen, zeigt die App
beim Start einen Dialog mit drei Möglichkeiten:

- "Copy data from AppData": Die App kopiert die Daten. AppData bleibt
  unverändert.
- "Move data from AppData": Die App kopiert die Daten und löscht sie danach
  in AppData.
- "Start empty": Die App ändert nichts.

Bei "Move data from AppData" löscht die App die Daten in AppData erst nach
einer erfolgreichen Kopie. Wenn die Kopie fehlschlägt, bleibt AppData
unverändert.

### Sichtbarkeit

Der Settings-Dialog zeigt die Zeile "Portable mode: data stored in <path>".
Die Zeile steht neben dem Hinweis zur unverwalteten Installation. Der Button
"Open data folder" steht direkt daneben. Der Fenstertitel ändert sich nicht.

## Arbeitspakete

**Zwei Pakete in derselben Welle dürfen nicht dieselbe Datei ändern.** Darum
gehört jede Datei unten zu genau einem Paket. Die Pakete einer Welle können
parallel laufen. Eine Welle beginnt erst, wenn die vorherige Welle fertig
ist. Jedes Paket bringt seine eigenen Tests aus dem Abschnitt "Tests" mit.

### Welle 1

**AP1: Datenordner berechnen.**

- Ergänze eine neue Klasse, zum Beispiel `Services/DataDirectoryResolver.cs`.
  Sie berechnet den Datenordner aus Marker, `IsManagedInstall` und
  Betriebssystem.
- Ändere in `App.axaml.cs` die Registrierung von `PersistenceService` und
  `DiagnosticLogger`. Beide bekommen den Pfad über ihren internen
  Konstruktor.
- Ändere `PersistenceService.cs` und `DiagnosticLogger.cs` nicht. AP2 ändert
  `PersistenceService.cs`.
- Fertig: Tests für jede Kombination der drei Bedingungen. Ohne
  `portable.txt` verhält sich die App wie bisher.

**AP2: Export-Format und Persistenz.**

- Ergänze eine neue Klasse, zum Beispiel `Services/ConfigExportService.cs`.
  Sie schreibt und liest die Export-Datei mit Formatversion.
- Ziehe in `PersistenceService.cs` das Laden der Gruppen aus einem JSON-Text
  in eine eigene Methode. `LoadGroups()` und der Import nutzen dieselbe
  Methode und dieselbe Migration.
- Ergänze in `IPersistenceService` und `PersistenceService` eine Methode, die
  die sync-state-Datei eines Slots löscht.
- Fertig: Round-Trip-Test und Migrationstest.

**AP3: Zuordnung beim Import.**

- Ergänze eine reine Logik-Klasse ohne UI und ohne Verbindung, zum Beispiel
  `Services/ConfigImportMerger.cs`.
- Eingabe: lokale Gruppen und importierte Gruppen. Ausgabe: neue Gruppen,
  überschriebene Gruppen und die Slot-`Id`s, deren sync-state wegfällt.
- Die Klasse setzt alle Regeln aus "Überschreiben einer vorhandenen Gruppe"
  um, auch für `PreferredLeaderSlotId`.
- Fertig: Plain-`[Fact]`-Tests für alle Regeln.

### Welle 2

**AP4: Export-Dialog** (braucht AP2).

- Ergänze ein neues View-Model und eine neue View für den Export-Dialog.
- Fertig: `[AvaloniaFact]`-Test für Liste, Checkboxen und
  "Include app settings".

**AP5: Import-Dialog und Übernahme** (braucht AP2 und AP3).

- Ergänze ein neues View-Model und eine neue View für den Import-Dialog.
- Ergänze in `MainWindowViewModel.Servers.cs` die Übernahme: trennen,
  ersetzen, wieder verbinden, sync-state löschen, Settings anwenden.
- Verbinde Gruppen mit `AutoConnect` nacheinander mit `StartupGroupSpacing`.
- Fertig: `[AvaloniaFact]`-Tests für den Dialog und für eine verbundene
  Gruppe mit `FakeSessionFactory`.

**AP6: Portable-Start** (braucht AP1).

- Ergänze in `App.axaml.cs` die Prüfung der Schreibrechte mit Fehlerdialog
  und Beenden.
- Ergänze den Dialog für den ersten Start (kopieren, verschieben, leer
  starten).
- Fertig: Tests für Kopieren und Verschieben mit Temp-Verzeichnissen. Bei
  einer fehlgeschlagenen Kopie bleibt AppData unverändert.

### Welle 3

**AP7: Settings-Dialog** (braucht AP1, AP4 und AP5).

- Ergänze in `SettingsWindow.axaml` und `SettingsViewModel.cs` die Buttons
  "Export servers...", "Import servers..." und "Open data folder".
- Ergänze die Zeile für den Portable-Modus und den Hinweis für eine
  Velopack-Installation mit `portable.txt`.
- Fertig: `[AvaloniaFact]`-Test für die Sichtbarkeit der Zeile und der
  Buttons.

### Welle 4

**AP8: Dokumentation und Abschluss** (braucht alle Pakete).

- Ergänze `README.md` (englisch) um Export, Import und Portable-Modus.
- Ergänze `CLAUDE.md` und `Umsetzungsplan.md`, wenn nötig.
- Lege `portable.txt` nicht in das Release-Zip. Der Benutzer legt die Datei
  selbst an.
- Führe `dotnet test AvaloniaApplication1.sln` aus.
- Verschiebe diese Datei ins Feature-Plan-Archiv mit dem Status
  "✅ Umgesetzt".

## Tests

- Plain `[Fact]`: Round-Trip von Export und Import über den internen
  Konstruktor von `PersistenceService` mit einem Temp-Verzeichnis.
- Plain `[Fact]`: Zuordnung beim Überschreiben (gleicher Slot-Name behält
  lokale `Id`, Slot nur lokal fällt weg, `PreferredLeaderSlotId` zeigt auf
  die lokale `Id`).
- Plain `[Fact]`: Import einer Datei mit älterem Format läuft durch die
  Migration.
- Plain `[Fact]`: Die Berechnung des Datenordners für jede Kombination aus
  Marker, `IsManagedInstall` und Betriebssystem.
- `[AvaloniaFact]` mit `FakeSessionFactory`: Import überschreibt eine
  verbundene Gruppe. Die App trennt sie und verbindet sie wieder.
- `[AvaloniaFact]` mit den echten Views: Export- und Import-Dialog zeigen
  die Gruppen, die Markierung für vorhandene Gruppen und die
  Settings-Checkbox.
