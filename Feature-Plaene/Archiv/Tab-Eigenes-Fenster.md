# Umsetzungsplan: Tabs in eigene Fenster lösen + Fenster-Blinken

## Status: ✅ Umgesetzt (2026-09-24)

Weicht an einigen Stellen vom Plantext unten ab:

- **Auslösen/Andocken laufen über ein Kontextmenü bzw. einen Button, nicht
  per Drag & Drop** (Dev-Feedback nach dem ersten Durchklicken: "ich kann
  die Tabs nicht herausziehen" - das Herausziehen eines Tabs in ein
  brandneues natives Fenster über `DoDragDropAsync` erwies sich in der
  Praxis als unzuverlässig). Ersetzt durch:
  - Rechtsklick auf einen Tab-Header (`MainWindow.axaml`) bzw. eine
    Dashboard-Overview-Zeile (`DashboardView.axaml`) → Kontextmenü-Eintrag
    "Open in new window" → `MainWindowViewModel.DetachGroup`. Der
    Dashboard-Eintrag ist ausgegraut (`IsEnabled="{Binding !IsDetached}"`),
    sobald die Gruppe schon in einem eigenen Fenster lebt -
    `GroupViewModel.IsDetached` ist dafür jetzt eine `[ObservableProperty]`
    statt einer reinen Laufzeit-Variable.
  - `DetachedGroupWindow`s eigener "Dock to main window"-Button statt einer
    Drag-Geste zurück auf die Haupt-Tableiste - ruft direkt
    `MainWindowViewModel.RedockGroup` über ein `Action<GroupViewModel>`,
    das `MainWindow.axaml.cs` beim Erzeugen des Fensters mitgibt.
  - `GroupReorderDragDrop`/die eigentliche Drag-Geste bleiben unverändert
    für das bereits bestehende Feature manuelles Tab-Reordering
    (Tab-Reihenfolge.md) - nur der *neue* Detach/Re-Dock-Teil dieses Plans
    wurde umgestellt.
- **Neue geteilte View `GroupDetailView` (UserControl)**, vom Plan selbst
  nicht vorgesehen: die eigentlichen Events/Hints/Items-Inhalte (bisher
  inline in `MainWindow.axaml`s `TabControl.ContentTemplate`) mussten in
  eine eigene, wiederverwendbare `UserControl` extrahiert werden, damit
  `DetachedGroupWindow` sie unverändert übernehmen kann, statt ~400 Zeilen
  XAML zu duplizieren. Jeder Button-/Listen-Event-Handler löst seinen Owner
  jetzt über `TopLevel.GetTopLevel(this)` auf statt über ein hartcodiertes
  `this` (MainWindow) - funktioniert dadurch unverändert in beiden Fenstern.
- **Auslösen des Detach** ist technisch "irgendwo auf dem `TabControl`
  gedroppt, das kein Tab-Header ist" (`MainWindow.axaml.cs`s
  `OnMainAreaDrop`), nicht wörtlich "außerhalb der `TabControl`-Bounds" -
  Avalonias Drag & Drop kann einen Drop nur an ein `AllowDrop`-Ziel melden,
  das der Zeiger gerade trifft, nie "irgendwo ohne jedes Ziel" - siehe
  `OnMainAreaDrop`s eigenen Kommentar.
- **Re-Docking läuft über dieselbe Registry-basierte Prüfung
  (`GroupViewModel.IsDetached`)** in beide Richtungen: derselbe
  `OnMainAreaDrop`/`OnGroupTabDrop` erkennt an `IsDetached`, ob ein Drop
  "andocken" oder "auslösen" bedeutet, statt zwei getrennte Codepfade zu
  brauchen.
- **Plain-Schließen eines `DetachedGroupWindow`s (✕-Button/Alt+F4) dockt
  automatisch wieder an**, statt die Gruppe unsichtbar "ausgelagert" zu
  lassen (im Plan selbst noch offen als "Wie wird ein losgelöstes Fenster
  wieder eingedockt... oder ist das gar nicht vorgesehen") - vermeidet einen
  Zustand ohne jede sichtbare UI für eine noch konfigurierte Gruppe.
- **`IWindowAttentionService.RequestAttention(Guid groupId)`** ist synchron
  (`void`), nicht `Task FlashGroupAsync(Guid)` wie im Plan skizziert - keine
  der drei Plattform-Implementierungen tut tatsächlich etwas Awaitbares.
- **Linux-Implementierung** hängt `_NET_WM_STATE_DEMANDS_ATTENTION` direkt
  per `XChangeProperty` an, nicht über den spec-konformen
  `ClientMessage`/`XSendEvent`-Weg (der ein handmarshalliertes
  `XClientMessageEvent`-Struct bräuchte, dessen Layout im Fehlerfall nativ
  abstürzen könnte) - bewusst die simplere, risikoärmere Variante mit rein
  skalaren P/Invoke-Signaturen, siehe `LinuxWindowAttention`s eigenen
  Kommentar. Ungetestet (kein Linux in dieser Session verfügbar).
- **Tests:** Kategorie A für `DetachGroup`/`RedockGroup` (`TabDetachTests.cs`)
  sowie für die Hint-/DeathLink-Blink-Trigger (`WindowAttentionTriggerTests.cs`,
  gegen einen neuen `FakeWindowAttentionService`) geschrieben und grün. Der
  dritte Trigger (Progression-Item, live) ist **nicht** getestet - der
  gemeinsame Test-Fake `FakeReceivedItemsHelper.AllItemsReceived` wirft
  `NotImplementedException` (eine schon vorher bestehende Lücke, kein
  existierender Test übt `ConnectionManager`s Item-Received-Pfad überhaupt
  aus) - ehrlich dokumentiert statt stillschweigend übersprungen, siehe
  `WindowAttentionTriggerTests`s eigenen Kommentar. Die eigentliche
  Drag-Geste (Detach/Re-Dock) ist laut `Tab-Reihenfolge.md`s eigener
  Erfahrung unter Avalonia.Headless nicht simulierbar - ebenfalls
  dokumentierte Lücke, keine Kategorie-C-Tests dafür.
- **Manuell verifiziert:** nur ein Start-Smoke-Test (App startet, läuft
  einige Sekunden ohne Absturz, DI-Verdrahtung der neuen Services greift).
  Das eigentliche Ziehen/Loslassen (Detach/Re-Dock) sowie das tatsächliche
  Blinken auf allen drei Plattformen braucht noch eine echte Hand-Verifikation
  durch den Entwickler - insbesondere Windows/Linux, da diese Session nur
  macOS zur Verfügung hatte.

## Ursprünglicher Plan

Kombiniert zwei ursprünglich getrennte Ideen zu einem Plan (Diskussion
2026-09-24), weil beide dieselbe Zusatz-Infrastruktur brauchen: sobald Tabs
in eigenen Fenstern leben können, muss "welches Fenster zeigt Gruppe X
gerade" aufgelöst werden - genau das braucht auch das Blinken, um das
richtige Fenster statt pauschal "das" (einzige) App-Fenster zu treffen.
Ersetzt/verschmilzt die vormals separate Skizze "Taskbar-button /
Titelleiste blinken lassen" (deren API-Recherche unten übernommen ist,
ehemals eine eigene Datei, inzwischen komplett in diesen Plan aufgegangen)
und diese Datei selbst.

Ursprüngliche Ideen: "Pop a tab into its own window (for multi-monitor
setups)" und "Desktop notifications" → (Nutzerentscheidung 2026-09-13)
Taskbar-/Titelleisten-Blinken statt Toasts, beide aus der archivierten
`archipolygo_feature_ideas.md`.

## Entschiedene Eckpunkte (2026-09-24 Diskussion)

- **Ein losgelöstes Fenster zeigt genau eine Gruppe** - keine eigene
  Mini-Tableiste. Mehrere Gruppen rausziehen = mehrere Fenster.
- **Re-Docking ist Teil der ersten Version**, über dieselbe Drag-Geste wie
  das Auslösen (innerhalb der Haupt-`TabControl`-Bounds losgelassen =
  wieder andocken).
- **Kein eigener Persistenz-Zustand für losgelöste Fenster.** Beim nächsten
  App-Start sind wieder alle Gruppen in einem einzigen Fenster, exakt wie
  heute - "losgelöst" ist reiner Laufzeit-Zustand einer laufenden Session,
  keine gespeicherte Fenster-Position/-Größe/-Monitor-Zuordnung in
  `groups.json`. Das nimmt der Umsetzung den kompletten
  Multi-Window-Startup-Sequenzierungs-Teil (kein `InitializeGroupsAsync`-
  Umbau nötig, kein neues Persistenz-Schema) - `MainWindowViewModel.Groups`
  bleibt beim Start weiterhin einfach eine flache Liste, alle im
  `MainWindow`.
- **Hauptfenster schließen beendet die App komplett** - alle losgelösten
  Fenster werden dabei mitgeschlossen (kein "App läuft nur mit
  Zusatzfenstern weiter"). `ShutdownMode` bleibt beim Avalonia-Standard
  (`OnMainWindowClose`); die App muss vor dem eigentlichen Schließen aber
  aktiv alle noch offenen Zusatzfenster einsammeln und schließen (siehe
  Phase 1 unten) statt sich auf stillschweigendes OS-Verhalten zu
  verlassen.
- **Blink-Trigger:** Hint erhalten, DeathLink, sowie Progression-Items.
  Letzteres ist bereits als wiederverwendbares Signal vorhanden - siehe
  Phase 2.
- **Blink-Ende:** beim Fokussieren des betroffenen Fensters, matcht das
  native Verhalten von `FlashWindowEx` & Co. von selbst (keine eigene
  Timeout-/Dismiss-Logik nötig).

## Architektur: gemeinsames Fundament

**`IGroupWindowLocator`** (neuer Singleton-Service, in `App.axaml.cs`
registriert wie die übrigen Services) - hält die Zuordnung
`GroupId -> Window`, welches Fenster gerade welche Gruppe zeigt:

- `MainWindow` registriert sich beim Start für jede Gruppe in
  `MainWindowViewModel.Groups` und hält das bei Detach/Re-Dock aktuell.
- Ein losgelöstes Fenster registriert sich bei seiner Erzeugung für genau
  seine eine Gruppe, deregistriert beim Schließen/Re-Docken wieder für
  `MainWindow`.
- `MainWindowViewModel.Groups` bleibt die einzige Quelle der Wahrheit für
  *welche Gruppen es gibt* - eine losgelöste Gruppe wird nicht aus dieser
  Collection entfernt, nur aus der Haupt-`TabControl`-Darstellung
  ausgeblendet und stattdessen in ihrem eigenen Fenster gezeigt. Das
  vermeidet einen Zustand, der über mehrere ViewModels verstreut ist, und
  ist genau die Information, die `IGroupWindowLocator` sowieso braucht.

Diese Registry ist die einzige Abhängigkeit, die Phase 2 von Phase 1
braucht - ansonsten sind beide Phasen unabhängig umsetzbar/testbar.

## Phase 1: Tabs in eigene Fenster lösen

- **Auslösen:** dieselbe Drag-Geste wie beim
  [manuellen Tab-Reordering](Tab-Reihenfolge.md) - außerhalb der
  `TabControl`-Bounds losgelassen = neues Fenster statt Reorder. Nutzt die
  bereits generisch gehaltene `GroupReorderDragDrop`
  (`Views/GroupReorderDragDrop.cs`) unverändert weiter - deren Payload
  (`GroupViewModel`) und Press/Move-Threshold-Erkennung sind schon
  ziel-unabhängig, siehe deren eigenen Kommentar zu genau diesem
  Follow-up.
- **Neues Fenster:** eine einfache `DetachedGroupWindow` (Views), deren
  `DataContext` direkt die bestehende `GroupViewModel`-Instanz ist (kein
  eigenes Zwischen-ViewModel nötig, da `GroupViewModel` schon alles trägt,
  was ein Tab-Inhalt braucht - Events/Hints/Items/Filter/Chat-Dropdown).
  Titelleiste zeigt `GroupViewModel.HeaderText`.
- **Re-Docking:** Drop auf die Haupt-`TabControl`-Bounds (auch wenn das
  Zielfenster gerade das `DetachedGroupWindow` selbst als Quelle ist) baut
  den Tab wieder in `MainWindow` ein und schließt das Zusatzfenster.
- **App-Exit:** `MainWindow`s `Closing`-Handler schließt zuerst alle noch
  offenen `DetachedGroupWindow`-Instanzen (z. B. über
  `IClassicDesktopStyleApplicationLifetime.Windows`), bevor der Standard-
  Shutdown greift - ohne gespeicherten Zustand (siehe Eckpunkte oben) muss
  dabei nichts persistiert werden, es geht nur ums saubere Schließen aller
  Fenster.
- **Tests:** die eigentliche Drag-Geste lässt sich laut
  `Tab-Reihenfolge.md`s eigener Erfahrung unter Avalonia.Headless nicht
  simulieren (kein registrierter `IPlatformDragSource`) - ehrlich als
  Lücke dokumentieren, wie dort schon gehandhabt. Stattdessen Kategorie A
  gegen die eigentliche Detach-/Re-Dock-Logik direkt (z. B. eine
  `MainWindowViewModel.DetachGroup(GroupViewModel)`/`RedockGroup(...)`-
  Methode, unabhängig von der Drag-Mechanik testbar) und Kategorie C gegen
  die echten Drop-Handler mit händisch gebautem `DragEventArgs`, wie beim
  Reordering.

## Phase 2: Fenster-Blinken

API-Recherche (2026-09-13, gegen die gepinnte Avalonia-Version 12.0.4):
kein eingebautes Avalonia-API dafür - geprüft gegen die offizielle Doku zur
exakt gepinnten Version, nicht gegen eine decompilierte DLL (siehe
CLAUDE.md-Grundregel zu NuGet-Paketen). Weder `Window`/`WindowBase`
(`Activate()`, `ShowActivated`, `IsActive`, `Topmost`, `ShowInTaskbar` sind
die einzigen aktivierungs-/aufmerksamkeitsnahen Member) noch `IWindowImpl`
bieten eine Flash-/Urgency-/RequestAttention-Methode. Braucht native
Aufrufe pro OS über den nativen Fenster-Handle (`TryGetPlatformHandle()`):

- **Windows:** `FlashWindowEx` (`user32.dll`) via P/Invoke - flasht den
  Taskleisten-Button/die Titelleiste, bis das Fenster den Fokus bekommt
  (passt direkt zum entschiedenen Blink-Ende oben, kein eigener Stop-Call
  nötig).
- **macOS:** kein Titelleisten-Blinken-Konzept - `NSApplication.
  requestUserAttention` (AppKit-Interop) lässt das Dock-Icon hüpfen, bis
  die App aktiviert wird.
- **Linux:** kein einheitlicher Mechanismus - der ICCCM/X11-"Urgency Hint"
  (`_NET_WM_STATE_DEMANDS_ATTENTION`) ist der Standardweg, der
  Window-Manager entscheidet aber, was tatsächlich passiert; Wayland
  uneinheitlich je nach Compositor - akzeptierte Einschränkung, kein Fix
  dafür geplant.

Quellen: [`IWindowImpl` Referenz](https://reference.avaloniaui.net/api/Avalonia.Platform/IWindowImpl/),
[`Window`-Klassen-Doku](https://api-docs.avaloniaui.net/docs/T_Avalonia_Controls_Window)

Gleiche Form wie die (umgesetzte) Passwort-Idee
([`Passwort-Speicherung.md`](Passwort-Speicherung.md)): eine kleine
`IWindowAttentionService`-Abstraktion mit drei Plattform-Implementierungen.

- **API:** `Task FlashGroupAsync(Guid groupId)` - löst über
  `IGroupWindowLocator` das zuständige `Window` auf und ruft die
  passende Plattform-Implementierung nur auf, wenn dieses Fenster gerade
  nicht `IsActive` ist (kein Blinken für ein Fenster, das der Nutzer
  sowieso gerade anschaut).
- **Trigger-Stellen** (alle bereits vorhandene Signale, keine neue
  Klassifikation nötig):
  - Hint erhalten - dort, wo `HintService`/`SessionEventTranslator`
    aktuell schon einen neuen `HintEntry` anlegt.
  - DeathLink-Event - dort, wo das aktuell schon verarbeitet wird.
  - Progression-Item erhalten - `ReceivedItemEntry.ItemKind ==
    EventTextSegmentKind.ItemProgression` (siehe
    `Models/EventEntry.cs`/`Services/EventSegmentBuilder.
    ClassifyItemFlags`) ist exakt das gesuchte "wichtig genug"-Signal,
    schon an jeder Stelle verfügbar, die einen `ReceivedItemEntry`
    entgegennimmt.
- **Threading:** wie jede andere UI-observable Mutation aus
  `ConnectionManager`/den Services heraus über `Dispatcher.UIThread.Post`
  anstoßen (siehe CLAUDE.md-Gotcha zu Session-Callback-Threads).

## Offene Detailfragen (nicht mehr architekturrelevant, fallen während der Umsetzung)

- Genaues visuelles Erscheinungsbild von `DetachedGroupWindow`s Titelleiste/
  Fenstergröße-Default (vermutlich einfach die aktuelle `MainWindow`-Größe
  übernehmen).
- Ob ein `DetachedGroupWindow` einen sichtbaren "Grip" für den Re-Dock-Drag
  braucht (analog zu `DashboardView.axaml`s `Border.drag-handle`) oder das
  ganze Fenster/seine Titelleiste als Drag-Quelle reicht.
