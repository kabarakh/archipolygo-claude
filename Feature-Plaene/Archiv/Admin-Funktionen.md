# Admin-Funktionen

## Status: ✅ Umgesetzt (2026-09-29, Stufen 1-3 plus Release/Collect)

Umgesetzt ist das UI-Konzept unten. Vorher lief es als Prototyp im
TestHarness (zur Laufzeit in die echte Tab-Ansicht eingehängt) und wurde
vom Entwickler durchgeklickt.
- Der dritte Bereich "Admin" rechts im Tab.
- Der Passwort-Dialog mit Hinweisen.
- Die Spieler-Übersicht: Inaktivität live hochzählend, 3 Tage gelb, 5 Tage
  rot.
- Der Spieler-Dialog mit Send item und Send location.
- Der Settings-Dialog.
- Die Checkbox "I'm the admin" im Add- und Edit-Dialog.

Code-Einstieg: `AdminPanelViewModel` (siehe CLAUDE.md). Abweichungen und
Ergänzungen gegenüber dem Text weiter unten:

- **Anführungszeichen nicht bei allen Kommandos.** Die Aussage "immer in
  Anführungszeichen" stimmt nur für die per `shlex.split` geparsten
  Kommandos (`/send`, `/send_multiple`, `/send_location`, `/option`).
  `/release` und `/collect` sind `@mark_raw`: `resolve_player` vergleicht
  den Rohtext *exakt* mit dem Spielernamen, Anführungszeichen würden also
  nie matchen. Dort wird der Name ohne Quotes gesendet. Das ist in
  `AdminCommands` festgehalten und getestet. Innerhalb von Quotes escaped
  Pythons `shlex` nur `\` und `"`, nicht `$` oder Backtick wie bash.
- **Antworten erkennen.** Erfolgreiche Admin-Aktionen (`/send`,
  `/release`, `/collect`, `/send_location`) antworten dem Admin gar nicht
  direkt, nur Fehler kommen als `AdminCommandResult`. `/send` und `/release`
  erscheinen als raumweiter Broadcast, `/send_location` nur als die normale
  Item-Meldung. Der Dialog zeigt deshalb, was in 1,5 s nach dem Senden an
  Command-Results und Broadcasts ankommt. Kommt nichts, erscheint "Sent. The
  server didn't reply …". `/option` antwortet immer direkt, dort wird nur
  darauf gewartet. Details in Umsetzungsplan.md, Abschnitt "Admin-Ansicht:
  ServerReplyReceived und LeaderConnected".
- **Eigener `EventType.Admin`** (Punkt 21, nach dem ersten Test gegen
  einen echten Server nachgezogen), mit eigenem Filter-Button "Admin" im
  Events-Filter. Den gibt es im Tab nur bei "I'm the admin", im Dashboard
  nur, wenn mindestens ein solcher Server existiert. Dazu gehören:
  - das `!admin …`-Echo von jedem Spieler,
  - Login-/Logout-Antworten,
  - `AdminCommandResult`,
  - "Cheat console: …",
  - die eigenen Hinweise der App (`[Archipolygo] …`).

  Der Server schickt all das als reinen Text ohne Spieler- und Item-Teile.
  `AdminEventFormatter` findet die Namen deshalb selbst im Text und färbt
  sie wie überall: eigener Slot, verwandter Slot, andere. In `/send`-Zeilen
  wird auch das Item eingefärbt, immer in der normalen Item-Farbe, weil der
  Server gecheatete Items ohne Klassifizierung (Flags 0) verschickt.
  Admin-Echos zählen nicht als Chat-Aufmerksamkeit.
- **Passwort-Schutz.** Das Admin-Passwort liegt nur im Speicher
  (`AdminPanelViewModel`); persistiert wird es nie, das ist per Test
  gegen alle geschriebenen Dateien abgesichert. Die direkte Antwort des
  Servers auf `/option server_password` bzw. `password` wiederholt den Wert
  im Klartext, daher maskiert `AdminEventFormatter.MaskPasswords` sie. Aus
  Vorsicht maskiert es auch das Login- und Option-Echo, obwohl der Server
  das selbst schon tut. Das geschieht, bevor die Zeile ins Event-Log (und
  damit in "Copy from here") oder in einen Dialog gelangt.
- **Filter "Exclude goaled slots"** in der Spielerliste (nur mit Tracker),
  nach dem ersten echten Test ergänzt.
- **Alias-Anzeige** folgt der App-Konvention "SlotName (Alias)" über
  `SlotProfile.FormatDisplayName`. Das behebt dasselbe Doppel-Alias-Problem
  wie bei den konfigurierten Slots.
- **Settings-Dialog.** `countdown_mode` und `item_cheat` stehen nicht im
  `RoomState`, beide starten deshalb auf "(unchanged)". Beim Raum-Passwort
  zeigt `RoomState` nur, *ob* eins gesetzt ist: Feld "neues Passwort"
  (leer = unverändert) plus Checkbox "Passwort entfernen" (sendet `null`).
  Der Dialog bleibt nach Save offen und zeigt die Server-Antworten.
- **Release/Collect** sind schon im Spieler-Dialog enthalten, mit
  Bestätigung. Ursprünglich war das erst für Stufe 4 vorgesehen.
- **Noch nicht umgesetzt** (Stufe 4 bzw. optional):
  - Hint für einen Spieler, Allow/Forbid release, Alias.
  - Kontextmenü auf Spielernamen (Punkt 20).
  - Sammelaktionen.
  - `/save`.
- **Neu in `IConnectionManager`:** `ServerReplyReceived`,
  `LeaderConnected`, `GetGameDataAsync` (Item- und Location-Namen für
  jedes Spiel; der Plattencache hat dafür `LocationIds` bekommen) und
  `GetRoomSettings`. `PlayerProgress` hat `LastActivity`, `ClientStatus`
  und `CheckedLocationIds` bekommen.
- **TestHarness:** TestServer ist als "I'm the admin" markiert und hat
  einen Fake-Tracker. `FakeConnectionManager` simuliert den Admin-Server
  (Passwort `admin`). Im Control Panel lassen sich Admin-Passwort,
  Tracker-ID, "anderer Client übernimmt" und Reconnect simulieren.

---

Ursprünglicher Diskussionsstand (vor der Umsetzung): Ausgangsidee: Der Host einer Welt kann sich in jedem
Client per `!admin login <Server-Passwort>` authentifizieren und danach
Serverkommandos (`!admin /<kommando>`) absetzen. Archipolygo soll dafür eine
komfortable Oberfläche bieten und dabei möglichst viele vorhandene
UI-Elemente wiederverwenden.

## Faktenlage (geprüft am 2026-09-29)

Quelle: `MultiServer.py` im Archipelago-Repo (Branch `main`) und
`Archipelago.MultiClient.Net` am Tag `v6.7.1` (die hier referenzierte
Version), jeweils im Quelltext auf GitHub - nicht aus dem Gedächtnis.

### Wie `!admin` funktioniert

- `!admin login <pw>` vergleicht mit der Server-Option `server_password`.
  Ist keins gesetzt, antwortet der Server nur "Sorry, Remote administration
  is disabled" - es gibt dann **gar keine** Admin-Funktionen.
- Die Anmeldung hängt an **genau einer Client-Verbindung**
  (`ctx.commandprocessor.client`). Serverweit kann immer nur *ein* Client
  gleichzeitig Admin sein - meldet sich jemand anderes an, verliert der
  vorherige Admin still seine Rechte. Bei Leader-Wechsel oder Reconnect ist
  die Anmeldung ebenfalls weg, weil die Socket-Verbindung eine neue ist.
- `!admin logout` meldet ab.
- Jedes `!admin ...` wird **allen** Spielern als Chatzeile angezeigt
  ("Name: !admin /send ..."). Nur das Passwort bei `login` und bei
  `/option server_password` wird serverseitig durch Sternchen ersetzt.
  Admin-Aktionen sind also für alle sichtbar. Das Passwort ist dabei
  geschützt, deshalb braucht es in der UI keine Warnung dazu.
- Antworten kommen als PrintJSON mit Typ `AdminCommandResult` (Admin-
  Kommandos) bzw. `CommandResult` (normale `!`-Kommandos). Beide Typen
  kennt die Bibliothek (`JsonMessageType`). Es sind aber reine Freitext-
  Antworten ohne Korrelations-ID zum gesendeten Kommando.

- **archipelago.gg bzw. WebHost-Räume** starten **ohne** `server_password`.
  `WebHostLib/customserver.py` übergibt `WebHostContext` dafür `""`. Die
  Fernverwaltung ist dort also zunächst abgeschaltet. Der Raum-Besitzer kann
  aber im Kommandofeld der Raumseite (`DBCommandProcessor`, ein
  `ServerCommandProcessor`) `/option server_password <pw>` eingeben. Danach
  funktioniert `!admin login <pw>` aus jedem Client. Der Wert wird im
  Spielstand mitgespeichert (`game_options`) und übersteht damit einen
  Neustart des Raums. Das Feature ist also auch für gehostete Räume
  nutzbar, braucht aber einmalig diesen Schritt auf der Webseite. Die UI
  sollte ihn erklären, wenn der Server "Remote administration is disabled"
  meldet (siehe F, Punkt 22).

### Verfügbare Admin-Kommandos (`!admin /...`)

| Kommando | Zweck |
|---|---|
| `/send <player> <item>` / `/send_multiple <n> <player> <item>` | Item an Spieler schicken, max. 100 Stück. Broadcast "Cheat console: sending ..." |
| `/send_location <player> <location>` | Location auslösen, als hätte der Spieler sie gecheckt (Name oder ID) |
| `/hint <player> <item>` / `/hint_location <player> <location>` | Hint für einen beliebigen Spieler erzeugen, ohne Hint-Punkte |
| `/release <player>` / `/collect <player>` | Release/Collect für einen Spieler erzwingen |
| `/allow_release <player>` / `/forbid_release <player>` | Release-Recht pro Spieler übersteuern |
| `/option <name> <value>` | Einstellung setzen: `hint_cost`, `location_check_points`, `release_mode`, `collect_mode`, `remaining_mode`, `countdown_mode`, `item_cheat`, `password`, `server_password`, `compatibility` |
| `/options` | Alle aktuellen Einstellungen auflisten. Achtung: zeigt als Admin auch `server_password` im Klartext |
| `/status [tag]`, `/players` | Status bzw. Online-Liste (gibt es auch ohne Admin als `!status`/`!players`) |
| `/alias <player> [name]` | Alias eines Spielers setzen |
| `/countdown [s]` | Countdown starten |
| `/save`, `/exit` | Spielstand speichern bzw. **Server beenden** |
| `/datastore` | Debug: Keys im DataStorage mit Größe |

Es gibt **kein** `/kick`.

### Inaktivitätszeit: gibt es nicht per Serverkommando

- Weder `/status` noch `!status` liefern eine Zeitangabe. Sie zeigen nur
  Verbindungen, "ready"/"finished" und `(checked/total)`.
- Der Server führt intern aber zwei Zeitstempel pro Spieler:
  `client_activity_timers` (letzter **neuer** Location-Check) und
  `client_connection_timers` (letztes Verbinden bzw. Trennen). Beide werden
  im Spielstand gespeichert.
- Die WebHost-API `/api/tracker/<tracker-id>` liefert beide als
  `activity_timers` bzw. `connection_timers`. Zusätzlich gibt sie
  `player_status` (ClientStatus) pro Spieler zurück.
- **Genau diesen Endpunkt fragt `MultiworldTrackerService` bereits ab**, für
  die Fortschrittsanzeige Tier 2. Er liest dort aber nur die Check-Zahlen.
  → Die Inaktivitätsanzeige braucht **keinen Admin-Login**, nur ein paar
  zusätzlich deserialisierte Felder. Voraussetzung ist eine gesetzte
  Tracker-ID, wie bei Tier 2.
- Das ist dieselbe Quelle wie die Spalte "Last Activity" auf der
  HTML-Trackerseite (z. B. `archipelago.gg/tracker/<id>`). `WebHostLib/tracker.py`
  (`get_room_last_activity`) rechnet dafür `jetzt - client_activity_timers`
  in Sekunden um, und die Seite formatiert diese Zahl clientseitig. Die
  JSON-API liefert stattdessen den absoluten Zeitstempel. Archipolygo
  rechnet die Dauer also selbst aus und kann sie live hochzählen, ohne
  neu abzufragen. Die Spalte "Status" der HTML-Seite (z. B. "Goal
  Completed", "Disconnected") ist der ClientStatus, in der API-Antwort
  `player_status`.
- Einschränkung: Die Werte stammen aus dem *gespeicherten* Spielstand und
  die API cacht 60 s. Sie sind also nur minutengenau und hängen vom
  Autosave-Intervall des Servers ab. Für "wer hängt seit 3 Stunden" reicht
  das.

### Raum-Einstellungen lesen: ebenfalls ohne Admin

- `!options` funktioniert für jeden Client. `server_password` wird dabei
  maskiert.
- Viele Werte liegen schon strukturiert vor: Die Bibliothek stellt über
  `session.RoomState` bereit: `HintCostPercentage`, `HintCost`,
  `LocationCheckPoints`, `HintPoints`, `Release-/Collect-/
  RemainingPermissions`, `HasPassword`, `ServerTags`, Versionen.
- Bei `/option` für Modi und Hint-Kosten schickt der Server ein
  `RoomUpdate`. `RoomState` bleibt also live aktuell.
- Archipolygo liest davon bisher nichts. Das gecachte `RoomInfoPacket`
  wird nur für die DataPackage-Checksummen genutzt.

## Brainstorming: Funktionen

Grundsatz: **Alles hier ist Admin-UI.** Es lebt ausschließlich in der
Admin-Ansicht des Tabs (siehe "UI-Konzept" weiter unten), die man erst
über eine Passwortabfrage betritt. Den Admin-Button gibt es nur bei
Servern, bei denen im Add- bzw. Edit-Dialog die Checkbox "I'm the admin"
gesetzt ist. Normale Spieler sehen davon also nichts. Das gilt auch für
Informationen, die technisch keinen Login brauchen, etwa die Inaktivität
aus der Tracker-API: Für einen normalen Spieler ist sie uninteressant, für
den Host ist sie die Grundlage, um zu entscheiden, wo er eingreift.

**Entschieden (2026-09-29): kein "Nur ansehen".** Die Admin-Ansicht zeigt
ihren Inhalt, auch die Spieler-Übersicht, erst nach erfolgreichem
`!admin login`. Auch dann, wenn einzelne Daten technisch ohne Login
verfügbar wären.

### A. Überblick / Monitoring (nach Login; Daten technisch ohne Login verfügbar)

1. **Spieler-Übersicht pro Raum**: eine Tabelle mit allen Spielern, nicht
   nur den eigenen Slots. Spalten: Name/Alias, Spiel, Fortschritt
   (`checks_done/total`), Status (verbunden / ready / playing / **goal**),
   "zuletzt aktiv vor X", "zuletzt verbunden vor X". Die Daten hält
   `GroupViewModel.MultiworldProgress` zum Teil schon, sie werden bisher
   nur zum Balken aufsummiert.
2. **Inaktivitäts-Hervorhebung** (entschieden 2026-09-29): nur farbliche
   Markierung in der Spieler-Übersicht. Ohne neuen Check seit **3 Tagen
   gelb**, seit **5 Tagen rot**. Kein eigener Attention-Trigger, keine
   konfigurierbare Schwelle.
3. **"Blockiert?"-Heuristik**: Inaktiv, nicht fertig, aber es liegen offene
   Hints auf Items, die *andere* für diesen Spieler finden müssen. Das
   deutet eher auf "wartet auf andere" als auf "hat aufgehört" hin.
   Umgekehrt: Wer schon *goal* hat, aber noch Items für andere hält, ist
   ein Kandidat für ein erzwungenes `/release`.
4. **Raum-Einstellungen anzeigen** (read-only aus `RoomState`): Hint-Kosten,
   Punkte pro Check, Release-/Collect-/Remaining-Modus. Das ist zugleich die
   Ausgangsansicht für den Einstellungs-Editor (C). Nur im Admin-Modus
   sichtbar, obwohl normale Spieler dieselben Werte per `!options` sehen
   können. Der Anwendungsfall in Archipolygo ist die Verwaltung durch den
   Host, nicht die Auskunft für Spieler.

### B. Eingreifen (Admin)

5. **Item senden**: Spieler wählen, dann Item aus dessen Spiel wählen,
   Anzahl (1-100), senden.
6. **Location auslösen** (`/send_location`): der Klassiker bei Softlocks
   oder Logikfehlern ("Spieler kommt an Check X nicht ran").
7. **Hint für beliebigen Spieler** (`/hint`, `/hint_location`), ohne
   Hint-Punkte.
8. **Release / Collect erzwingen**, und **Release erlauben/verbieten** pro
   Spieler.
9. **Sammelaktionen**: z. B. "Release für alle Spieler mit Status *goal*"
   oder "für alle, die seit mehr als N Tagen inaktiv sind". Nacheinander
   gesendet, mit Bestätigungsdialog und Vorschau der Liste.
10. **Alias setzen**, **Countdown starten**.

### C. Welt-Einstellungen (Admin)

11. **Einstellungs-Editor**: `release_mode`/`collect_mode`/`remaining_mode`
    als ComboBox (Werte serverseitig festgelegt: `goal`/`enabled`/
    `disabled`, dazu `auto`/`auto_enabled` außer bei `remaining_mode`),
    `hint_cost` (Prozent) und `location_check_points` als Zahlenfeld,
    `countdown_mode` (`enabled`/`disabled`/`auto`), `item_cheat` als
    Checkbox, Raum-Passwort ändern.
    - Aktuelle Werte aus `RoomState` bzw. `!options`, nur Änderungen
      senden.
12. `server_password` ändern: optisch abgesetzt, weil man sich damit selbst
    aussperren kann. Es gibt aber keinen Bestätigungsdialog, den gibt es nur
    bei Release/Collect. Nach einer Änderung sollte die App das im Speicher
    gehaltene Admin-Passwort auf den neuen Wert setzen, sonst schlägt der
    nächste automatische Re-Login fehl.

### D. Serververwaltung (Admin, gefährlich)

13. `/save` als harmloser Button.
14. **`/exit` wird bewusst nicht angeboten** (entschieden 2026-09-29), auch
    nicht versteckt oder mit Bestätigung. Wer den Server wirklich beenden
    will, tippt `!admin /exit` selbst in den Chat.

### E. Admin-Modus und Login

15. **Checkbox "I'm the admin"** im
    ConnectionEditor, in den Modi `NewGroup` **und** `EditGroup` (neues
    `ShowXxx`-Bool nach dem üblichen Muster). Sie wird persistiert, z. B. als
    `ServerConnectionGroup.IsAdmin`, und steuert nur, ob der "Admin"-Button
    im rechten Bereich des Tabs erscheint (siehe UI-Konzept). Das Passwort
    wird dort nicht eingegeben, sondern erst beim Klick auf "Admin"
    abgefragt.
16. **Admin-Passwort pro Server** in der Gruppe halten, nur im Speicher
    wie `ServerConnectionGroup.Password` (siehe `Passwort-Speicherung.md` im
    Feature-Plan-Archiv). Abgefragt wird es beim Klick auf "Admin", über
    einen Dialog nach dem Muster von `PasswordPromptWindow`.
17. **Automatischer Re-Login** nach jedem erfolgreichen `SwitchLeaderAsync`
    bzw. Reconnect, solange der Admin-Modus für die Gruppe aktiv ist. Sonst
    ist man nach jedem "Chat as"-Wechsel still kein Admin mehr.
18. **Admin-Status sichtbar machen**: Schild-Icon im Tab-Header, im
    Dashboard und in der Statuszeile. Wenn die Anmeldung verloren geht
    (anderer Client übernimmt, Reconnect ohne Passwort), fällt das Icon
    sichtbar weg.
19. **Passwort nicht in der lokalen Pfeil-hoch-Historie**: Das ist rein
    lokal, der Server maskiert das Passwort für andere Clients ohnehin. Die
    Historie (`RecordSentMessage`) würde ein manuell in den Chat getipptes
    `!admin login geheim` im Klartext behalten. Solche Zeilen nicht
    aufnehmen. Der Login über den Passwort-Dialog läuft sowieso nicht über
    die Historie.

### F. Weitere Ideen

20. **Kontextmenü auf Spielernamen** in Events/Hints/Items: "Item senden
    an ...", "Location auslösen für ...". Öffnet denselben Spieler-Dialog
    wie die Admin-Ansicht, nur wenn man als Admin angemeldet ist. Das ist
    der kürzeste Weg vom Problem ("X sagt im Chat, er steckt fest") zur
    Aktion. Optional, erst nach der Admin-Ansicht.
21. **Admin-Protokoll**: eigener Event-Filter bzw. eigene `EventType`
    `AdminResult` für `AdminCommandResult`-Antworten. So bleiben sie
    findbar und gehen im normalen Chat nicht unter. Log-Export
    (`Log-Export.md` im Archiv) bekäme das automatisch mit.
22. **Hinweis bei deaktivierter Fernverwaltung**: Wenn der Server "Remote
    administration is disabled" antwortet, das als klare UI-Meldung zeigen
    statt als Chatzeile. Im Admin-Bereich eventuell kurz erklären, wo man
    `server_password` setzt.

Bewusst ausgeschlossen, auch nicht als eigenes Feature:
`!remaining`/`!missing`/`!checked` als Buttons. Diese Kommandos bleiben
reine Chat-Eingaben.

## UI-Konzept (Vorschlag vom 2026-09-29)

### Admin-Ansicht als dritter Bereich rechts im Tab

- Der rechte Bereich von `GroupDetailView` schaltet heute per
  `filter-toggle`-Buttons zwischen **Hints** und **Items** um
  (`GroupViewModel.SelectedRightPanel`). Dazu kommt ein dritter Button
  **Admin**, also ein neuer Wert `Admin` im selben Enum. Filterzeile
  und Inhaltszeile blenden sich wie bei Hints/Items per
  `IsVisible`-Binding um.
- Der Button ist nur sichtbar, wenn die Gruppe als "I'm the admin"
  markiert ist (Punkt 15).
- Weil `GroupDetailView` auch in abgelösten Tab-Fenstern steckt
  (`Tab-Eigenes-Fenster.md` im Archiv), gibt es die Admin-Ansicht dort
  automatisch mit. Im Dashboard gibt es sie bewusst nicht.
- **Kopfzeile der Admin-Ansicht** (anstelle der Hint- bzw. Item-Filter):
  - Anmeldestatus mit Schild-Icon,
  - Button **Server settings...**, öffnet einen eigenen Dialog (siehe C),
  - Aktualisieren, Stand der Tracker-Daten ("Stand: vor 1 min"),
  - Logout.
- **Inhalt: Spieler-Übersicht.** Eine Zeile pro Spieler im Raum, ohne
  Slot 0 und ohne Item-Link-Gruppen (`FilterToRealPlayers`). Spalten:
  - Name/Alias, Spiel,
  - Status (Goal / Playing / Ready / Connected / Disconnected),
  - Checks `x/y`,
  - **inaktiv seit** (live hochzählend, aus `activity_timers`, ab einer
    Schwelle farblich hervorgehoben),
  - ein **Aktions-Button** pro Zeile, siehe nächster Abschnitt.
  - Eigene Slots sind markiert.
  - Sortierbar, Standard: am längsten inaktiv zuerst.
  - **Ohne Tracker-ID** (entschieden 2026-09-29): Die Übersicht zeigt nur
    Namen und Spiele aus dem Roster (`GetRoomPlayersAsync`), dazu einen
    Hinweis, dass Status, Checks und Inaktivität eine Tracker-ID
    brauchen. Die Tracker-ID trägt man im Edit-Dialog ein. Im
    Spieler-Dialog fehlt dann die Filterung erledigter Locations.

### Spieler-Dialog (nach dem Muster des Hint-Dialogs)

- Aufgerufen über den Button der Zeile. Der Spieler steht damit schon fest:
  Die Slot-ComboBox des Hint-Dialogs entfällt oder ist nur eine
  Überschrift.
- **Modus**, analog zur Item/Location-Umschaltung des Hint-Dialogs:
  - **Send item** (`/send_multiple <n> ...`): Item-Liste aus dem
    DataPackage des Spiels dieses Spielers, dazu ein Anzahl-Feld (1-100).
  - **Send location** (`/send_location`): Location-Liste aus dem
    DataPackage desselben Spiels. Die vorhandene Checkbox "bereits
    erledigte ausblenden" lässt sich wiederverwenden. Die schon
    gecheckten Locations *aller* Spieler liefert dieselbe Tracker-API
    (`player_checks_done`).
- Suchfeld und gefilterte Liste unverändert aus dem Hint-Dialog.
- Die Antwort des Servers (`AdminCommandResult`) erscheint im Dialog, der
  danach offen bleibt. So sind mehrere Aktionen hintereinander möglich.
- Erweiterungsmöglichkeiten im selben Dialog, später: Hint für diesen
  Spieler (`/hint`, `/hint_location`), Release/Collect erzwingen,
  Release erlauben/verbieten, Alias setzen.

### Server-Settings-Dialog

- Eigener kleiner Dialog, erreichbar über den Button in der Kopfzeile.
  Enthält die Felder aus C. Aktuelle Werte kommen aus `RoomState`,
  beim Speichern werden nur geänderte Werte als `/option ...` gesendet.
- `server_password` ändern ist optisch abgesetzt und extra bestätigt.
  `/exit` gibt es nicht (Punkt 14).

### Klick auf "Admin": Passwortabfrage mit Hinweisen

- Solange man nicht angemeldet ist, öffnet der Klick auf **Admin** zuerst
  einen Passwort-Dialog. Er erklärt:
  - **Wofür das Passwort ist:** das Server- bzw. Admin-Passwort
    (`server_password`), nicht das Raum-Passwort.
  - **Falls der Server keins hat:** Die Fernverwaltung ist dann
    abgeschaltet. Bei archipelago.gg setzt der Raum-Besitzer eins über
    das Kommandofeld auf der Raumseite: `/option server_password <pw>`.
    Bei Self-Hosting per `--server_password` oder in der `host.yaml`.
  - **Wann man abgemeldet wird:**
    - Wechsel des Accounts über "Chat as": Das ist eine neue
      Verbindung.
    - Verbindungsabbruch bzw. Reconnect.
    - Wenn sich ein anderer Client als Admin anmeldet. Es gibt serverweit
      nur einen Admin, und der Server meldet die Verdrängung nicht.
    - App-Neustart, weil das Passwort nicht gespeichert wird.
    - Logout.
  - In den ersten beiden Fällen meldet Archipolygo automatisch neu an,
    solange die App läuft (Punkt 17).
- Antwortet der Server mit "Remote administration is disabled" oder
  "Password incorrect", zeigt der Dialog das direkt an, statt nur eine
  Chatzeile zu erzeugen.
- Der Dialog muss per Leader-Session senden, braucht also eine bestehende
  Verbindung. Ist die Gruppe offline, ist der Admin-Button deaktiviert oder
  zeigt einen Hinweis.

## Wiederverwendung vorhandener UI

| Bedarf | Vorhandenes Element | Anpassung |
|---|---|---|
| Spieler-Dialog (Item senden / Location auslösen) | **Hint-Dialog** (`HintPickerWindow` + `HintPickerViewModel`): Modus Item/Location, Suchfeld, gefilterte Liste, Checkbox zum Ausblenden | Der Spieler steht fest, die Slot-ComboBox entfällt. Die Item-Liste kommt aus dem DataPackage-Cache pro Spiel (gibt es schon, pro Spiel und Checksumme). Die Location-Liste kommt aus den DataPackage-Location-Namen des Spiels, gefiltert über `player_checks_done` aus der Tracker-API. Der bisherige "missing locations"-Cache kennt nur eigene Slots. Dazu kommen ein Anzahl-Feld und eine Antwortanzeige. Offen ist, ob man dafür `HintPickerViewModel` erweitert oder eine Schwester-Klasse mit gemeinsamer Basis baut. |
| Spielerauswahl | `GetRoomPlayersAsync` + `FilterToRealPlayers`, `PlayerChoice`, der Player-Picker aus dem AddSlot-Modus des ConnectionEditors | direkt nutzbar |
| Admin-Ansicht | Hints/Items-Umschaltung im rechten Bereich (`SelectedRightPanel`, `filter-toggle`-Buttons, `IsVisible`-Umschaltung von Filter- und Inhaltszeile) | dritter Enum-Wert `Admin` |
| Spieler-Übersicht | `MultiworldProgress` bzw. `MultiworldTrackerService` (fragt `/api/tracker` schon ab), Listen-Stil der Hints-Liste | zusätzlich `activity_timers` und `player_status` deserialisieren |
| Passwortabfrage | `PasswordPromptWindow` / `PasswordPromptViewModel` | eigener, einfacherer Dialog mit Hinweistexten, aber derselbe Aufbau |
| Server-Settings-Dialog | Feld-Layout des ConnectionEditors bzw. SettingsWindows | eigener kleiner Dialog |
| Rückmeldungen | Event-Liste mit Filtern, `EventEntry` | neuer `EventType` für Command-Results |
| Freitext-Notausgang | Chat-Eingabe: `!admin ...` funktioniert heute schon unverändert über `session.Say` | nur die Passwort-Maskierung in der Historie ergänzen |
| Bestätigung (nur Release/Collect) | bestehende Dialog-Muster (z. B. Gruppe entfernen) | - |
| Admin-Indikator | Mute-Icon im Tab-Header bzw. Dashboard | analoges Icon |

## Technische Stolpersteine

- **Nur über die Leader-Session.** Die Anmeldung gilt pro Socket, und nur
  die Leader-Session abonniert die `MessageLog`. Antworten auf Befehle, die
  über eine temporäre Session gehen (`RunAsSlotAsync`), gehen heute
  verloren. Admin-Kommandos sollten deshalb immer über den aktuellen Leader
  laufen. Das passt auch zu "nur ein Admin-Client pro Server".
- **Antworten zuordnen.** Es gibt keine Korrelations-ID. Pragmatisch: Die
  nächste `AdminCommandResult`-Nachricht nach dem Senden gehört zum
  Kommando. Dafür Admin-Kommandos seriell senden, analog zum `_groupLocks`-
  Gate. Ob wirklich jede Antwort genau eine PrintJSON-Nachricht ist, muss
  noch geprüft werden (bei `/options` sind es mehrere `output`-Aufrufe).
- **Erfolg bzw. Fehler erkennen.** Der Server matcht Namen unscharf
  (`get_intended_text`) und antwortet bei Mehrdeutigkeit mit Freitext.
  Wenn die UI exakte Namen aus Roster und DataPackage schickt, ist das
  kaum relevant. Die Freitext-Antwort sollte trotzdem immer angezeigt
  werden.
- **Anmeldestatus ist nicht abfragbar.** Es gibt kein Kommando "bin ich
  Admin?". Der Status muss aus der Login-Antwort abgeleitet werden. Wird
  man verdrängt, meldet der Server das nicht. Man merkt es erst an einem
  "You must first login ..." beim nächsten Kommando. Darauf könnte man mit
  einem automatischen Re-Login reagieren, samt Hinweis "ein anderer Client
  hat die Admin-Anmeldung übernommen".
- **Namen immer in Anführungszeichen** (vom Entwickler bestätigt,
  2026-09-29). Spieler-, Item- und Location-Namen funktionieren in
  Anführungszeichen und brauchen sie bei Leerzeichen. Archipolygo setzt sie
  deshalb **immer**, z. B. `!admin /send "Player One" "Progressive Sword"`.
  Das ist eine Regel für alle Argumente, keine Fallunterscheidung.
- **Tracker-Werte sind verzögert** (siehe oben). In der UI als "ca." bzw.
  mit Stand-Zeitpunkt anzeigen.

## Mögliche Stufen

1. **Admin-Ansicht + Login**: Checkbox "I'm the admin" im Add- bzw.
   Edit-Dialog, dritter Button im rechten Bereich,
   Passwort-Dialog mit Hinweisen, Login bzw. Re-Login am Leader,
   Admin-Indikator, `AdminCommandResult` als eigener `EventType`,
   Login-Zeilen aus der lokalen Chat-Historie heraushalten. Dazu die Spieler-Übersicht
   mit Inaktivität, Status und Checks aus der Tracker-API.
2. **Spieler-Dialog**: Send item und Send location.
3. **Server-Settings-Dialog.**
4. **Später bzw. optional**: weitere Aktionen im Spieler-Dialog (Hint,
   Release/Collect, Allow/Forbid, Alias), Kontextmenü auf Spielernamen,
   Sammelaktionen, `/save`.

## Offene Fragen für die Diskussion

Bereits entschieden (2026-09-29): Die Admin-Ansicht liegt als dritter
Bereich rechts im Tab. Sichtbar ist sie nur mit der Checkbox "I'm the
admin" im Add- bzw. Edit-Dialog. Es gibt kein "Nur ansehen". Ohne
Tracker-ID genügt ein Hinweis. `/exit` wird nicht angeboten. Eine
Bestätigung gibt es nur bei Release/Collect, auch bei Sammelaktionen damit.
Inaktivität wird nur farblich markiert (3 Tage gelb, 5 Tage rot). Namen
stehen immer in Anführungszeichen.

- Da es UI-lastig ist: vor der echten Umsetzung per `ui-feature-prototyp`
  im TestHarness durchklicken, vor allem die Admin-Ansicht, den
  Spieler-Dialog und den Passwort-Dialog.
