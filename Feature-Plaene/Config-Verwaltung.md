# Feature-Idee (grobe Skizze): Config-Verwaltung (Export/Import + Portable-Modus)

**Status: 🗨️ Muss noch durchdiskutiert werden** - keine Umsetzungsplan im
Sinne von `Tab-Reihenfolge.md`, sondern eine grobe erste Skizze. Ursprünglich
aus der inzwischen archivierten Datei
`archipolygo_feature_ideas.md` ("Config export/import
- back up and restore the server/slot list (`groups.json`) when moving to
another machine") übernommen, damals noch als `Config-Export-Import.md`.
Am 2026-10-02 um einen Portable-Modus für die Zip-Version erweitert und
umbenannt: beide Teile lösen dasselbe Grundproblem ("meine Konfiguration
liegt irgendwo, wo ich sie nicht sehe, und kommt nicht mit auf einen
anderen Rechner") und sollten zusammen geplant werden statt als zwei
unabhängige Features.

## Ausgangslage

`PersistenceService` schreibt die Server-/Slot-Liste bereits als
`groups.json` unter `%AppData%/Archipolygo` - ein manuelles Kopieren dieser
Datei auf einen anderen Rechner funktioniert also im Prinzip schon heute,
nur ohne Nutzerführung (Datei suchen/finden, kein Export/Import-Dialog in
der App selbst). Daneben liegen `sync-state/<slotId>.json`, `settings.json`
und das Diagnose-Log.

Das Datenverzeichnis ist bereits austauschbar: `PersistenceService` hat
einen internen Konstruktor, der das Verzeichnis als Parameter nimmt (bisher
nur für Tests genutzt, damit ein Round-Trip-Test nie die echten Daten des
Nutzers anfasst). Ein anderer Speicherort ist also im Kern nur eine
Entscheidung beim Start, kein Umbau.

Seit [Passwort-Speicherung](Archiv/Passwort-Speicherung.md) (umgesetzt
2026-09-15) enthält `groups.json` keine Passwörter mehr, nur noch ein
`RequiresPassword`-Flag pro Slot. Sowohl ein Export als auch ein
Datenordner auf einem USB-Stick sind dadurch unbedenklich, ohne eigene
Verschlüsselung.

## Teil 1: Export/Import

### Offene Fragen

- Ist "Export" einfach ein Kopieren/Anzeigen des `groups.json`-Pfads (damit
  Nutzer die Datei selbst finden/sichern), oder ein echter
  Save-As-Dialog innerhalb der App? Ein schlichter Button "Open data
  folder" in den Settings wäre ein günstiger erster Schritt, der in beiden
  Modi (normal/portable, siehe Teil 2) funktioniert und einen Teil des
  Bedarfs schon allein abdeckt.
- Import: was passiert bei Konflikten mit bereits konfigurierten Gruppen
  (gleicher Host/Port, aber andere Slots)? Komplett ersetzen, zusammenführen,
  oder Nutzer pro Konflikt entscheiden lassen?
- Versionierung: falls sich das `groups.json`-Format künftig ändert
  (`PersistenceService` hat bereits eine Migration von einem älteren
  Flat-Profile-Format, `MigrateLegacyProfilesIfNeeded`) - müsste ein
  importierter älterer Export durch dieselbe Migration laufen.
- Gehört `sync-state/` mit in einen Export? Ohne ihn holt der neue Rechner
  beim ersten Connect per Catch-up einfach den ganzen Verlauf neu - das
  funktioniert, zeigt aber u. U. alles noch einmal als "neu" an.

## Teil 2: Portable-Modus (nur Zip-Version)

Die Zip-Version ist bereits "auspacken und starten", schreibt ihre Daten
aber trotzdem nach `%AppData%/Archipolygo`. Ein Portable-Modus legt sie
stattdessen neben die ausführbare Datei - umziehen heißt dann nur noch
"Ordner kopieren". Das entschärft den Hauptanwendungsfall von Teil 1
(Rechnerwechsel), ersetzt ihn aber nicht: Installer-Nutzer und das
Zusammenführen mit bereits konfigurierten Gruppen brauchen weiterhin
Export/Import.

### Grobe Idee

- **Opt-in per Marker neben der EXE** (z. B. ein leerer `portable`-Ordner
  oder eine `portable.txt`), nicht automatisch für jede Zip-Version.
  Bestehende Zip-Nutzer haben ihre Daten schon in AppData - ein
  automatischer Wechsel ab Version X würde für sie stillschweigend alle
  Server "verlieren". Ist der Marker da, liegt alles darunter
  (`groups.json`, `sync-state/`, `settings.json`, Diagnose-Log); fehlt er,
  bleibt alles wie bisher.
- Optional: beim ersten Start im Portable-Modus, wenn der portable
  Datenordner leer ist, aber AppData-Daten existieren, anbieten, diese zu
  übernehmen (kopieren, nicht verschieben).

### Offene Fragen

- **Velopack-Installationen ausnehmen.** `vpk pack` erzeugt zusätzlich ein
  eigenes "portable .zip" (siehe `release.yml`), und Velopack tauscht bei
  jedem Update den App-Ordner aus - Daten darin wären nach dem nächsten
  Update weg. Portable-Modus also nur, wenn `IsManagedInstall` false ist?
  Oder Velopacks eigenes Portable-Zip so behandeln, dass die Daten eine
  Ebene über dem ausgetauschten Ordner landen? (Vorher in Velopacks
  Doku für die referenzierte Version nachlesen, wie das Layout genau
  aussieht - nicht raten.)
- **Schreibrechte.** Liegt die entpackte Zip-Version z. B. unter
  `C:\Program Files`, ist der Ordner nicht beschreibbar. Klarer Hinweis
  statt stillem Fallback auf AppData (ein stiller Fallback würde genau die
  Verwirrung erzeugen, die der Modus beheben soll).
- **macOS.** Gatekeeper/Quarantäne-Verhalten bei heruntergeladenen Dateien
  könnte beeinflussen, wo die App tatsächlich läuft und ob sie neben sich
  schreiben darf - auf echter Mac-Hardware testen, nicht annehmen.
- **Sichtbarkeit.** Soll die App irgendwo anzeigen, dass sie im
  Portable-Modus läuft (Settings-Dialog, neben dem bestehenden Hinweis zur
  unverwalteten Installation)? Spätestens der "Open data folder"-Button aus
  Teil 1 macht den tatsächlichen Pfad ohnehin sichtbar.
