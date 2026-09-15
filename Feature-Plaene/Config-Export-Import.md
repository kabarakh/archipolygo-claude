# Feature-Idee (grobe Skizze): Config-Export/Import

**Status: 🗨️ Muss noch durchdiskutiert werden** - keine Umsetzungsplan im
Sinne von `Tab-Reihenfolge.md`, sondern eine grobe erste Skizze. Ursprünglich
aus der inzwischen archivierten Datei
`archipolygo_feature_ideas.md` ("Config export/import
- back up and restore the server/slot list (`groups.json`) when moving to
another machine") übernommen.

## Ausgangslage

`PersistenceService` schreibt die Server-/Slot-Liste bereits als
`groups.json` unter `%AppData%/Archipolygo` - ein manuelles Kopieren dieser
Datei auf einen anderen Rechner funktioniert also im Prinzip schon heute,
nur ohne Nutzerführung (Datei suchen/finden, kein Export/Import-Dialog in
der App selbst).

## Offene Fragen vor einem echten Plan

- **["Don't store passwords at all"](Archiv/Passwort-Speicherung.md) ist
  inzwischen umgesetzt** (2026-09-15) - `groups.json` enthält seitdem gar
  keine Passwörter mehr, nur noch ein `RequiresPassword`-Flag pro Slot. Ein
  Export ist dadurch bereits "sicher" genug zum Teilen/Sichern, ohne eigene
  Verschlüsselung; Export/Import muss sich also nicht mehr mit
  Klartext-Passwörtern in der exportierten Datei auseinandersetzen.
- Ist "Export" einfach ein Kopieren/Anzeigen des `groups.json`-Pfads (damit
  Nutzer die Datei selbst finden/sichern), oder ein echter
  Save-As-Dialog innerhalb der App?
- Import: was passiert bei Konflikten mit bereits konfigurierten Gruppen
  (gleicher Host/Port, aber andere Slots)? Komplett ersetzen, zusammenführen,
  oder Nutzer pro Konflikt entscheiden lassen?
- Versionierung: falls sich das `groups.json`-Format künftig ändert
  (`PersistenceService` hat bereits eine Migration von einem älteren
  Flat-Profile-Format, `MigrateLegacyProfilesIfNeeded`) - müsste ein
  importierter älterer Export durch dieselbe Migration laufen.
