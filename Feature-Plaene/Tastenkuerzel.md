# Feature-Idee (grobe Skizze): Tastenkürzel

**Status: 🗨️ Diskutieren** - Diese Datei ist kein Umsetzungsplan wie
`Tab-Reihenfolge.md`. Sie ist eine grobe erste Skizze. Die Idee kommt aus
der Datei `archipolygo_feature_ideas.md`. Diese Datei ist jetzt archiviert
("Keyboard shortcuts for common actions (next tab, focus chat input, …)").

## Offene Fragen vor einem echten Plan

- Welche Aktionen bekommen ein Tastenkürzel? Die Idee nennt "next tab" und
  "focus chat input" als Beispiele. Die echte Liste steht noch nicht fest.
  Sie kann auch andere Aktionen enthalten, zum Beispiel "!hint senden", "als
  anderen Slot chatten" oder "nächste/vorherige Gruppe".
- Avalonia oder das Betriebssystem belegen manche Tastenkombinationen schon.
  Zum Beispiel ist Strg+Tab für den Wechsel des Tabs in vielen Programmen
  üblich. Prüfe, ob Avalonia oder `TabControl` diese Kombination schon
  selbst abfängt.
- Kann der Benutzer die Tastenkürzel selbst einstellen? Oder reicht für die
  erste Version eine feste Belegung?
- Wo registriert die App die Tastenkürzel technisch? Es gibt zwei
  Möglichkeiten:
  - global auf `MainWindow` (`KeyBindings`/`InputBindings`),
  - pro Control, abhängig davon, welches Element den Fokus hat.

  Die zweite Möglichkeit ist zum Beispiel wichtig für "Chat-Eingabe
  fokussieren". Dieses Tastenkürzel ist nur nützlich, wenn der Fokus *nicht*
  schon auf der Chat-Eingabe ist.
