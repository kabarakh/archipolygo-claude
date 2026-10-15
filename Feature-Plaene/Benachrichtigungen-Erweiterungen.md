# Feature-Idee (grobe Skizze): Erweiterungen der Benachrichtigungen

**Status: 🗨️ Diskutieren** - Diese Datei ist kein Umsetzungsplan. Sie ist
eine grobe erste Skizze. Die Ideen bleiben aus `Benachrichtigungen.md` übrig
(im Feature-Plan-Archiv, umgesetzt 2026-09-29).

Die Grundlage ist schon da:
- `AttentionCategory` und `IAttentionTracker` sind die zentrale Stelle. An
  diese Stelle meldet jeder Auslöser.
- Der Settings-Dialog hat eine Tabelle mit den Kategorien.

Eine neue Art von Benachrichtigung ist darum im Kern eine weitere Spalte in
dieser Tabelle und ein weiterer Zweig in `AttentionTracker`.

Entscheide erst, wenn der Alltag mit echten Gruppen zeigt, was fehlt.

## Ideen

- **Toast-Benachrichtigungen** (Windows-Toast / `UNUserNotification` /
  libnotify) mit Inhalt ("Player X sent you Hookshot"). Diese Idee macht
  deutlich mehr Arbeit. Unter Windows braucht ein Toast eine AUMID an einer
  Verknüpfung. Prüfe, ob Velopack diese Verknüpfung schon anlegt. Die
  Tabelle der Kategorien bekommt dann eine dritte Spalte "Toast".
- **Sound** pro Kategorie, auch als weitere Spalte.
- **"Re-alert after N minutes"**: Heute blinkt ein Fenster erst wieder,
  wenn es einmal aktiv war (feste Regel in `AttentionTracker`). Wenn der
  Benutzer ein zweites Blinken nach längerer Zeit vermisst, kommt hier eine
  Einstellung dazu.
- **Linux-Badge** (Unity LauncherEntry über DBus): Der Entwickler lässt
  diese Idee mit Absicht weg. Der Badge braucht eine installierte
  `.desktop`-Datei, und das Release-Zip liefert diese Datei nicht mit.
  Avalonia bringt `Tmds.DBus.Protocol` schon mit. Die Idee ist nur
  interessant, wenn es einmal ein Linux-Paket mit `.desktop`-Datei gibt.

## Offene Fragen

- Welche dieser Lücken fällt im echten Spiel wirklich auf?
- Toasts: Gibt es sie nur für bestimmte Kategorien (zum Beispiel DeathLink,
  Erwähnung im Chat)? Oder kann der Benutzer sie frei wählen, wie Count und
  Blink?
- Prüfe vor der Umsetzung alle nativen APIs gegen die offizielle
  Dokumentation (Grundregel aus `CLAUDE.md`).
