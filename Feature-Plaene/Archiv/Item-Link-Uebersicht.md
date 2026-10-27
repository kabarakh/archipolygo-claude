# Umsetzungsplan: Übersicht der Item-Link-Gruppen

## Status: ✅ Umgesetzt (2026-10-28)

Der Entwickler hat die Idee im Harness geprüft und freigegeben. Es gab
vorher keinen eigenen Plan. Dieses Dokument beschreibt darum direkt die
Umsetzung.

- Alle neuen Tests stehen in `ItemLinkGroupTests.cs` (6 Tests).
- Die gesamte Suite (607 Tests) ist grün.
- Die Dashboard-Hint-Liste zeigt noch keinen Tooltip am Namen einer
  Item-Link-Gruppe. Diese Liste hat ein eigenes Template.

---

## Anlass

Ein Raum kann Item-Link-Gruppen haben. Mehrere Slots teilen sich dann einen
Item-Pool. Archipelago sendet ein Item an die Item-Link-Gruppe, nicht an
einen Slot. Das Event-Log und die Hints zeigen darum nur den Namen der
Item-Link-Gruppe. Der Benutzer sieht nicht, welche Slots in der
Item-Link-Gruppe sind.

## Datenquelle

Die Recherche gilt für `Archipelago.MultiClient.Net` 6.7.1.

- Das Roster enthält jede Item-Link-Gruppe als eigenen Eintrag.
- `PlayerInfo.IsGroup` ist `true` für eine Item-Link-Gruppe.
- `PlayerInfo.GetGroupMembers(IPlayerHelper)` liefert die Mitglieder als
  `PlayerInfo`.
- Die Item-Link-Gruppen ändern sich innerhalb eines Seeds nicht.

## Lösung

Die App zeigt die Mitglieder an zwei Stellen.

### 1. Tooltip am Namen

- Der Tooltip zeigt die Mitglieder, zum Beispiel "Item link: TestSlot,
  Nightowl, Player One".
- Der Tooltip erscheint im Event-Log und beim Empfänger in der Hint-Liste.
- `RoomSlotStatus.ItemLinkMembersText` trägt den Text. Nur der Status einer
  Item-Link-Gruppe hat einen Text. Für andere Namen ist er `null`.
- Jedes Namenssegment und jeder Hint hat schon einen `RoomSlotStatus`.
  Darum braucht der Tooltip keine eigene Zuordnung.

### 2. Panel "Item links"

- `RightPanelView.ItemLinks` ist ein vierter Wert neben Hints, Items und
  Admin.
- Der Button "Item links" erscheint nur, wenn der Raum Item-Link-Gruppen
  hat (`HasItemLinkGroups`).
- Pro Item-Link-Gruppe zeigt das Panel Name, Anzahl der Mitglieder, Spiel
  und die Mitglieder.
- Eigene Slots stehen oben. Sie haben die Farbe `OwnSlotName`.
- Die Goal-Markierung gilt für die Item-Link-Gruppe und für jedes Mitglied.
- Das Panel hat keine Filterleiste (`IsRightPanelFilterBarVisible`).
- Wenn alle Item-Link-Gruppen verschwinden, wechselt die App auf Hints.

## Ablauf in der App

1. `ConnectionManager.SwitchLeaderAsync` ruft nach jedem Leader-Connect
   `ReadItemLinkGroups` auf.
2. `SessionEventTranslator.ReadItemLinkGroups` liest die Einträge mit
   `IsGroup` aus `session.Players.AllPlayers`.
3. Die Methode sendet die Daten über `Dispatcher.UIThread.Post` an
   `GroupViewModel.ApplyItemLinkGroups`.
4. `ApplyItemLinkGroups` ersetzt die ganze Liste und setzt die Tooltip-Texte
   neu.

**Ein Fehler beim Lesen darf den Leader-Connect nicht abbrechen.** Die
Übersicht ist nur eine Anzeige. `ConnectionManager` schreibt den Fehler
darum nur mit `_diagnosticLogger.Warning`.

Die Methode liest das Roster nicht über `FilterToRealPlayers`. Dieser
Helfer entfernt genau die Item-Link-Gruppen.

## Dateien

- `Models/ItemLinkGroup.cs`: `RoomPlayerRef`, `ItemLinkGroupSnapshot`,
  `ItemLinkMember`, `ItemLinkGroup`.
- `ViewModels/GroupViewModel.ItemLinks.cs`: Liste, Befehl und
  Klassifizierung der Mitglieder.
- `Views/GroupDetailView.axaml`: Button, Panel und Tooltip in der
  Hint-Liste.
- `Views/SharedTemplates.axaml`: Tooltip im Template der Namenssegmente.
- `TestHarness/ControlPanelWindow.cs`: Abschnitt "Item links" mit
  Testdaten.

## Nebenbefund im Harness

`TestHarnessApp.axaml` hatte die Ressourcen von `App.axaml` nicht. Die
Dashboard-Hint-Liste stürzte darum beim ersten Hint ab. Jetzt hat
`TestHarnessApp.axaml` dieselben Ressourcen und Styles wie `App.axaml`.
