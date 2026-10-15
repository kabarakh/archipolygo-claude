# Feature-Idee (grobe Skizze): Tray-Icon

**Status: 🗨️ Diskutieren** - Diese Datei ist kein Umsetzungsplan wie
`Tab-Reihenfolge.md`. Sie ist eine grobe erste Skizze. Die Idee kommt aus der
Datei `archipolygo_feature_ideas.md`, die jetzt im Archiv liegt ("Tray icon -
keep running minimized, with an unread-events badge").

## Nachtrag (2026-09-29)

Seit `Benachrichtigungen.md` (Feature-Plan-Archiv) zeigt die App immer einen
Zählerstand. Diese Orte zeigen ihn:

- der Fenstertitel,
- ein Badge am Tab,
- das Dashboard,
- ein Taskleisten-Overlay unter Windows,
- ein Dock-Badge unter macOS.

Damit ist der Teil "Unread-Badge" dieses Wunsches erledigt. Offen ist nur
noch "aus der Taskleiste verschwinden" (siehe unten).

## Offene Kernfrage (2026-09-13 aufgeworfen)

["Taskbar-button / title-bar flash"](Archiv/Tab-Eigenes-Fenster.md) deckt
das Bedürfnis "Desktop notifications" ab und ist schon umgesetzt. Hat ein
Tray-Icon trotzdem einen eigenen Nutzen? Oder ist es nur ein anderer Weg zum
selben Ziel ("mich benachrichtigen")?

Einschätzung aus der Diskussion: Blinken und Tray-Badge erfüllen
verschiedene Bedürfnisse.

- **Blinken** holt kurz die Aufmerksamkeit. Es reagiert auf ein konkretes
  Ereignis und hört irgendwann auf.
- **Tray-Badge** ist ein Zählerstand, den der Benutzer immer sehen kann. Er
  kann ihn jederzeit prüfen und muss nicht auf ein Ereignis warten.
- Mit einem Tray-Icon kann die App außerdem ganz aus der Taskleiste
  verschwinden. Das ist ein anderer Wunsch als nur "minimiert". Dafür ist
  aber ein Grund nötig. Will der Benutzer nur die Taskleiste aufräumen? Oder
  will er, dass die App immer im Hintergrund läuft?

Das Blinken erfüllt schon "benachrichtige mich". Vom ursprünglichen Wunsch
nach einem Tray-Icon bleibt also nur der kleinere Rest "aus der Taskleiste
verschwinden". Dieser Rest ist real. Aber sein Grund ist viel schwächer als
zuerst angenommen. **Der Entwickler muss bewusst entscheiden, ob das Feature
nötig ist.** Nimm nicht automatisch an, dass es weiter nötig ist.

## Falls doch gewünscht - grobe technische Eckpunkte

- Avalonia hat `TrayIcon` (`Avalonia.Controls.TrayIcon`, `NativeMenu`) als
  API für alle Plattformen. Prüfe das vor dem Start der Umsetzung gegen die
  genau gepinnte Version 12.0.4. Nimm dafür die offizielle Doku zur
  gepinnten Version (siehe Grundregel in CLAUDE.md). Nimm keine dekompilierte
  DLL und keine Doku zum `main`-Branch.
- Ein Unread-Badge auf dem Tray-Icon selbst funktioniert nicht auf jeder
  Plattform gleich gut. Windows braucht ein eigenes Overlay-Icon. Unter macOS
  und Linux ist die Unterstützung uneinheitlich. Wahrscheinlich ist ein
  Wechsel des Icons (normal oder mit Punkt) die bessere Lösung als ein echter
  Badge mit Zahl.
- Das Feature bringt ein neues Verhalten: Die App minimiert in den Tray statt
  in die Taskleiste. Kläre, wie das mit dem normalen Minimieren und Schließen
  zusammenspielt. Minimiert zum Beispiel der Schließen-Button die App in den
  Tray, statt sie zu beenden? Das ist eine eigene UX-Entscheidung. Kläre sie
  vorher.
