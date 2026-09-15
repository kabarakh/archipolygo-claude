# Feature-Idee (grobe Skizze): Taskbar-Button / Titelleiste blinken lassen

**Status: 🗨️ Muss noch durchdiskutiert werden** - noch kein Umsetzungsplan
im Sinne von `Tab-Reihenfolge.md`, sondern eine grobe erste Skizze mit
bereits erledigter API-Recherche. Ersetzt die ursprüngliche Idee
"Desktop notifications" (Toast-Popups) aus der inzwischen archivierten
Datei `archipolygo_feature_ideas.md` - Nutzerpräferenz
(2026-09-13): keine Toast-Benachrichtigungen, stattdessen ein
Taskbar-Button-/Titelleisten-Blinken ("Skype ruft an"-artig), z. B. wenn ein
Hint eintrifft oder ein wichtiges Item empfangen wird, während die App im
Hintergrund läuft.

## API-Recherche (2026-09-13, gegen die gepinnte Avalonia-Version 12.0.4)

Kein eingebautes Avalonia-API dafür - geprüft gegen die offizielle Doku zur
exakt gepinnten Version, nicht gegen eine decompilierte DLL (siehe
CLAUDE.md-Grundregel zu NuGet-Paketen). Weder `Window`/`WindowBase`
(`Activate()`, `ShowActivated`, `IsActive`, `Topmost`, `ShowInTaskbar` sind
die einzigen aktivierungs-/aufmerksamkeitsnahen Member) noch `IWindowImpl`
bieten eine Flash-/Urgency-/RequestAttention-Methode. Es bräuchte native
Aufrufe pro OS über den nativen Fenster-Handle (`TryGetPlatformHandle()`):

- **Windows**: `FlashWindowEx` (`user32.dll`) via P/Invoke - flasht den
  Taskleisten-Button und/oder die Titelleiste, bis das Fenster den Fokus
  bekommt.
- **macOS**: kein Titelleisten-Blinken-Konzept (kein Per-Fenster-Titelleisten-
  Chrome in dem Sinne) - das nächstliegende Äquivalent ist das
  Dock-Icon-Hüpfen via `NSApplication.requestUserAttention` (AppKit-Interop).
- **Linux**: kein einheitlicher Mechanismus - der ICCCM/X11-"Urgency Hint"
  (`_NET_WM_STATE_DEMANDS_ATTENTION`) ist der Standardweg, um zu *fragen*,
  aber der Window-Manager entscheidet, was tatsächlich passiert
  (Taskleisten-Blinken, Titelleisten-Hervorhebung oder gar nichts);
  Wayland-Unterstützung ist uneinheitlich je nach Compositor.

Das hat dieselbe Form wie die (inzwischen umgesetzte) Passwort-Idee (siehe
[`Passwort-Speicherung.md`](Archiv/Passwort-Speicherung.md)): eine kleine
`IWindowAttentionService`-artige Abstraktion mit drei
Plattform-Implementierungen, kein einzelner Cross-Platform-Call.

Quellen: [`IWindowImpl` Referenz](https://reference.avaloniaui.net/api/Avalonia.Platform/IWindowImpl/),
[`Window`-Klassen-Doku](https://api-docs.avaloniaui.net/docs/T_Avalonia_Controls_Window)

## Offene Fragen vor einem echten Plan

- Wann genau soll geblinkt werden - jedes neue Event, oder nur bestimmte
  Kategorien (Hint erhalten, wichtiges/progression Item, DeathLink)? Bei
  jedem Chat-Event zu blinken wäre vermutlich zu aufdringlich.
- Wie/wann hört das Blinken auf - beim Fokussieren des Fensters (naheliegend,
  matcht das native Verhalten von `FlashWindowEx` u. ä.), oder braucht es
  eine explizite eigene Bedingung?
- **Cross-Referenz zu [`Tab-Eigenes-Fenster.md`](Tab-Eigenes-Fenster.md)**:
  sobald Tabs in eigenen Fenstern leben können, funktioniert das Blinken
  weiterhin, muss aber auflösen, *welches* `Window` gerade die betroffene
  Gruppe zeigt, statt pauschal "das" (einzige) App-Fenster zu blinken - der
  Trigger und das aktuell fokussierte Fenster können dann auseinanderfallen.
  Welches der beiden Features zuerst gebaut wird, sollte diese
  Auflösungslogik von vornherein mitdenken, damit das andere Feature sie
  nicht nachträglich umbauen muss.
