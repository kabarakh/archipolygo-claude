# Umsetzungsplan: Auto-Update (Velopack)

## Status: ✅ Umgesetzt (2026-09-09, Commit `2a8c9bb`)

Umgesetzt und getestet, mit einigen Entscheidungen, die der Plan noch offen
gelassen hatte. Der Rest des Dokuments (Velopack-API, `vpk`-Flags) wurde
gegen Velopack 1.2.0 verifiziert und ist weiterhin akkurat.

**macOS bekommt bewusst kein Velopack-Paket.** War im Plan nicht
entschieden. Grund, der erst bei der Umsetzung auffiel: `vpk pack` für
macOS braucht `codesign`/`xcrun`/`productbuild` - nur auf echter
Mac-Hardware vorhanden, nicht cross-kompilierbar vom Linux-Runner aus, den
`release.yml` für alle anderen Targets nutzt. Zusätzlich verlangt Velopack
für macOS zwingend Code-Signing/Notarization, sonst läuft die gepackte App
u. U. gar nicht. Beides bräuchte einen echten `macos-latest`-Runner
(Kosten/Kontingent) und ein Signing-Zertifikat - beides nicht angeschafft.
macOS bleibt beim bisherigen manuellen Zip-Download, ohne Auto-Update.

**Altes Zip UND neues Velopack-Paket koexistieren** für Windows/Linux, statt
das Zip zu ersetzen - explizite Nutzervorgabe. Jedes Release enthält jetzt:
`windows-x64.zip`/`linux-x64.zip`/`macos-x64.zip`/`macos-arm64.zip` (wie
immer) plus ein zusätzliches Velopack-Paket-Set (`Setup.exe`/`.AppImage` +
`.nupkg` + `releases.{channel}.json`) für win-x64/linux-x64.

**Kein Migrationspfad automatisiert** - der Plan hatte das schon als offene
Frage markiert, sie ist weiterhin nur teilweise gelöst: die App zeigt einen
passiven Hinweis im Settings-Dialog ("du nutzt eine unverwaltete
Installation, lade stattdessen den Installer"), wenn `IsManagedInstall`
false ist (und die Plattform Velopack grundsätzlich unterstützt - nicht auf
macOS). Es gibt keine automatische Erkennung/Umleitung/Ein-Klick-Migration
von Zip zu verwalteter Installation - ein Nutzer muss den Installer selbst
einmalig manuell herunterladen und ausführen.

**Keine Delta-Updates** - `vpk pack` läuft ohne vorherigen `vpk download`-
Schritt, erzeugt also bei jedem Release ein volles Paket statt eines
kleineren Delta-Updates gegen die vorherige Version. Einfachere Pipeline,
im Plan nicht explizit entschieden.

**Tag-Namenskonvention**: ein führendes `v` wird automatisch entfernt (z. B.
`v1.2.3` → `1.2.3`), alles andere muss schon gültiges SemVer2 sein. Eine
ungültige Tag-Form lässt nur das jeweilige Velopack-Asset scheitern
(`continue-on-error: true`), nicht das ganze Release - die Zips sind zu dem
Zeitpunkt schon angehängt.


Ergänzt `archipolygo_feature_ideas.md` ("Auto-update instead of manually
downloading GitHub releases"). Nutzer-Entscheidung: vollständiges
In-Place-Update statt nur Hinweis-Banner mit Download-Link - das ist der
größere Eingriff der sechs Pläne, weil er die komplette Release-Pipeline
ersetzt, nicht nur eine neue In-App-Funktion hinzufügt.

## Warum das die Release-Pipeline verändert, nicht nur die App

[Velopack](https://docs.velopack.io/) paketiert die App nicht nur, sondern
**ersetzt** den heutigen Build-Schritt: aktuell erzeugt `release.yml` über
`dotnet publish --self-contained --single-file` + `zip` vier eigenständige
Downloads (win-x64, linux-x64, osx-x64, osx-arm64). Velopacks eigenes
Tool (`vpk`) übernimmt Paketierung *und* Delta-Updates in einem eigenen
Format - das ist kein additiver Schritt neben dem bestehenden Zip-Build,
sondern ersetzt ihn. Velopack selbst bewirbt sich als "hostbar auf GitHub
Releases", die grobe RID-Matrix (win/osx-x64/osx-arm64/linux) passt zu dem,
was `release.yml` heute schon baut - die Kompatibilität mit
`SelfContained=true`/`PublishSingleFile=true` im Detail (insbesondere
macOS: `osx-arm64` + `osx-x64` als zwei getrennte Channels oder ein
Universal-Build) ist zum Planungszeitpunkt nicht abschließend verifiziert
und muss vor der Umsetzung anhand der aktuellen Velopack-Doku zur exakt zu
verwendenden Paketversion geprüft werden (gleicher Grundsatz wie bei
`Archipelago.MultiClient.Net`: die Doku zur tatsächlich gepinnten Version
lesen, nicht die allgemeine Startseite).

## Grobe Schritte

1. **Velopack-Paket referenzieren** (`Velopack` NuGet-Paket in
   `AvaloniaApplication1.csproj`), `VelopackApp.Build().Run()` als aller-
   erste Zeile in `Program.cs`s `Main` (vor `BuildAvaloniaApp()`, laut
   Velopacks eigenem Muster nötig, damit es Update-/Install-Betriebsmodi der
   App abfangen kann, bevor die eigentliche Avalonia-App startet).
2. **`release.yml` umbauen**: `dotnet publish` bleibt (Velopack braucht
   weiterhin einen normalen Self-contained-Build als Rohmaterial), aber
   danach `vpk pack` statt `zip` - erzeugt Velopacks Installer-/Update-
   Artefakte statt der heutigen Zip-Datei. `-p:InformationalVersion=${{
   github.event.release.tag_name }}` (für `AppVersionInfo`) bleibt
   unverändert bestehen; Velopacks eigene Versionsnummer (für
   Delta-Updates) muss zusätzlich sauber aus demselben Tag abgeleitet
   werden - Velopack verlangt vermutlich ein strengeres SemVer-Format als
   die bisherigen freien Tag-Namen; das muss beim Umbau geprüft und ggf.
   die Tag-Namenskonvention selbst mit angepasst werden.
3. **In-App-Update-Check**: `Services/IUpdateService.cs`/`UpdateService.cs`
   - kapselt Velopacks `UpdateManager` (`CheckForUpdatesAsync`,
   `DownloadUpdatesAsync`, `ApplyUpdatesAndRestart`). Aufruf beim Start
   (fire-and-forget, nach `App.axaml.cs`s DI-Setup, nie den Start
   blockieren, nie bei Fehler - z. B. offline - crashen: try/catch, stiller
   No-op) sowie über einen neuen Button in `SettingsWindow.axaml`
   ("Nach Updates suchen").
4. **UI für "Update verfügbar"**: bewusst **kein** Banner (zu aufdringlich,
   beansprucht dauerhaft eine ganze Zeile) - stattdessen ein kleiner
   Indikator direkt am "Settings..."-Button in der Toolbar
   (`StackPanel DockPanel.Dock="Top"` in `MainWindow.axaml`). Gleiches
   Badge-Idiom wie der bestehende Ungelesen-Badge im Tab-Header
   (`Border Background="OrangeRed" CornerRadius="8" MinWidth="16" Height="16"`
   um eine kleine `TextBlock` in `TabControl.ItemTemplate`) - hier als
   kleiner undezenter Punkt, der den "Settings..."-Button überlagert
   (`Grid` um Button + `Border CornerRadius="8" Width="8" Height="8"
   HorizontalAlignment="Right" VerticalAlignment="Top"
   IsVisible="{Binding IsUpdateAvailable}"`), keine eigene Beschriftung.
   Klick auf den Punkt (oder auf "Settings..." selbst, wenn der Punkt
   sichtbar ist) öffnet ein `Flyout` mit der neuen Versionsnummer und einem
   "Jetzt aktualisieren"-Button, der `DownloadUpdatesAsync` +
   `ApplyUpdatesAndRestart` auslöst (schließt und startet die App neu - der
   Nutzer muss vorher explizit zustimmen, nicht automatisch im Hintergrund
   neu starten, während er ggf. gerade in einer verbundenen Session ist).
   Der Punkt bleibt sichtbar, bis entweder aktualisiert wurde oder die App
   neu gestartet wird - kein "für diese Sitzung wegklicken"-Zustand nötig,
   da der Indikator selbst schon minimal-invasiv genug ist.
5. **`IUpdateService` fake-/mockbar machen** (Interface + Implementierung,
   gleiches DI-Muster wie überall sonst in `App.axaml.cs`), damit
   `MainWindowViewModel`s Reaktion auf "Update verfügbar" ohne echten
   Netzwerkzugriff testbar ist.

## Ausdrücklich nicht Teil dieses Plans

- Ein automatischer, unbeaufsichtigter Neustart mitten in einer laufenden
  Archipelago-Verbindung - die Zustimmung zum Neustart muss immer vom Nutzer
  kommen (Update-Banner, kein Zwangs-Popup).
- Code-Signing/Notarization für macOS/Windows (Velopack-Updates ohne Signing
  laufen typischerweise mit denselben OS-Warnungen wie heute schon beim
  manuellen Download) - das ist ein separates, größeres Thema
  (Zertifikate/Kosten), hier nicht mitgeplant.

## Tests

- **Kategorie A**: Versionsvergleich/„ist das eine echte neue Version"-Logik,
  falls Velopacks eigener `SemanticVersion`-Vergleich nicht direkt
  ausreicht - reine Logik, kein Avalonia nötig.
- **Kategorie B/C nur eingeschränkt anwendbar**: `IUpdateService` ist kein
  `IArchipelagoSession`-Konsument (kein Bezug zum Kategorie-B-Seam) und der
  eigentliche Download/Restart lässt sich in einem Headless-Test ohnehin
  nicht sinnvoll auslösen. Testbar ist nur `MainWindowViewModel`s *Reaktion*
  auf ein `IUpdateService`-Ergebnis (der Punkt am Settings-Button erscheint
  korrekt, das Flyout zeigt die richtige Versionsnummer) über eine einfache
  Fake-Implementierung von `IUpdateService` - das ist eher ein gewöhnlicher
  Kategorie-C-View-Test als ein Velopack-spezifischer.

## Offene Punkte, die vor Umsetzungsbeginn geklärt werden müssen

1. Velopack-Doku zur exakt zu pinnenden Version lesen (nicht die allgemeine
   Startseite) - insbesondere macOS-Unterstützung (Signing-Anforderungen,
   `osx-arm64`/`osx-x64`-Handling) im Detail, bevor `release.yml` angefasst
   wird.
2. Tag-/Versionsnamenskonvention für Releases ggf. anpassen (SemVer-Zwang).
3. Migrationspfad für bestehende, bereits manuell installierte Nutzer ohne
   Velopack (die erste Velopack-Version kann sich nicht selbst "von außen"
   installieren - das erste Update auf Velopack hin bleibt für diese Nutzer
   ein einmaliger manueller Download, erst danach greift Auto-Update).
