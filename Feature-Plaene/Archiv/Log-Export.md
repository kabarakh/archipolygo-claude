# Umsetzungsplan: Log-Export ("Found by me"-Filter + "Copy from here")

## Status: ✅ Umgesetzt (2026-09-21)

Genau wie unten geplant gebaut, mit zwei Abweichungen, die sich erst beim
Implementieren zeigten:

- **Kein kompiliertes `x:Name`-Feld für Controls im TabControl-Content-Template.**
  Der Plan ging (implizit) davon aus, `EventsListBox`/`HintsListBox`/die
  beiden neuen Buttons ließen sich wie gewöhnliche benannte Elemente aus dem
  Code-behind referenzieren. Tatsächlich generiert Avalonia dafür **kein**
  Feld, weil diese Controls innerhalb von `TabControl.ContentTemplate`
  liegen (CLAUDE.md's Lazy-Materialisierungs-Gotcha) - pro offenem Tab gibt
  es eine eigene Instanz, ein einzelnes Feld wäre mehrdeutig. Deshalb griffen
  auch die schon vorher bestehenden Handler (`OnEventsListKeyDown`/
  `OnHintsListKeyDown`) nie auf ein benanntes Feld zu, sondern immer nur auf
  `sender`. Lösung: ein neuer Helper `MainWindow.FindInSameTemplateInstance<T>`
  läuft vom `sender` aus den Visual Tree hoch und sucht in den
  Geschwister-Zweigen nach dem passenden `Name` (`GetVisualDescendants()`,
  dieselbe Technik, die `JumpToNewestButton.cs` schon nutzt) - `Name` bleibt
  also ein reiner Laufzeit-Name, kein `x:Name`.
- **`IClipboard` in der real gepinnten Avalonia-Version 12.0.4 hat kein
  `GetTextAsync`/`SetTextAsync` als Interface-Member** - das Interface selbst
  ist inzwischen auf eine `IAsyncDataTransfer`/`SetDataAsync`-API umgestellt;
  `SetTextAsync`/`TryGetTextAsync` (nicht `GetTextAsync`) existieren nur als
  Erweiterungsmethoden in `Avalonia.Input.Platform.ClipboardExtensions` und
  brauchen entsprechend `using Avalonia.Input.Platform;` (bereits in
  `ClipboardCopyHelper.cs` vorhanden, in den neuen Tests ergänzt). Betrifft
  nur den Zugriff aus Tests/neuem Code - `ClipboardCopyHelper` selbst musste
  nicht geändert werden.
- Kategorie-C-Tests (`CopyFromHereTests.cs`) konnten das erfolgreich gegen
  Avalonia.Headlesss eingebauten In-Memory-Clipboard verifizieren - die im
  Plan unten offen gelassene Frage, ob das überhaupt geht, ist damit geklärt:
  ja, funktioniert direkt über einen echten simulierten Button-Klick
  (`window.MouseDown`/`MouseUp`) und `TopLevel.GetTopLevel(window).Clipboard`.

Ursprünglich aus der inzwischen archivierten Datei
`archipolygo_feature_ideas.md` ("Log export - write events/hints/chat out
as text or CSV, e.g. to share in Discord") übernommen, dann über mehrere
Diskussionsrunden (2026-09-21) auf den Scope unten eingedampft.

## Ausgangslage

Events/Hints/Received Items liegen pro Gruppe in `GroupViewModel`s
Collections (gespeist über `MessageHistoryService`/`HintService`) nur
in-memory bzw. als internes Sync-State-JSON (`sync-state/<slotId>.json`,
siehe `PersistenceService`) - kein nutzerfacing Export vorhanden.

## Nutzen

Die ursprüngliche Idee ("Discord teilen") deckt eigentlich zwei
unterschiedliche Bedürfnisse ab, die zu unterschiedlichem Scope/Format
führen würden - für v1 wird bewusst nur das erste bedient:

- **Kurativ teilen**: kleine, saubere Textschnipsel für eine
  Discord-Nachricht ("kann jemand meinen Hint checken", "das habe ich für
  euch gefunden") → Zwischenablage, Klartext.
- **Archivieren/Auswerten** (voller Session-Verlauf, CSV): bewusst **nicht**
  Teil von v1 (siehe "Explizit außerhalb von v1" unten).

Chat und der komplette, ungefilterte Events-Merged-Log werden **nicht**
exportierbar gemacht: Chat sind fremde Spieler-Nachrichten (Weiterverbreitung
fragwürdig, und im Room ohnehin für alle sichtbar); der volle Events-Log ist
als Sammel-Feed (Chat+Items+Hints+Connects) zu unspezifisch für einen
Sharing-Anwendungsfall - deshalb der neue `FoundByMe`-Relevanzfilter unten,
der genau den relevanten Ausschnitt herausschält statt "alles" exportierbar
zu machen.

## Scope v1

Zwei unabhängige Export-"Quellen" (Events-Liste mit `FoundByMe`-Filter,
Hints-Liste), beide über denselben "Copy from here"-Mechanismus →
Zwischenablage, reiner Text (kein CSV, kein Datei-Dialog).

**Kein drittes Panel** für ein "Das habe ich gefunden"-Digest (verworfen -
die Information steckt schon im normalen Events-Log, ein eigenes Panel
würde sie nur duplizieren) und **kein neues Datenmodell** (kein
`FoundItemEntry`, spiegelbildlich zu `ReceivedItemEntry`) - beides bewusst
zugunsten von Wiederverwendung des bestehenden Events-Logs verworfen.

## Ansatz

### 1. `Models/EventRelevanceFilter.cs` - neuer Filterwert

Dritter Wert `FoundByMe`, exklusiv zu `All`/`ConcernsMe` (bestätigt: keine
Kombination der beiden Relevanz-Werte gewünscht - anders als die
Item-Kategorie-Checkboxen, die unabhängig von allem anderen bleiben).

### 2. `GroupViewModel.VisibleEvents` - Filterlogik

Die Relevanz-Filterung sitzt am Anfang der bestehenden Filterkette:

```csharp
if (SelectedEventRelevanceFilter == EventRelevanceFilter.ConcernsMe)
{
    events = events.Where(e => e.ConcernsOwnSlot);
}
```

Neuer Zweig für `FoundByMe`. Technischer Mechanismus (präziser als in
früheren Diskussionsrunden angenommen - **kein** Textvergleich gegen
Slot-Namen nötig): `EventSegmentBuilder.BuildChatSegments` klassifiziert
für jede `ItemSendLogMessage`-Zeile bereits das erste `PlayerMessagePart`
(den Finder/Sender, da AP-Chat-Zeilen im Format "{Finder} sent {Item} to
{Receiver} ({Location})" aufgebaut sind) über `ClassifyPlayer` als
`OwnSlotName` (Finder = die verbundene Leader-Session selbst) oder
`ConnectedSlotName` (Finder = ein anderer in dieser Gruppe konfigurierter
Slot). Eine Zeile "gehört" also zu "found by me", wenn ihr erstes Segment
einen dieser beiden Kinds trägt:

```csharp
if (SelectedEventRelevanceFilter == EventRelevanceFilter.FoundByMe)
{
    events = events.Where(e =>
        e.Type == EventType.ItemReceived &&
        e.Segments.Count > 0 &&
        (e.Segments[0].Kind == EventTextSegmentKind.OwnSlotName ||
         e.Segments[0].Kind == EventTextSegmentKind.ConnectedSlotName));
}
```

Das schließt automatisch die Zeilen aus, die der Leader für seinen eigenen
direkten Empfang erzeugt (`BuildItemReceivedSegments` - erstes Segment ist
dort immer der reine Text `"Received "`, kein klassifizierter
Spieler-Name), und funktioniert unverändert mit dem schon vorhandenen
Slot-Filter-Dropdown (`SelectedEventsSlotFilter`) und den
Item-Kategorie-Checkboxen (`ShowProgressionItemEvents` etc.) weiter unten
in derselben Filterkette - keine Änderung an denen nötig.

Dazu ein `[RelayCommand]` `ShowFoundByMeEvents()` (setzt
`SelectedEventRelevanceFilter = EventRelevanceFilter.FoundByMe`), analog zu
`ShowAllEventsRelevanceCommand`/`ShowOwnEventsOnlyCommand`.

### 3. `Views/ClipboardCopyHelper.cs` - "Copy from here"

Neue Methode neben dem bestehenden `CopySelectedLinesAsync<T>`:

```csharp
public static async Task<bool> CopyFromSelectedOnwardsAsync<T>(Visual anchor, ListBox listBox, Func<T, string> toText) where T : class
```

Unterschied zu `CopySelectedLinesAsync`: statt nur die selektierten Items
zu behalten, wird der **früheste** selektierte Index in Anzeigereihenfolge
gesucht, und ab dort **alle** nachfolgenden Items übernommen (unabhängig
von deren eigenem Selektionsstatus). Gibt `false` zurück, wenn nichts
selektiert ist (Aufrufer nutzt das nicht direkt für die Button-Deaktivierung
- die läuft separat über `SelectionChanged`, s. u. -, aber die Methode
bleibt trotzdem robust gegen "nichts ausgewählt", falls sie je anders
aufgerufen wird).

### 4. `Views/MainWindow.axaml` - UI

- **Relevanz-Toggle-Zeile der Events-Spalte** (WrapPanel mit "All"/"Concerns
  me"): dritter `Button` "Found by me", `Command="{Binding
  ShowFoundByMeEventsCommand}"`, `Classes.active` analog an
  `SelectedEventRelevanceFilter == FoundByMe` gebunden.
- **Toolbar-Zeile der Events-Spalte** (dort wo aktuell nur der
  Slot-Filter-`ComboBox` sitzt): neuer `Button Content="Copy from here"
  Name="CopyEventsFromHereButton" Click="OnCopyEventsFromHereClick"
  IsEnabled="False"` (Startzustand: nichts selektiert).
- **Toolbar-Zeile der Hints-Spalte** (Row 2, neben Slot-Filter + Suchbox):
  gleiches Muster, `Name="CopyHintsFromHereButton"
  Click="OnCopyHintsFromHereClick"`.
- **Hints-`ListBox` braucht einen `Name`** - aktuell unbenannt (anders als
  `EventsListBox`), muss für den Click-Handler referenzierbar sein, z. B.
  `Name="HintsListBox"`.

### 5. `Views/MainWindow.axaml.cs` - Handler

- `OnCopyEventsFromHereClick`/`OnCopyHintsFromHereClick`: rufen
  `ClipboardCopyHelper.CopyFromSelectedOnwardsAsync<EventEntry>(this,
  EventsListBox, e => e.Text)` bzw. das Hints-Äquivalent mit demselben
  Zeilenformat wie `OnHintsListKeyDown`
  (`$"{hint.ItemName}: {hint.FindingPlayerName} -> {hint.ReceivingPlayerName} : {hint.LocationName}"`)
  auf - identisch zum bereits bestehenden Strg+C-Zeilenformat, keine neue
  Formatierung.
- **Button-Enabled-Zustand**: `SelectionChanged`-Handler auf beiden
  `ListBox`en (`OnEventsListSelectionChanged`/`OnHintsListSelectionChanged`),
  setzen `CopyEventsFromHereButton.IsEnabled`/`CopyHintsFromHereButton.IsEnabled`
  direkt auf `listBox.SelectedItems?.Count > 0` - Code-behind-getriebene
  UI-State-Synchronisation, kein neuer ViewModel-Zustand nötig (passt zum
  bestehenden Muster, dass die Clipboard-Mechanik komplett im Code-behind
  lebt, nicht in `GroupViewModel`).

**Kein Header** vor dem kopierten Text (Server-/Slot-Name o. Ä.) - nur die
reinen Zeilen. Bei "Alle Slots" als Slot-Filter gäbe es ohnehin keinen
eindeutigen Slot-Namen für einen Header, und der Nutzer stellt den Kontext
in der eigenen Discord-Nachricht ohnehin selbst her.

## Was bewusst gleich bleibt

- Der bestehende Strg+C-Mechanismus (`OnEventsListKeyDown`/
  `OnHintsListKeyDown`, `CopySelectedLinesAsync`) bleibt unverändert -
  "kopiere genau die Auswahl". "Copy from here" ist eine zusätzliche,
  unabhängig sinnvolle Aktion auf derselben Liste, keine Ablösung.
- Zeilenformat pro Eintrag ist für beide Mechanismen identisch (s. o.) -
  "Copy from here" unterscheidet sich nur in der Zeilen-*Auswahl*, nicht im
  Text pro Zeile.
- Item-Kategorie-Checkboxen und Slot-Filter-Dropdown der Events-Spalte
  bleiben unverändert und wirken auf `FoundByMe` genauso wie auf `All`/
  `ConcernsMe`.

## Bekannte Grenzen (akzeptiert)

- **Kein Backlog.** Für die Empfangsrichtung gibt es einen
  Catch-up-Mechanismus über `AllItemsReceived`; eine äquivalente "alle
  meine bisherigen Funde"-Liste bietet `Archipelago.MultiClient.Net` für
  die Senderichtung nicht. `FoundByMe` zeigt also nur Funde, die *seit dem
  letzten Connect* live als Chat-Zeile durchliefen - nicht rückwirkend seit
  Rundenbeginn. Akzeptiert, weil das zum Hauptanwendungsfall ("kurz was zum
  Teilen zusammenstellen") passt.
- **Randfall: eigener Slot findet für einen anderen eigenen konfigurierten
  Slot.** `ResolvePrimarySlotId` (in `ConnectionManager`) taggt eine
  Item-Send-Zeile mit der SlotId des **Empfängers**, sobald dieser
  konfiguriert ist - auch wenn der Finder ebenfalls ein konfigurierter Slot
  ist. Der neue `FoundByMe`-Filter selbst erkennt diesen Fund trotzdem
  korrekt (er prüft das Segment, nicht `SlotId`), aber der zusätzliche
  Slot-Filter-Dropdown (der nach `SlotId` filtert) würde diese Zeile unter
  dem *Empfänger*-Slot einsortieren, nicht unter dem *Finder*-Slot. Selten
  genug (eigener Fund für eigenen anderen Slot) und die Zeile bleibt
  trotzdem sichtbar (nur beim spezifischen Slot-Filter ggf. am "falschen"
  Slot) - keine Sonderbehandlung für v1.

## Explizit außerhalb von v1

- Export des vollständigen Events-Logs (ungefiltert) oder von Chat.
- CSV-Format.
- Datei-Export (Save-Dialog).
- Rückwirkendes Backlog für den `FoundByMe`-Filter.
- Ein eigenes drittes Panel für ein "gefunden"-Digest.
- Ein eigenes `FoundItemEntry`-Datenmodell.

## Tests

**Umsetzung:** genau so gebaut, plus die beiden oben dokumentierten
API-Korrekturen.

- **Kategorie A** (`GroupViewModelEventRelevanceTests.cs`, `[Fact]`, kein
  Avalonia nötig): `GroupViewModel` direkt instanziieren, `Events` mit einer
  Mischung aus direkt empfangenen Zeilen (`BuildItemReceivedSegments`-Form,
  "Received ..."), "found by me"-Zeilen (erstes Segment `OwnSlotName`/
  `ConnectedSlotName`) und Fremd-Zeilen (`OtherSlotName`) befüllen,
  `SelectedEventRelevanceFilter = FoundByMe` setzen und `VisibleEvents`
  gegen die erwartete Teilmenge prüfen - plus Interaktion mit den
  Item-Kategorie-Checkboxen und dass `All` die Einschränkung nicht anwendet.
- **Kategorie C** (`CopyFromHereTests.cs`, `[AvaloniaFact]`, echtes
  `MainWindow`): eine Zeile in der Events- bzw. Hints-`ListBox` selektieren,
  prüfen dass der jeweilige "Copy from here"-Button von deaktiviert auf
  aktiviert wechselt (und beim Aufheben der Auswahl wieder deaktiviert);
  echter simulierter Klick (`window.MouseDown`/`MouseUp`, wie in
  `EventsListAutoScrollTests`) und der kopierte Zwischenablage-Inhalt
  (`TopLevel.GetTopLevel(window).Clipboard`, `TryGetTextAsync()`) gegen die
  erwarteten Zeilen (Anker + alle danach) geprüft - funktioniert direkt
  gegen Avalonia.Headlesss eingebauten In-Memory-Clipboard, kein Mock nötig.
