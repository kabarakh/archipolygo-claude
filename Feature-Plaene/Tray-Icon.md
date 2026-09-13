# Feature-Idee (grobe Skizze): Tray-Icon

**Status: 🗨️ Muss noch durchdiskutiert werden** - keine Umsetzungsplan im
Sinne von `Tab-Reihenfolge.md`, sondern eine grobe erste Skizze. Ursprünglich
aus der inzwischen archivierten Datei
`archipolygo_feature_ideas.md` ("Tray icon - keep
running minimized, with an unread-events badge") übernommen.

## Offene Kernfrage (2026-09-13 aufgeworfen)

Seit die Idee ["Taskbar-button / title-bar flash"](Fenster-Blinken.md) das
"Desktop notifications"-Bedürfnis abdeckt: hat ein Tray-Icon noch
eigenständigen Mehrwert, oder war es nur ein anderer Weg zum selben Ziel
("mich benachrichtigen")?

Einschätzung aus der Diskussion: Blinken und Tray-Badge lösen
unterschiedliche Bedürfnisse -

- **Blinken** ist ein momentaner Aufmerksamkeits-Trigger (reagiert auf ein
  konkretes Ereignis, hört irgendwann auf).
- **Tray-Badge** ist ein dauerhaft einsehbarer Zählerstand, den man jederzeit
  prüfen kann, ohne auf einen Trigger zu warten.
- Ein Tray-Icon erlaubt außerdem, komplett aus der Taskleiste zu
  verschwinden (ein anderer Wunsch als nur "minimiert") - dafür bräuchte es
  aber eine Begründung, warum das gewünscht ist (reines Aufräumen der
  Taskleiste? Immer-im-Hintergrund-laufen-Anwendungsfall?).

Mit Blinken als Lösung für "benachrichtige mich" bleibt vom
ursprünglichen Tray-Icon-Wunsch nur noch der schmalere Rest
"aus der Taskleiste verschwinden" übrig - real, aber eine deutlich
schwächere Begründung als ursprünglich angenommen. Muss bewusst entschieden
werden, nicht automatisch als weiterhin gebraucht vorausgesetzt werden.

## Falls doch gewünscht - grobe technische Eckpunkte

- Avalonia bietet `TrayIcon` (`Avalonia.Controls.TrayIcon`,
  `NativeMenu`) als plattformübergreifende API - müsste vor
  Umsetzungsbeginn gegen die exakt gepinnte Version 12.0.4 verifiziert
  werden (siehe CLAUDE.md-Grundregel: offizielle Doku zur gepinnten
  Version, nicht decompilierte DLL, nicht `main`-Branch-Doku).
- Unread-Badge auf dem Tray-Icon selbst ist plattformabhängig unterschiedlich
  gut unterstützt (Windows: eigenes Overlay-Icon nötig; macOS/Linux:
  uneinheitlich) - vermutlich eher über Icon-Wechsel (normal/mit Punkt) als
  über echten Zahlen-Badge lösbar.
- Würde "App minimiert in den Tray statt in die Taskleiste" als neues
  Verhalten einführen - Interaktion mit dem normalen Minimieren/Schließen
  müsste geklärt werden (Schließen-Button minimiert in den Tray statt die
  App zu beenden? Das ist ein eigenständiger UX-Entscheid, der vorab
  geklärt gehört).
