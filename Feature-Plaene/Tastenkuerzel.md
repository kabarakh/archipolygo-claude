# Feature-Idee (grobe Skizze): Tastenkürzel

**Status: 🗨️ Muss noch durchdiskutiert werden** - keine Umsetzungsplan im
Sinne von `Tab-Reihenfolge.md`, sondern eine grobe erste Skizze. Ursprünglich
aus der inzwischen archivierten Datei
`archipolygo_feature_ideas.md` ("Keyboard shortcuts
for common actions (next tab, focus chat input, …)") übernommen.

## Offene Fragen vor einem echten Plan

- Welche Aktionen genau bekommen ein Tastenkürzel? Die Idee nennt "next tab"
  und "focus chat input" als Beispiele, aber die tatsächliche Liste (auch
  z. B. "!hint senden", "als anderen Slot chatten", "nächste/vorherige
  Gruppe") ist noch nicht festgelegt.
- Konflikte mit von Avalonia/dem OS bereits belegten Tastenkombinationen
  (z. B. Strg+Tab für Tab-Wechsel ist in vielen Programmen Konvention,
  müsste geprüft werden, ob Avalonia/`TabControl` das nicht schon selbst
  abfängt).
- Sollen Tastenkürzel konfigurierbar sein, oder reicht eine feste Belegung
  für die erste Version?
- Wo würden sie technisch registriert - global auf `MainWindow`
  (`KeyBindings`/`InputBindings`), oder pro Kontrolle, je nachdem welches
  Element gerade den Fokus hat (relevant z. B. für "Chat-Eingabe
  fokussieren", das nur sinnvoll ist, wenn der Fokus *nicht* schon dort
  liegt)?
