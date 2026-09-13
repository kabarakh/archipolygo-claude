# Feature-Idee (grobe Skizze): Log-Export

**Status: 🗨️ Muss noch durchdiskutiert werden** - keine Umsetzungsplan im
Sinne von `Tab-Reihenfolge.md`, sondern eine grobe erste Skizze. Ursprünglich
aus der inzwischen archivierten Datei
`archipolygo_feature_ideas.md` ("Log export - write
events/hints/chat out as text or CSV, e.g. to share in Discord") übernommen.

## Ausgangslage

Events/Hints/Chat liegen pro Gruppe in `GroupViewModel`s Collections
(gespeist über `MessageHistoryService`/`HintService`) nur in-memory bzw. als
internes Sync-State-JSON (`sync-state/<slotId>.json`, siehe
`PersistenceService`) - kein nutzerfacing Export als Text/CSV vorhanden.

## Offene Fragen vor einem echten Plan

- Was genau wird exportiert - nur Events, nur Hints, nur Chat, oder alles
  zusammen? Vermutlich mit denselben Filtern wie in der UI schon vorhanden
  (Kategorie-/Slot-Filter), damit man z. B. nur die eigenen Hints exportieren
  kann.
- Format: reiner Text (für Discord-Copy-Paste, evtl. mit Discord-Markdown wie
  Codeblöcken) vs. CSV (für Tabellen-Auswertung) - vermutlich beides als
  Option, nicht nur eins.
- Scope: nur die aktuell sichtbare gefilterte Liste eines Tabs, oder auch ein
  gruppenübergreifender Export (analog zum Dashboard-Tab)?
- Wie weit zurück? Nur was gerade in der UI/im Speicher geladen ist, oder
  auch historische Daten, die beim Neustart schon aus dem Sync-State
  wiederhergestellt wurden?
- Wohin exportieren - Datei speichern (Avalonia `StorageProvider`/
  Save-File-Dialog) vs. direkt in die Zwischenablage kopieren? Für den
  genannten Anwendungsfall ("in Discord teilen") wäre eine
  Clipboard-Option vermutlich der naheliegendere erste Schritt als ein
  Datei-Dialog.
