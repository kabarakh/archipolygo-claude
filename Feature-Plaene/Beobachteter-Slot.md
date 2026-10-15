# Feature-Idee (grobe Skizze): Beobachteter Slot

**Status: 🗨️ Diskutieren** - Diese Datei ist kein Umsetzungsplan. Sie ist
eine Skizze aus der Diskussion vom 2026-10-13. Einige Entscheidungen stehen
fest (siehe "Entscheidungen"). Die Form der UI und die Methode für Catch-up
stehen noch nicht fest.

> **Regel bis zur finalen Entscheidung:** Probiere alle Ideen dieses Plans
> nur im Harness aus (Skill `ui-feature-prototyp`). Schreibe für dieses
> Feature keinen Code in `AvaloniaApplication1/`. Diese Regel gilt auch für
> kleine Teile, zum Beispiel für das neue Datenfeld. Zuerst probiert der
> Entwickler die Varianten im Harness aus und wählt eine. Danach machst du
> aus dieser Datei einen 📝-Umsetzungsplan.

## Anlass

In einem Raum kann der Benutzer mehrere eigene Slots spielen. Er will
zusätzlich einen fremden Slot verfolgen (Beispiel: `malandor`). Besonders
will er wissen, wann dieser Slot ein Progression-Item empfängt. Heute zeigt
die App Ereignisse nur für eigene Slots. Außerdem kennt
`IAttentionTracker.Report` den Slot nicht, zu dem ein Ereignis gehört.

## Entscheidungen

- **Der beobachtete Slot ist ein fremder Slot.** Er ist kein `SlotProfile`.
  Hints, Event-Log, Received Items und Dashboard zeigen ihn wie einen
  eigenen Slot. Er hat eine sichtbare Markierung (zum Beispiel ⭐). Diese
  Grenzen gelten:
  - Der Benutzer kann ihn in "Chat as" nicht auswählen.
  - Er ist nie Leader.
  - Die App fragt nicht nach seinem Passwort.
  - Die Startup-Sync-Zähler zählen ihn nicht.
- **Eine Gruppe hat höchstens einen beobachteten Slot.** Die App speichert
  seinen Namen aus dem Roster, zum Beispiel in
  `ServerConnectionGroup.WatchedSlotName` (`string?`). Vergleiche Namen mit
  `OrdinalIgnoreCase`. Diese Datenstruktur stellt sicher, dass eine Gruppe
  höchstens einen beobachteten Slot hat.
- **Für den beobachteten Slot sind nur Progression-Items wichtig.** Das gilt
  für das Panel und für die Benachrichtigungen. Traps, Useful-Items,
  Filler-Items und Startitems sind nicht wichtig. Event-Log und Items-Panel
  zeigen trotzdem alle Items des Slots, wie bei eigenen Slots.
- **Der Status "beobachtet" steuert auch die Benachrichtigungen.** So gibt
  es ein Konzept, nicht zwei.
- **Catch-up ist nötig.** Die App muss auch die Ereignisse holen, die
  passieren, während die App geschlossen ist (Methoden siehe Teil 5).

## Teile (im Harness ausprobieren)

### 1. Panel "letzte n Progression-Items"

Die Daten sind eine gefilterte, kurze Sicht auf die empfangenen Items der
Gruppe. Filter: Empfänger ist der beobachtete Slot, und
`ItemKind == ItemProgression`.

Varianten:
- **Position:** Ein Streifen, der immer sichtbar ist und den der Benutzer
  einklappen kann. Setze ihn über das Event-Log *oder* über das rechte
  Panel. Mache ihn nicht zu einer vierten `RightPanelView`. Sonst verdeckt
  zum Beispiel die Hint-Ansicht den Streifen.
- **Darstellung:** Eine statische Liste (neuestes Item oben, mit
  Scrollleiste) *oder* ein Ticker, der automatisch durchläuft. Aktuelle
  Einschätzung: Nimm die Liste. Text, der sich bewegt, lenkt den Benutzer
  ständig ab.
- **Zeile:** Zum Beispiel `Hookshot ← Spieler X (Location)`. Hebe neue
  Einträge hervor, bis der Benutzer die Gruppe sieht (`RefreshSeenState`).
- **n:** Ein fester Wert (5 bis 10) oder eine Einstellung (global oder pro
  Gruppe).
- Das Panel zeigt auch Items aus dem Catch-up, denn das Panel zeigt einen
  Zustand. Wie bisher lösen Items aus dem Catch-up keine Benachrichtigung
  aus.
- Das Panel ist nur sichtbar, wenn die Gruppe einen beobachteten Slot hat.

### 2. Auswahl des beobachteten Slots

Setze die Auswahl in den Connection-Editor (Modus `EditGroup`): "Watched
slot: (none) / …". Fülle die Liste aus dem Roster, wie den vorhandenen
Player-Picker. Lass die eigenen Slots der Gruppe weg. Prüfe außerdem, ob ein
Eintrag im Kontextmenü nützlich ist, zum Beispiel am Spielernamen im
Event-Log.

### 3. Dashboard und Event-Log

- **Hervorhebung:** Event-Zeilen mit einem Progression-Item für den
  beobachteten Slot bekommen ⭐ oder eine Akzentfarbe. Das gilt für das
  Event-Log des Tabs und für das gemeinsame Event-Log im Dashboard. Ein
  Schalter neben den vorhandenen Filtern für Item-Klassen steuert die
  Hervorhebung.
- **Slot-Filter:** Der beobachtete Slot ist ein eigener Eintrag in jedem
  der drei Slot-Filter.
- **Filter "Watched only"** im Dashboard: Dieser Filter zeigt eine Liste
  "Gruppe - Slot - Item" über alle Gruppen. Sie ist fast gleich wie das
  Panel aus Teil 1, aber für alle Gruppen.
- **Server-Übersicht im Dashboard:** Jede Gruppe bekommt eine kurze Zeile,
  zum Beispiel "⭐ malandor: Hookshot · 3 min ago".

### 4. Benachrichtigungen

- Für den beobachteten Slot meldet die App nur Progression-Items. Sie
  meldet keine Hints, keine anderen Items und keine Erwähnungen im Chat.
- Variante A: Eine neue Kategorie `WatchedProgressionItem`. Sie ist eine
  weitere Zeile in der Tabelle der Einstellungen, mit eigenen Werten für
  Count und Blink.
- Variante B: Keine neue Einstellung. Das Ereignis hat Vorrang: Es ignoriert
  die Stummschaltung der Gruppe und die Drosselung des Blinkens.
- Toast und Ton aus `Benachrichtigungen-Erweiterungen.md` sind mögliche
  Ergänzungen für später. Dieser Fall ist ein guter erster Einsatz für sie.

### 5. Datenquellen: live und Catch-up

**Live (passiv über den Leader, kein eigener Login):**
- Items: `SessionEventTranslator.OnLeaderMessageReceived` sieht jedes
  gesendete Item im Raum. Heute übernimmt die Methode ein Item nur, wenn der
  Empfänger ein eigener Slot ist. Ergänze diese Prüfung um den Namen des
  beobachteten Slots.
- Hints: `TrackSiblingHintsOnLeader` abonniert schon die Hints von jedem
  numerischen Slot mit `TrackHints(slot: n)`. Mache dasselbe für den
  beobachteten Slot. Der Server sendet beim Verbinden immer alle Hints.

**Catch-up (Ereignisse, während die App geschlossen ist) - zwei Methoden:**
- **Methode A: Tracker-API des Webhosts** (`api/tracker/<id>`).
  `MultiworldTrackerService` fragt diese API schon ab. Der Quelltext des
  WebHosts (`WebHostLib/api/tracker.py`) zeigt: Die API liefert
  `player_items_received` für jeden Slot. Das ist eine Liste von
  `NetworkItem` (Item, Location, Sender, Flags). Die App muss die IDs mit
  dem Datapackage der Leader-Session in Namen umwandeln.
  - Vorteil: Kein Login. Die anderen Spieler sehen nichts.
  - Nachteil: Die Methode funktioniert nur, wenn die Gruppe eine
    Tracker-Referenz hat. Darum funktioniert sie nicht bei einem selbst
    gehosteten Server ohne Webhost. Die Daten können etwas verzögert sein
    (Cache des Trackers).
- **Methode B: Kurzer Login als beobachteter Slot**, wie
  `CatchUpSyncCoreAsync` für eigene Slots. Die App verbindet sich immer mit
  dem Tag `Tracker`. Darum ändert sie nichts am Slot.
  - Vorteil: Die Methode funktioniert auf jedem Server. Sie nutzt die
    vorhandene Mechanik (`ProfileSyncState`, Index des letzten gesehenen
    Items).
  - Nachteil: Alle Spieler im Raum sehen den Login ("malandor ... has
    joined/left" bei jedem Catch-up). Das kann den Besitzer des Slots
    verwirren. Wenn der Slot ein eigenes Passwort hat, schlägt der Login
    fehl.
- Aktuelle Einschätzung: Nimm Methode A. Nimm Methode B nur als Fallback,
  den der Benutzer ausdrücklich einschaltet. Prüfe vor der Entscheidung die
  Tracker-API gegen die offizielle Archipelago-Dokumentation. Oder prüfe sie
  gegen den Quelltext des WebHosts in der Version, die der Server nutzt.
- Im Harness kannst du das nur mit Fake-Daten simulieren. Zum Beispiel
  zeigt ein Button "Catch-up liefert 3 Items", wie die nachgeholten Items im
  Panel und im Event-Log aussehen.

## Offene Fragen (im Harness klären)

1. Liste oder Ticker? Position über dem Event-Log oder über dem rechten
   Panel?
2. Ist n ein fester Wert oder eine Einstellung? Wenn es eine Einstellung
   ist: Wo ist sie?
3. Benachrichtigung: Variante A oder Variante B? Muss ein beobachteter Slot
   auch bei einer stummgeschalteten Gruppe durchkommen?
4. Dashboard: Reichen Hervorhebung und Filter? Oder ist die Übersichtszeile
   pro Gruppe auch nötig?
5. Zeitangabe pro Eintrag ("3 min ago")? Heute hat `ReceivedItemEntry`
   keinen Zeitstempel. Lohnt sich ein Zeitstempel? Welche Zeit gilt für
   Items aus dem Catch-up, deren Empfangszeit unbekannt ist?
6. Catch-up: Methode A, Methode B oder Methode A mit Methode B als Fallback?
7. Kann der Benutzer auch einen *eigenen* Slot als beobachtet markieren?
   Oder gilt das Feature nur für fremde Slots?
