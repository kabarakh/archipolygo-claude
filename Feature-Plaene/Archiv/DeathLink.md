# Umsetzungsplan: DeathLink-Support

## Status: ✅ Umgesetzt (2026-09-10)

Umgesetzt und getestet (Kategorie B, `ConnectionManagerDeathLinkTests.cs`),
mit einer bewussten Abweichung vom Plan; der Rest des Dokuments (die
`ISessionFactory`/`IDeathLinkService`-Seam-Konstruktion,
`Archipelago.MultiClient.Net.BounceFeatures.DeathLink`-API gegen 6.7.1
verifiziert) wurde exakt so umgesetzt und ist weiterhin akkurat.

**Keine `DeathLinkEnabled`-Checkbox, kein `ServerConnectionGroup.DeathLinkEnabled`.**
Der Plan sah unten (Abschnitt "Datenmodell") ein Pro-Gruppe-Flag samt Checkbox
im Verbindungs-Dialog vor, das steuert, ob `EnableDeathLink()` aufgerufen
wird. Bei der Umsetzung selbst nachträglich korrigiert: der "DeathLink"-Tag
ist ein reines Server-Protokoll-Opt-in dafür, dass der Server diese
Bounce-Pakete überhaupt an diese Session weiterleitet - kein "Senden" und
keine Aktion, die diese App im Namen des Nutzers ausführt. Da Archipolygo
nie einen DeathLink sendet und nie darauf reagiert außer ihn anzuzeigen (kein
Pausieren, kein simuliertes Sterben), gibt es nichts, wovor ein deaktiviertes
Flag den Nutzer schützen würde - `ConnectionManager.ConnectSlotSessionAsync`
ruft `EnableDeathLink()` deshalb jetzt bedingungslos für die Leader-Session
auf. Ergebnis: eine Konfigurationsfläche und ein Modell-Property weniger, bei
exakt gleichem beobachtbarem Verhalten für den Nutzer (DeathLinks erscheinen
im Event-Log jedes Rooms, das sie überhaupt unterstützt).

Ergänzt `archipolygo_feature_ideas.md` ("DeathLink support — surface and relay
DeathLink events for games that use it").

## Ziel

Für Gruppen, die DeathLink aktiviert haben: eingehende DeathLinks (jemand im
Multiworld ist gestorben) sichtbar im Event-Log markieren.

**Ausdrücklich kein Sende-Button.** Normale Archipelago-Clients lösen einen
DeathLink nicht per manuellem Knopfdruck aus, sondern weil die jeweilige
`apworld`/Spielintegration einen echten In-Game-Tod erkennt und das dann
automatisch meldet. Archipolygo ist kein Spiel-Client - es hat gar keinen
Zugriff auf den tatsächlichen Spielzustand eines Slots und könnte einen
"Tod" nur erfinden, nicht echt feststellen. Ein "Death!"-Button hier wäre
also kein Abbild dessen, wie DeathLink normalerweise funktioniert, sondern
eine Fehlbedienung, die zu falschen Signalen an alle anderen DeathLink-
Spieler im Room führen würde. Dieser Plan deckt deshalb **nur** das
Empfangen und Anzeigen ab, nicht das Senden.

## Kernbefund: `DeathLinkService` braucht den konkreten `ArchipelagoSession`-Typ

`Archipelago.MultiClient.Net.BounceFeatures.DeathLink` (Paketversion 6.7.1,
Quelle direkt geprüft, nicht geraten) bringt bereits alles Nötige mit -
**kein eigenes Bounce-Packet-Handling nötig**:

- `DeathLinkProvider.CreateDeathLinkService(this ArchipelagoSession session)`
  - eine Extension-Method auf dem **konkreten** `ArchipelagoSession`-Typ, nicht
    auf `IArchipelagoSession`.
- `DeathLinkService` selbst bietet u. a. `OnDeathLinkReceived`-Event,
  `EnableDeathLink()`/`DisableDeathLink()` (setzt/entfernt den
  `"DeathLink"`-Tag auf der Connection - das ist es, was die Serverseite
  tatsächlich entscheidet, ob dieser Client DeathLinks empfängt) sowie
  `SendDeathLink(DeathLink)`, das dieser Plan bewusst **nicht** anbindet
  (siehe "Ziel" oben - kein Sende-Button).
- `DeathLinkService`s eigener Konstruktor ist `internal` - er lässt sich also
  **nur** über die Extension-Method erzeugen, nicht direkt `new`en.

Das kollidiert mit dem gerade abgeschlossenen Kategorie-B-Seam
(`Test-Umsetzungsplan.md`): `ConnectionManager` hält seit dessen Umbau nur noch
`IArchipelagoSession`, nicht mehr den konkreten Typ - ein Cast
`(session as ArchipelagoSession)?.CreateDeathLinkService()` würde für echte
Sessions funktionieren, aber bei jeder `FakeArchipelagoSession` in Tests
stumm `null` liefern (kein Absturz, aber auch keine Testbarkeit).

**Lösung, konsistent mit dem bestehenden Seam-Muster:** `ISessionFactory` um
eine zweite Methode erweitern:

```csharp
public interface ISessionFactory
{
    IArchipelagoSession CreateSession(string host, int port);
    IDeathLinkService? CreateDeathLinkService(IArchipelagoSession session);
}
```

`IDeathLinkService` ist ein neues, kleines, selbst definiertes Interface (in
`Services/`) - nur mit dem, was für den Empfang gebraucht wird:

```csharp
public interface IDeathLinkService
{
    event Action<DeathLink> OnDeathLinkReceived;
    void EnableDeathLink();
    void DisableDeathLink();
}
```

Kein `SendDeathLink` im eigenen Interface - konsequent mit "kein
Sende-Button" oben; nur was tatsächlich gebraucht wird, gleiches Prinzip wie
bei den übrigen Fakes in `AvaloniaApplication1.TestSupport`
("Rest wirft bewusst `NotImplementedException`", hier: Rest existiert gar
nicht erst im Wrapper-Interface).

`ArchipelagoSessionFactoryAdapter.CreateDeathLinkService` castet intern auf
`ArchipelagoSession` und ruft die Extension-Method auf (gibt `null` zurück,
falls der Cast fehlschlägt - sollte im echten Betrieb nie passieren, da
`ArchipelagoSessionFactoryAdapter.CreateSession` immer eine echte
`ArchipelagoSession` liefert). Für Tests: `FakeDeathLinkService : IDeathLinkService`
in `AvaloniaApplication1.TestSupport`, von `FakeSessionFactory.CreateDeathLinkService`
zurückgegeben - macht das ganze Feature Kategorie-B-testbar, ohne echten Server.

## Datenmodell

- `ServerConnectionGroup.DeathLinkEnabled` (bool) - **pro Gruppe, nicht pro
  Slot**, konsistent mit `AutoConnect`/`Password`, die laut CLAUDE.md bewusst
  "einmal pro Server" statt pro Slot leben: nur der Leader hat je eine
  Session, DeathLink-Tags sind ohnehin nur für die aktuell verbundene Session
  wirksam - ein Konzept "pro Slot" hätte hier keine beobachtbare Wirkung.
- In `ConnectionEditorViewModel`/`ConnectionEditorWindow.axaml`: eine
  CheckBox "DeathLink" neben `AutoConnect` (gleiche `ShowXxx`-Sichtbarkeits-
  Konvention wie die anderen Modus-abhängigen Felder).

## ConnectionManager-Anbindung

In `ConnectSlotSessionAsync`, nur für `isLeaderSession == true` (nur der
Leader hat eine lebende Session, gleiche Begründung wie beim
`MessageLog.OnMessageReceived`-Abo):

1. Nach erfolgreichem Login: `_sessionFactory.CreateDeathLinkService(session)`.
2. Ist `group.Group.DeathLinkEnabled`: `EnableDeathLink()` aufrufen, sonst
   nichts (Tag bleibt aus).
3. `OnDeathLinkReceived` abonnieren → über `IMessageHistoryService` einen
   neuen `EventEntry` anlegen (neuer `EventType.DeathLink`, eigene Farbe/Icon
   via `EventTextSegmentKind`, analog zu `HintReceived`/`ItemReceived`).

Keine eigene Verwaltung/kein eigenes Dictionary für die `IDeathLinkService`-
Instanz nötig (anders als `_sessions`) - es gibt nichts, was sie später
wiederfinden müsste (kein Senden, siehe oben). Die reale `DeathLinkService`
meldet sich intern selbst bei `socket.PacketReceived` an, hält sich also
über diese Referenz am Leben, solange die Session offen ist, und stirbt
automatisch mit dem Socket beim Leader-Wechsel/Disconnect - kein
explizites Dispose, kein Aufräum-Code nötig.

## UI

Empfangene DeathLinks laufen automatisch durchs bestehende Event-Log - keine
neue UI-Fläche nötig, nur ein neuer `EventTextSegmentKind`/eine neue Farbe in
`Converters/EventTextSegmentKindToBrushConverter`, damit sie optisch
auffallen (z. B. dunkelrot). Die einzige neue Bedienfläche ist die
`DeathLinkEnabled`-Checkbox im Verbindungs-Dialog (siehe Datenmodell oben) -
sonst nichts.

## Tests (siehe Test-Umsetzungsplan.md-Konventionen)

- **Kategorie B**: `ConnectionManager` aktiviert `EnableDeathLink()` nur wenn
  `DeathLinkEnabled` gesetzt ist (und lässt es bei deaktivierter Gruppe
  unangetastet); ein über `FakeDeathLinkService.RaiseDeathLinkReceived(...)`
  simulierter eingehender DeathLink erzeugt genau einen passenden
  `EventEntry` mit dem neuen `EventType.DeathLink`.
- **Kategorie A**: keine (das Feature hat keine sinnvolle session-lose reine
  Logik jenseits der Modell-Property).

## Offene Detailfrage für die Umsetzung

Serverseitig muss der Room selbst DeathLink unterstützen (spielabhängig) -
der Client kann das nicht vorab erkennen, außer implizit daran, dass nie ein
DeathLink ankommt. Für die erste Version reicht "User aktiviert es manuell,
UI zeigt keine Warnung falls das Spiel es nicht unterstützt" - eine
Erkennung wäre ein separates, kleineres Follow-up.
