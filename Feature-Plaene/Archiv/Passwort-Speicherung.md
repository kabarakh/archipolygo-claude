# Feature-Idee (grobe Skizze): Passwörter nicht speichern

## Status: ✅ Umgesetzt (2026-09-15)

Umgesetzt und getestet. Der Rest des Dokuments ist der ursprüngliche
Diskussionsverlauf und weiterhin akkurat für die Grundidee - die folgenden
Punkte weichen von dem ab, was dort zuletzt festgehalten war:

- **Ein einziges persistiertes Flag statt zwei.** Kein separates
  `ServerConnectionGroup.RequiresPassword` neben
  `SlotProfile.RequiresPassword` - nur Letzteres existiert. Die Frage "wird
  dafür ein Passwort gebraucht" lässt sich pro Slot beantworten (das
  tatsächlich verwendete `effectivePassword` ist ohnehin die
  Slot-Override-oder-Gruppen-Fallback-Berechnung), ein zweites Flag hätte
  nur unklare Vorrangfälle erzeugt ("welches Flag gewinnt, wenn beide
  gesetzt sind"). `ServerConnectionGroup.Password`/`SlotProfile.Password`
  sind wie geplant `[JsonIgnore]` - reine In-Memory/Session-Werte.
- **Mechanismus: ein `ConnectionManager.PasswordRequested`-Hook**, direkt in
  `ConnectSlotSessionAsync` verankert (`Services/ConnectionManager.cs`) -
  nicht ein separater Vorab-Scan in `MainWindowViewModel`, der vor dem
  Start-Loop läuft. Jeder Connect-Pfad (Leader-Connect, Catch-up-/
  Sibling-Sync, Hint-Picker-Probe-Connect) läuft durch dieselbe Methode und
  bekommt den Passwort-Dialog dadurch automatisch, ohne Änderungen an den
  einzelnen Aufrufstellen. Der "eine konsolidierte Dialog"-Effekt entsteht
  dadurch, dass der erste Hook-Aufruf in einer Start-Sequenz gierig alle
  aktuell offenen Gruppen/Slots einsammelt und deren Felder mit anzeigt -
  nicht dadurch, dass vorab bekannt wäre, wer alles gefragt werden muss.
- **"Skip blanks"-Button aus dem Mockup entfällt.** Laut der schon
  entschiedenen Validierungsregel ("jede Gruppe mit ungelöstem Feld bleibt
  disconnected") verhält sich ein leer gelassenes Feld + "Connect" klicken
  exakt wie "Skip blanks" - ein zweiter Button hätte nur Verwirrung
  gestiftet. Nur noch "Connect" und "Cancel" (Fenster schließen = alles
  überspringen).
- **Migration bestehender `groups.json`-Dateien** war im Diskussionsverlauf
  noch nicht ausgearbeitet: `PersistenceService.LoadGroups()` macht einen
  rohen `JsonDocument`-Pre-Pass über den JSON-Text (parallel zum normalen
  typisierten Deserialize, das `Password` jetzt stillschweigend ignoriert)
  und setzt `RequiresPassword` für jeden Slot, dessen altes File noch ein
  nicht-leeres `"Password"`-Feld hatte - braucht kein Migrations-Flag, wird
  nach dem ersten `SaveGroups()` von selbst zum No-op. Die noch ältere
  Pre-Phase-6-`profiles.json`-Migration (`MigrateLegacyProfilesIfNeeded`)
  bekam dieselbe Behandlung.
- **Nebenbei gefundener und mitbehobener Bug**: `GetRoomPlayersAsync` nahm
  als einzige Connect-auslösende Methode in `ConnectionManager` nicht das
  per-Gruppen-Lock - ein "Add slot"-Probe-Connect konnte dadurch parallel
  zu einem eigenen, gerade auf einen Passwort-Dialog wartenden
  Leader-Connect derselben Gruppe laufen. Jetzt genauso gated wie
  `GetHintableLocationsAsync` & Co.
- **Nachtrag (2026-09-15, dev feedback)**: Per-Slot-Passwortfelder werden im
  Dialog standardmäßig eingeklappt (nicht mehr immer sichtbar sobald
  `Slots.Count > 1`) und nur über einen 🔒-Toggle sichtbar gemacht -
  dieselbe Konvention wie `SelectableSlotRow.ShowOverride` im "Add
  slot"-Dialog. Grund: die überwiegende Mehrheit der Räume nutzt ein
  einziges geteiltes Passwort statt Slot-Overrides, und da jeder Slot, der
  überhaupt ein Passwort braucht, intern als "braucht eins" markiert wird
  (siehe oben, ein Flag statt zwei) - auch wenn er nur das Gruppen-Fallback
  nutzt - hätte ein Mehr-Slot-Server sonst pro Slot ein redundantes Feld
  gezeigt, obwohl das eine Gruppenfeld allein für alle reicht.
  `PasswordPromptGroupRow.SlotFieldsExpanded` startet automatisch
  ausgeklappt, wenn einer der Slots gerade ein Retry-Fehler-Feld
  (`ShowError`) trägt - ein Fehlschlag darf nie hinter dem eingeklappten
  Toggle verschwinden.

---

## Ausgangslage

Passwörter liegen aktuell im Klartext in `groups.json`
(`ServerConnectionGroup.Password`, `SlotProfile.Password` als optionaler
Slot-Override).

## Bevorzugte Richtung (2026-09-13 Diskussion)

Nicht verschlüsselt speichern, sondern **gar nicht speichern** - denselben
Instinkt anwenden, den Archipelago selbst an anderen Stellen auch verfolgt:
nur ein `RequiresPassword`-Flag pro Gruppe/Slot persistieren, und beim
tatsächlichen Connect-Zeitpunkt danach fragen. Einmal pro Session abgefragt,
danach im Speicher für die Wiederverwendung über die Leader-Switches/
Catch-up-Syncs derselben Gruppe hinweg behalten - nicht bei jedem einzelnen
Connect-Versuch neu fragen.

OS-Keychain-/Credential-Store-Integration (Windows Credential Manager,
macOS Keychain, Linux Secret Service via libsecret/GNOME Keyring/KWallet,
mit Fallback für eine GUI-Linux-Session ohne laufenden Secret-Service-Daemon
- z. B. minimale Window-Manager wie i3/sway ohne gnome-keyring/kwalletd)
wurde als Alternative durchdacht, ist aber nicht mehr die bevorzugte
Richtung: das Geheimnis gar nicht erst zu speichern ist einfacher und
umgeht die Pro-OS-Integrationsarbeit komplett.

## Start-Verhalten (2026-09-13 resolved)

`AutoConnect`/Start (`MainWindowViewModel.InitializeGroupsAsync`,
`ConnectionManager.SwitchLeaderAsync` setzt `AutoConnect = true`) verbindet
Gruppen aktuell unbeaufsichtigt beim App-Start. Ohne gespeichertes Passwort
kann eine passwortgeschützte AutoConnect-Gruppe nicht mehr stillschweigend
durchlaufen:

- **Blockieren, nicht überspringen.** Der Start wartet auf die
  Passwort-Eingabe, bevor die normale, per `StartupGroupSpacing`
  entzerrte Connect-Sequenz weiterläuft.
- **Ein konsolidierter Dialog für alle betroffenen Gruppen/Slots auf
  einmal**, statt seriell pro Gruppe zu unterbrechen. Layout-Beispiel
  (siehe Mockup `passwort-dialog-mockup.html` im Mockup-Verzeichnis):

  ```
  Server ABC Passwort
  Server B Passwort
  Server B Slot A Passwort
  Server B Slot 12 Passwort
  Server C Passwort
  ```

  Ein Feld pro Gruppen-Passwort *plus* ein Feld pro Slot mit eigenem
  Override. Das Gruppen-Feld wird immer angezeigt, auch wenn es am Ende
  ungenutzt bleibt (z. B. weil alle Slots eigene Passwörter haben) - nicht
  bedingt ausgeblendet, weil ein Slot-Passwort immer das Gruppen-Passwort
  übertrumpft.
- **Validierung pro Gruppe**: nicht "das Gruppenfeld muss ausgefüllt sein",
  sondern "jeder Slot, der ein Passwort braucht, muss eins bekommen -
  entweder über sein eigenes Feld oder über das Fallback-Gruppenfeld".
  Beispiel oben: Slot A + Slot 12 ausfüllen reicht, Server B selbst braucht
  dann kein eigenes. Eine Gruppe mit irgendeinem ungelösten Passwort bleibt
  komplett disconnected - kein Teil-Connect nur der versorgten Slots.

## RequiresPassword-Ermittlung, Fehlerfall, Wiederverwendung des Dialogs (2026-09-15 resolved)

- **Selbstheilendes Flag.** `RequiresPassword` wird pro Gruppe/Slot
  persistiert und bei jedem erfolgreichen Connect neu gesetzt - `true`,
  wenn dafür ein Passwort nötig war, `false` bei leerem Passwort. Der
  Start-Dialog fragt nur ab, was aktuell als `true` markiert ist; es gibt
  keinen separaten Erkennungsmechanismus für "hat sich seitdem geändert".
  Liegt das Flag falsch (ein bisher offener Server verlangt plötzlich
  eins, oder umgekehrt), scheitert genau dieser eine Connect-Versuch ganz
  normal wie jeder andere Login-Fehler - kein Vorab-Dialog dafür in dem
  Moment. Der nächste manuelle Reconnect fragt dann (über denselben
  Dialog, siehe unten) erneut nach und aktualisiert das Flag anhand des
  Ergebnisses.
- **Falsches Passwort im konsolidierten Dialog**: Gruppen/Slots, die
  erfolgreich verbunden haben, bleiben verbunden und werden nicht erneut
  gefragt. Nur das Feld/die Felder der Gruppe mit falschem Passwort werden
  - mit Fehlermeldung - im selben Dialog erneut zum Retry angezeigt.
- **Ein Dialog für Start und manuellen Connect.** Derselbe Aufbau
  (Gruppenfeld + eingerückte Slot-Override-Felder, siehe Mockup) wird auch
  außerhalb des Starts verwendet - z. B. "Connect" auf einer
  AutoConnect-aus-Gruppe, oder eine brandneue Gruppe zum ersten Mal
  verbinden - einfach mit nur einer Gruppe im Dialog (N=1 des
  allgemeinen Falls), kein zweiter UI-Codepfad.

## Noch offen

- **Überschneidung mit ["Config-Export/Import"](../Config-Export-Import.md)**:
  falls diese Idee zuerst umgesetzt wird, enthält `groups.json` keine
  Passwörter mehr, was einen Export automatisch "sicherer" macht - die
  beiden Ideen sollten nicht unabhängig voneinander geplant werden.
