# Feature-Idee (grobe Skizze): Einstellbare Benachrichtigungen (Blinken, Badge, Zähler)

**Status: 🗨️ Muss noch durchdiskutiert werden** - keine Umsetzungsplan im
Sinne von `Tab-Reihenfolge.md`, sondern eine grobe erste Skizze. Entstanden
aus der Diskussion (2026-09-26) über das Fenster-Blinken aus
`Tab-Eigenes-Fenster.md` (Feature-Plan-Archiv): "welche Optionen gibt es
da noch? (Discord zeigt die Anzahl an und hört nach einiger Zeit auf zu
blinken)".

## Ausgangslage

- `IWindowAttentionService.RequestAttention(Guid groupId)` blinkt das für
  die Gruppe zuständige Fenster (Hauptfenster oder abgetrenntes
  `DetachedGroupWindow`), sofern es nicht `IsActive` ist. Auslöser sind die
  bestehenden Relevanz-Signale (Hint für einen eigenen Slot, DeathLink,
  Progression-Item).
- Seit 2026-09-26 mit fester Obergrenze: Windows blinkt 4-mal, danach bleibt
  der Taskleisten-Button hervorgehoben bis zur Aktivierung; macOS hüpft
  einmal (`NSInformationalRequest`); Linux setzt nur
  `_NET_WM_STATE_DEMANDS_ATTENTION`. Diese Werte sollen nutzerseitig
  einstellbar werden.

## Mögliche Bausteine

### 1. Blinken einstellbar

- Aus / N-mal (Default 4) / bis zum Fokus (altes Verhalten).
- Drosselung: kein erneutes Blinken, solange die vorherige Hervorhebung
  noch aktiv ist bzw. das letzte Blinken < X Sekunden her ist (sonst bei
  vielen Item-Sends in Folge nervig).
- macOS: einmal hüpfen (Informational) vs. dauerhaft (Critical) - "N-mal"
  gibt es dort nicht.

### 2. Zähler / Badge (Anzahl ungelesener relevanter Ereignisse)

- **Windows:** Overlay-Icon am Taskleisten-Button über
  `ITaskbarList3::SetOverlayIcon` (COM-Interop), Zahl selbst als
  16×16-Icon gerendert - dasselbe, was Discord für die rote Zahl nutzt.
- **macOS:** Dock-Badge über `NSApp.dockTile.badgeLabel` - gleiches
  objc-Runtime-Muster wie das bestehende `MacWindowAttention`.
- **Linux:** Unity-LauncherEntry-API über DBus
  (`com.canonical.Unity.LauncherEntry`, `count`/`count-visible`) - KDE,
  Dash-to-Dock, Plank; best-effort wie der restliche Linux-Pfad.
- **Plattformübergreifender Fallback:** Fenstertitel `(3) Archipolygo -
  ServerName` (Alt-Tab, Taskleisten-Tooltip). Trivial, überall wirksam.
- Alternative: `Window.Icon` dynamisch mit gerendertem Badge tauschen
  (ändert aber auch das Titelleisten-Icon).

### 3. Weitere Kanäle (eher später / optional)

- Toast-Benachrichtigungen (Windows-Toast / `UNUserNotification` /
  libnotify) mit Inhalt ("Player X sent you Hookshot"). Deutlich mehr
  Aufwand; unter Windows ist eine AUMID an einer Verknüpfung nötig - prüfen,
  ob Velopack die schon anlegt.
- Sound, optional pro Kategorie.
- Tray-Icon-Badge - siehe `Tray-Icon.md`, dessen offene Kernfrage sich mit
  Baustein 2 teilweise überschneidet (Taskleisten-Badge deckt den
  "dauerhaft einsehbarer Zählerstand"-Teil ab).

## Offene Fragen vor einem echten Plan

- **Was zählt?** Dieselben Relevanz-Signale wie das Blinken, oder
  zusätzlich z. B. jede Chat-Nachricht / jedes Item? Getrennt nach
  Items/Hints?
- **Zählen pro Fenster:** Jedes abgetrennte Fenster hat einen eigenen
  Taskleisten-Button → eigene Zahl; das Hauptfenster zählt nur die noch
  enthaltenen Tabs (inkl. Dashboard?). Was passiert mit dem Zähler beim
  Abtrennen/Andocken eines Tabs?
- **Wann zurücksetzen?** Bei Fokus aufs Fenster, beim Öffnen des
  betreffenden Tabs, oder erst beim Scrollen bis zum neuesten Eintrag
  (vgl. den bestehenden "Jump to newest"-Button)?
- **Granularität der Einstellungen:** global in `settings.json`, oder
  zusätzlich pro Server (Gruppe) überschreibbar (z. B. einen lauten Server
  stummschalten)?
- **Stumm-Zeiten / "Nicht stören"?** Oder reicht ein globaler Aus-Schalter?
- **UI-Ort:** bestehender Settings-Dialog, neuer Abschnitt
  "Notifications"? Mit dem `ui-feature-prototyp`-Skill im TestHarness
  vorab durchklicken.
- Alle nativen APIs vor Umsetzung gegen die offizielle Doku verifizieren
  (CLAUDE.md-Grundregel; für Avalonia gegen die gepinnte Version).

## Grobe Reihenfolge (Vorschlag)

1. ✅ Feste Blink-Obergrenze (2026-09-26 umgesetzt).
2. Einstellung für Blinken (Aus / N-mal / bis Fokus) + Drosselung.
3. Zähl-Logik (plattformunabhängig, pro Fenster) + Titel-Zähler als
   erster, überall funktionierender Anzeigeweg.
4. Native Badges: Windows-Overlay, macOS-Dock-Badge, Linux-LauncherEntry.
5. Optional: Toasts, Sound.
