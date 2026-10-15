# Feature-Idee (grobe Skizze): Config-Verwaltung (Export/Import + Portable-Modus)

**Status: 🗨️ Diskutieren** - Diese Datei ist kein Umsetzungsplan wie
`Tab-Reihenfolge.md`. Sie ist eine grobe erste Skizze. Die Idee kommt aus der
Datei `archipolygo_feature_ideas.md`, die jetzt im Archiv liegt ("Config
export/import - back up and restore the server/slot list (`groups.json`) when
moving to another machine"). Damals hieß diese Datei `Config-Export-Import.md`.
Am 2026-10-02 kam ein Portable-Modus für die Zip-Version dazu, und die Datei
bekam ihren neuen Namen. Der Grund: Beide Teile lösen dasselbe Problem. Der
Benutzer sagt: "Meine Konfiguration liegt an einem Ort, den ich nicht sehe,
und sie kommt nicht mit auf einen anderen Rechner." Darum plane beide Teile
zusammen, nicht als zwei getrennte Features.

## Ausgangslage

`PersistenceService` schreibt die Liste der Gruppen und Slots schon als
`groups.json` unter `%AppData%/Archipolygo`. Der Benutzer kann diese Datei
also heute schon von Hand auf einen anderen Rechner kopieren. Aber die App
führt ihn dabei nicht. Er muss die Datei selbst finden, und die App hat
keinen Dialog für Export oder Import. Neben `groups.json` liegen
`sync-state/<slotId>.json`, `settings.json` und das Diagnose-Log.

Das Datenverzeichnis ist schon austauschbar: `PersistenceService` hat einen internen Konstruktor mit dem Verzeichnis als Parameter. Bisher
nutzen nur Tests diesen Konstruktor. So fasst ein Round-Trip-Test nie die
echten Daten des Benutzers an. Ein anderer Speicherort ist also nur eine
Entscheidung beim Start, kein Umbau.

Seit [Passwort-Speicherung](Archiv/Passwort-Speicherung.md) (umgesetzt
2026-09-15) enthält `groups.json` keine Passwörter mehr. Die Datei hat nur
noch ein `RequiresPassword`-Flag pro Slot. Darum sind ein Export und ein
Datenordner auf einem USB-Stick unbedenklich. Eine eigene Verschlüsselung ist
nicht nötig.

## Teil 1: Export/Import

### Offene Fragen

- Was ist "Export"? Variante 1: Die App kopiert oder zeigt den Pfad von
  `groups.json`. Dann findet und sichert der Benutzer die Datei selbst.
  Variante 2: Die App zeigt einen echten Save-As-Dialog. Ein einfacher
  Button "Open data folder" in den Settings ist ein günstiger erster Schritt.
  Er funktioniert in beiden Modi (normal und portable, siehe Teil 2). Allein
  deckt er schon einen Teil des Bedarfs ab.
- Import: Was passiert bei einem Konflikt mit einer konfigurierten Gruppe
  (gleicher Host und Port, aber andere Slots)? Möglichkeiten: Die App ersetzt
  die Gruppe ganz, oder sie führt beide zusammen. Oder der Benutzer
  entscheidet bei jedem Konflikt.
- Versionierung: Das Format von `groups.json` kann sich in Zukunft ändern.
  Dann muss ein importierter älterer Export durch dieselbe Migration laufen.
  `PersistenceService` hat schon eine Migration von einem älteren
  Flat-Profile-Format (`MigrateLegacyProfilesIfNeeded`).
- Gehört `sync-state/` in einen Export? Ohne `sync-state/` holt der neue
  Rechner beim ersten Connect den ganzen Verlauf mit Catch-up neu. Das
  funktioniert. Aber die App zeigt dann vielleicht alles noch einmal als
  "neu".

## Teil 2: Portable-Modus (nur Zip-Version)

Die Zip-Version ist schon "auspacken und starten". Trotzdem schreibt sie ihre
Daten nach `%AppData%/Archipolygo`. Ein Portable-Modus legt die Daten neben
die ausführbare Datei. Dann heißt Umziehen nur noch "Ordner kopieren". Das
löst den Hauptfall von Teil 1 (Rechnerwechsel) zum großen Teil. Aber es
ersetzt Teil 1 nicht. Benutzer des Installers brauchen weiter Export und
Import. Auch für das Zusammenführen mit konfigurierten Gruppen ist Export und
Import nötig.

### Grobe Idee

- **Opt-in mit einem Marker neben der EXE** (zum Beispiel ein leerer Ordner
  `portable` oder eine Datei `portable.txt`). Die App schaltet den Modus
  nicht automatisch für jede Zip-Version ein. Der Grund: Vorhandene Benutzer
  der Zip-Version haben ihre Daten schon in AppData. Bei einem
  automatischen Wechsel ab Version X "verlieren" sie ohne Hinweis alle
  Gruppen. Wenn der
  Marker da ist, liegen alle Daten darunter (`groups.json`, `sync-state/`,
  `settings.json`, Diagnose-Log). Wenn der Marker fehlt, bleibt alles wie
  bisher.
- Optional: Der erste Start im Portable-Modus kann anbieten, die Daten aus
  AppData zu übernehmen. Das gilt, wenn der portable Datenordner leer ist und
  in AppData Daten liegen. Die App kopiert die Daten, sie verschiebt sie
  nicht.

### Offene Fragen

- **Velopack-Installationen ausnehmen.** `vpk pack` erzeugt zusätzlich ein
  eigenes "portable .zip" (siehe `release.yml`). Velopack tauscht bei jedem
  Update den App-Ordner aus. Daten in diesem Ordner sind also nach dem
  nächsten Update weg. Gilt der Portable-Modus also nur, wenn
  `IsManagedInstall` false ist? Oder behandelt die App das Portable-Zip von
  Velopack so, dass die Daten eine Ebene über dem ausgetauschten Ordner
  liegen? Lies vorher in der Velopack-Doku für die referenzierte Version
  nach, wie das Layout genau aussieht. Rate nicht.
- **Schreibrechte.** Wenn die entpackte Zip-Version zum Beispiel unter
  `C:\Program Files` liegt, kann die App in diesen Ordner nicht schreiben.
  Dann muss die App einen klaren Hinweis zeigen. Sie darf nicht still auf
  AppData ausweichen. Ein stilles Ausweichen erzeugt genau die Verwirrung,
  die der Modus beheben will.
- **macOS.** Gatekeeper und Quarantäne bei heruntergeladenen Dateien können
  beeinflussen, wo die App wirklich läuft. Sie können auch beeinflussen, ob
  die App neben sich schreiben darf. Teste das auf echter Mac-Hardware.
  Nimm es nicht einfach an.
- **Sichtbarkeit.** Muss die App irgendwo zeigen, dass sie im Portable-Modus
  läuft? Ein möglicher Ort ist der Settings-Dialog, neben dem vorhandenen
  Hinweis zur unverwalteten Installation. Spätestens der Button "Open data
  folder" aus Teil 1 zeigt den echten Pfad sowieso.
