# Feature-Idee (grobe Skizze): Erweiterungen der Benachrichtigungen

**Status: 🗨️ Muss noch durchdiskutiert werden** - eine grobe erste Skizze,
kein Umsetzungsplan. Übrig geblieben aus `Benachrichtigungen.md` (im
Feature-Plan-Archiv, umgesetzt 2026-09-29). Die Grundlage steht bereits:
`AttentionCategory` und `IAttentionTracker` als zentrale Stelle, an die jeder
Auslöser meldet, dazu die Kategorie-Tabelle im Settings-Dialog. Eine neue Art
der Benachrichtigung wäre also im Kern eine weitere Spalte in dieser Tabelle
und ein weiterer Zweig in `AttentionTracker`.

Erst sinnvoll zu entscheiden, wenn sich im Alltag mit echten Welten zeigt,
was fehlt.

## Ideen

- **Toast-Benachrichtigungen** (Windows-Toast / `UNUserNotification` /
  libnotify) mit Inhalt ("Player X sent you Hookshot"). Deutlich mehr
  Aufwand. Unter Windows braucht es eine AUMID an einer Verknüpfung; prüfen,
  ob Velopack die schon anlegt. Die Kategorie-Tabelle bekäme eine dritte
  Spalte "Toast".
- **Sound** pro Kategorie, ebenfalls als weitere Spalte.
- **"Re-alert after N minutes"**: Heute blinkt ein Fenster nicht erneut,
  bis es einmal aktiv war (feste Regel in `AttentionTracker`). Falls man ein
  zweites Blinken nach längerer Zeit vermisst, käme hier eine Einstellung
  dazu.
- **Linux-Badge** (Unity LauncherEntry über DBus): bewusst weggelassen, weil
  er eine installierte `.desktop`-Datei bräuchte, die das Release-Zip nicht
  mitliefert. `Tmds.DBus.Protocol` bringt Avalonia bereits mit. Nur
  interessant, falls es einmal ein Linux-Paket mit `.desktop`-Datei gibt.

## Offene Fragen

- Welche dieser Lücken fällt im echten Spielbetrieb tatsächlich auf?
- Toasts: nur für bestimmte Kategorien (z. B. DeathLink, Chat-Erwähnung) oder
  frei wählbar wie Count/Blink?
- Alle nativen APIs vor der Umsetzung gegen die offizielle Doku prüfen
  (CLAUDE.md-Grundregel).
