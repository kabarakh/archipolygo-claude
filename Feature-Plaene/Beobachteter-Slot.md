# Feature-Idee (grobe Skizze): Beobachteter Slot

**Status: 🗨️ Muss noch durchdiskutiert werden** - kein Umsetzungsplan,
sondern eine Skizze aus der Diskussion vom 2026-10-13. Eckdaten sind
entschieden (siehe unten), die UI-Form und der Catch-up-Weg noch nicht.

> **Verbindliche Regel bis zur finalen Entscheidung:** Alle
> Umsetzungsideen dieses Plans werden **ausschließlich im `TestHarness/`**
> ausprobiert (Skill `ui-feature-prototyp`). In `AvaloniaApplication1/`
> wird dafür nichts gebaut - auch kein "kleiner Vorgriff" wie das neue
> Datenfeld -, bis der Entwickler die Varianten im Harness durchgeklickt und
> sich festgelegt hat. Erst dann wird diese Datei zu einem 📝-Umsetzungsplan.

## Anlass

In einem Raum, in dem man mehrere eigene Slots spielt, will man zusätzlich
den Slot eines *anderen* Spielers (Beispiel: `malandor`) mitverfolgen und
besonders mitbekommen, wenn er ein Progression-Item erhält. Heute zeigt die
App nur Ereignisse für konfigurierte (eigene) Slots, und
`IAttentionTracker.Report` kennt den betroffenen Slot gar nicht.

## Entschiedene Eckdaten

- **Der beobachtete Slot ist ein fremder Slot**, kein `SlotProfile`. Er
  erscheint in Hints, Event-Log, Received-Items und Dashboard wie ein
  eigener Slot (optisch unterscheidbar, z. B. ⭐), ist aber **nicht** in
  "Chat as" auswählbar, wird nie Leader, bekommt keine Passwortabfrage und
  zählt nicht in den Startup-Sync-Zählern mit.
- **Höchstens ein beobachteter Slot pro Welt (Gruppe).** Gespeichert als
  Rosternname, etwa `ServerConnectionGroup.WatchedSlotName` (`string?`,
  Vergleich `OrdinalIgnoreCase`) - "höchstens einer" ist damit strukturell
  garantiert.
- **Nur Progression-Items** zählen als "beobachtenswert" (Panel und
  Benachrichtigung) - keine Traps, Useful- oder Filler-Items, keine
  Startitems. Im Event-Log/Items-Panel erscheinen trotzdem alle Items des
  Slots, wie bei eigenen Slots.
- **Der Beobachten-Status steuert auch die Benachrichtigungen**, ein Konzept
  statt zwei.
- **Catch-up ist gewünscht** - auch was passiert, während die App zu ist,
  soll nachgeholt werden (Wege siehe Baustein 5).

## Bausteine (auszuprobieren im Harness)

### 1. Panel "letzte n Progression-Items"

Datenseitig eine gefilterte, gekürzte Sicht auf die Received-Items der
Gruppe (Empfänger = beobachteter Slot, `ItemKind == ItemProgression`).

Varianten:
- **Platzierung:** dauerhaft sichtbarer, einklappbarer Streifen über dem
  Event-Log *oder* über dem rechten Panel. Bewusst **nicht** als vierte
  `RightPanelView`, sonst verdeckt z. B. die Hint-Ansicht ihn.
- **Darstellung:** statische Liste (neuestes oben, scrollbar) *oder*
  automatisch durchrotierender Ticker. Vorläufige Einschätzung: Liste, weil
  bewegter Text ständig ablenkt.
- **Zeile:** etwa `Hookshot ← Spieler X (Location)`; noch ungesehene Einträge
  hervorgehoben, bis die Gruppe gesehen wurde (`RefreshSeenState`).
- **n:** fest (5-10) oder einstellbar (global / pro Welt).
- Catch-up-Backlog erscheint im Panel (es zeigt einen Zustand), löst aber wie
  bisher keine Benachrichtigung aus.
- Nur sichtbar, wenn die Gruppe einen beobachteten Slot hat.

### 2. Auswahl des beobachteten Slots

Im Connection-Editor (Modus `EditGroup`): "Watched slot: (none) / …",
befüllt aus dem Raum-Roster wie der vorhandene Player-Picker, ohne die
eigenen konfigurierten Slots. Prüfen, ob zusätzlich ein Kontextmenü-Eintrag
(z. B. am Spielernamen im Event-Log) sinnvoll ist.

### 3. Dashboard und Event-Log

- **Hervorhebung:** Event-Zeilen mit Progression-Item für den beobachteten
  Slot bekommen ⭐/Akzentfarbe - im Tab-Event-Log und im konsolidierten
  Dashboard-Log, per Schalter neben den vorhandenen Item-Klassen-Filtern.
- **Slot-Filter:** der beobachtete Slot als eigener Eintrag in den drei
  Slot-Filtern.
- **Filter "Watched only"** im Dashboard: ergibt weltübergreifend eine Liste
  "Welt - Slot - Item", praktisch das Panel aus Baustein 1 über alle Server.
- **Server-Übersicht im Dashboard:** pro Server eine kompakte Zeile
  "⭐ malandor: Hookshot · vor 3 min".

### 4. Benachrichtigungen

- Für den beobachteten Slot wird nur ein Progression-Item gemeldet - keine
  Hints, keine anderen Items, keine Chat-Erwähnungen.
- Variante A: eigene Kategorie `WatchedProgressionItem` als weitere Zeile in
  der Settings-Tabelle (Count/Blink separat einstellbar).
- Variante B: kein neuer Settings-Eintrag, sondern Vorrang - umgeht
  Server-Mute und Blink-Drosselung.
- Toast/Sound aus `Benachrichtigungen-Erweiterungen.md` würden später
  naheliegend zuerst für genau diesen Fall kommen.

### 5. Datenquellen: live und Catch-up

**Live (passiv über den Leader, kein eigener Login nötig):**
- Items: `SessionEventTranslator.OnLeaderMessageReceived` sieht jeden
  Item-Send im Raum, übernimmt ihn heute aber nur, wenn der Empfänger ein
  konfigurierter Slot ist - müsste um den beobachteten Namen erweitert
  werden.
- Hints: `TrackSiblingHintsOnLeader` abonniert per `TrackHints(slot: n)`
  bereits Hints beliebiger numerischer Slots; für den beobachteten genauso.
  Hints kommen beim Verbinden ohnehin vollständig vom Server.

**Catch-up (was lief, während die App zu war) - zwei Wege:**
- **Weg A: Webhost-Tracker-API** (`api/tracker/<id>`, die
  `MultiworldTrackerService` schon abfragt). Laut WebHost-Quelltext
  (`WebHostLib/api/tracker.py`) liefert sie `player_items_received` pro
  Slot als `NetworkItem`-Liste (Item, Location, Sender, Flags). IDs müssten
  über das Datapackage der Leader-Session in Namen übersetzt werden.
  Vorteil: kein Login, unsichtbar für die anderen Spieler. Nachteil: geht
  nur, wenn für die Gruppe eine Tracker-Referenz hinterlegt ist (also nicht
  bei selbst gehosteten Servern ohne Webhost); Daten können leicht
  verzögert sein (Tracker-Cache).
- **Weg B: kurzer Login als der beobachtete Slot**, wie
  `CatchUpSyncCoreAsync` für eigene Slots (die App verbindet sich ohnehin
  mit Tag `Tracker`, ändert am Slot also nichts). Vorteil: funktioniert auf
  jedem Server, vorhandene Mechanik (`ProfileSyncState`, Index des zuletzt
  gesehenen Items). Nachteil: für alle im Raum sichtbar ("malandor ...
  has joined/left" bei jedem Catch-up) - kann den Besitzer des Slots
  irritieren; scheitert, wenn der Slot ein eigenes Passwort hat.
- Vorläufige Einschätzung: A bevorzugt, B höchstens als ausdrücklich
  einzuschaltender Fallback. Vor einer Entscheidung die Tracker-API gegen
  die offizielle Archipelago-Doku bzw. den WebHost-Quelltext der
  tatsächlich laufenden Version prüfen.
- Im Harness lässt sich das nur über Fake-Daten simulieren (z. B. ein
  Button "Catch-up liefert 3 Items"), um zu sehen, wie Backlog im Panel und
  im Log aussieht.

## Offene Fragen (im Harness zu klären)

1. Liste oder Ticker? Platzierung über Event-Log oder über rechtem Panel?
2. n fest oder einstellbar - und wenn einstellbar, wo?
3. Benachrichtigung: Variante A oder B? Soll ein beobachteter Slot trotz
   Server-Mute durchkommen?
4. Dashboard: reicht Hervorhebung + Filter, oder zusätzlich die
   Übersichtszeile pro Server?
5. Zeitangabe pro Eintrag ("vor 3 min")? `ReceivedItemEntry` hat heute
   keinen Zeitstempel - lohnt sich einer, und was gilt für Catch-up-Items,
   deren Empfangszeit man nicht kennt?
6. Catch-up: Weg A, Weg B oder A mit B als Fallback?
7. Soll man auch einen *eigenen* Slot als beobachtet markieren können, oder
   bleibt das Feature auf fremde Slots beschränkt?
