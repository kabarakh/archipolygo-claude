# Feature-Idee (grobe Skizze): Passwörter nicht speichern

**Status: 🗨️ Muss noch durchdiskutiert werden** - kein Umsetzungsplan im
Sinne von `Tab-Reihenfolge.md`, aber schon deutlich weiter konkretisiert als
die meisten anderen offenen Ideen. Ersetzt die ursprüngliche Idee
"Encrypted password storage / OS keychain integration" aus der inzwischen
archivierten Datei `archipolygo_feature_ideas.md`.

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

## Noch offen

- Wie/wo wird `RequiresPassword` pro Gruppe/Slot ermittelt und
  gespeichert - vermutlich beim ersten erfolgreichen Connect gesetzt
  (das Passwort wurde gebraucht), aber wie erkennt man später, dass ein
  zuvor passwortloser Server plötzlich eins braucht (oder umgekehrt)?
- Was passiert bei einem falschen Passwort im konsolidierten Dialog - alle
  Felder erneut anzeigen, oder nur die betroffene Gruppe?
- **Überschneidung mit ["Config-Export/Import"](Config-Export-Import.md)**:
  falls diese Idee zuerst umgesetzt wird, enthält `groups.json` keine
  Passwörter mehr, was einen Export automatisch "sicherer" macht - die
  beiden Ideen sollten nicht unabhängig voneinander geplant werden.
