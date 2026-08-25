---
name: ui-feature-prototyp
description: Für neue Feature-Ideen, die vor allem die UI/UX betreffen - zuerst grob im TestHarness prototypisch nachbauen und vom Entwickler selbst live durchklicken lassen, bevor die Idee in die echte App (AvaloniaApplication1) übernommen wird. Nutzen, bevor Code für eine neue UI-Idee (neues Panel, neue Interaktion, neues Layout, ...) in die Produktiv-App eingebaut wird.
---

# Neue UI-Feature-Ideen zuerst im TestHarness prototypisieren

UI-Ideen lassen sich schlecht allein am Code oder an einer Beschreibung
beurteilen - Layout, Klickfluss und "wie fühlt sich das an" zeigen sich erst,
wenn man es tatsächlich anklickt. Deshalb wird eine neue, vor allem
UI-getriebene Feature-Idee **zuerst grob im vorhandenen `TestHarness/`-
Projekt** (siehe `.claude/skills/app-testen`) nachgebaut, bevor überhaupt
Code für die echte App (`AvaloniaApplication1`) geschrieben wird.

## Wann dieser Skill greift

- Die Idee betrifft überwiegend UI/UX: ein neues Panel, eine neue Spalte
  oder ein neuer Filter, eine andere Anordnung, eine neue Interaktion
  (Kontextmenü, Drag&Drop, zusätzlicher Dialog-Schritt), visuelles Feedback
  (Badges, Farben, Animationen, neue Sortierung).
- Auch bei "Variante A oder B?"-Fragen: beide Varianten grob im TestHarness
  bauen und den Entwickler beide durchklicken lassen, statt das im Chat
  abstrakt zu diskutieren.
- **Nicht** nötig für reine Backend-/Verbindungs-/Persistenz-Änderungen ohne
  sichtbares UI-Delta - die gehen direkt in `AvaloniaApplication1`.

## Vorgehen

1. **Idee im TestHarness grob prototypisieren.** Wo möglich echte Views/
   Controls aus `AvaloniaApplication1` wiederverwenden oder als Basis
   kopieren, damit der Prototyp repräsentativ ist (siehe `app-testen`
   Grundprinzip 1). Über `ControlPanelWindow`/`FakeConnectionManager` genug
   plausible Testdaten einspeisen, damit die neue UI sich sinnvoll befüllt
   zeigt (z. B. genug Events, um ein neues Filter-Feature zu demonstrieren).
   Der Prototyp muss nicht production-ready sein - Ziel ist ein anklickbarer
   Beweis der Idee, keine fertige Implementierung; TODOs/Hacks im Code sind
   an dieser Stelle okay.
2. **Den Entwickler selbst live durchklicken lassen, nicht nur einen
   Screenshot zeigen.** Bei einer neuen Feature-Idee reicht die eigene
   Sichtprüfung per Screenshot/simuliertem Klick (wie in `app-testen`
   Grundprinzip 2, für Bug-Reproduktion gedacht) nicht aus - hier soll ein
   Mensch die Idee selbst bewerten und ein Gefühl dafür bekommen. Also:
   `TestHarness` sichtbar für den User starten (`dotnet run --project
   TestHarness`, im Vordergrund, nicht versteckt im Hintergrund) und um
   Feedback bitten.
3. **Erst nach Rückmeldung/Freigabe des Entwicklers** die Idee in die echte
   App übernehmen - Prototyp-Code sauber in echte Views/ViewModels
   überführen, TestHarness-spezifische Abkürzungen dabei entfernen. Die im
   `ControlPanelWindow` neu hinzugekommenen Buttons können bleiben, wenn sie
   fürs künftige Testen dieses Features nützlich sind, oder wieder entfernt
   werden, wenn sie nur für die eine Entscheidung gebraucht wurden.

## Warum nicht direkt in der echten App ausprobieren

- Eine echte Archipelago-Verbindung wäre nur ein Störfaktor beim reinen
  UI-Feedback (Netzwerk, Login-Timing, Server-Zustand).
- Ein halbfertiges Feature soll nicht versehentlich in `AvaloniaApplication1`
  landen, bevor der Entwickler es überhaupt gesehen und abgenickt hat.
- Kurzer Iterationszyklus: Anpassung → TestHarness neu starten (siehe
  `app-testen` Grundprinzip 3 zum sparsamen Umgang mit Neustarts - gilt hier
  genauso) → Entwickler schaut sich's an → ggf. weiter anpassen, ohne dass
  irgendetwas in der Produktiv-App zwischenzeitlich in einem
  Halbfertig-Zustand ist.
