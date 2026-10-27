# Archipolygo - notes for AI coding assistants

This file helps a new Claude session to make a correct, minimal change. With
it, the session does not have to read every file to learn the architecture.
Read this file first. It links to the files that you need.

For what the app does and how a *user* uses it, read `README.md` first.
Read especially its sections "How connections work" and "Using the app".
This file is the code-level companion to the README:

- It tells how the code implements the same concepts.
- It lists the non-obvious gotchas. Without this list, you find them again
  the hard way.

`Umsetzungsplan.md` (German) agrees with the current code. It documents:

- the original Phase 1-5 plan,
- the Phase 6 server/slot/leader rewrite that replaced that plan,
- all work built on top of that rewrite.

Use it as a second source for *why* something has its shape, together with
the doc comments in the code. Those comments explain the reason, not only
the mechanics.

## The core model: groups, slots, one leader connection

- `ServerConnectionGroup` (`Models/ServerConnectionGroup.cs`) is one tab.
  One tab is one Archipelago room (Host, Port, shared Password). The group
  holds an `ObservableCollection<SlotProfile> Slots`.
- `SlotProfile` (`Models/SlotProfile.cs`) is one of *your* accounts in that
  room. It holds almost nothing: `SlotName` and an optional per-slot
  `Password` override. Host, Port and the shared Password are on the group.
- `ConnectionManager` (`Services/ConnectionManager.cs`, contract in
  `Services/IConnectionManager.cs`) keeps **at most one live
  `ArchipelagoSession` per group** at a time. This session is the "leader".
  `_leaderSlotByGroup` maps `GroupId -> SlotProfile.Id`.
  - `SwitchLeaderAsync` changes the leader slot. It connects the new slot
    first. It disconnects the old slot only after the connect succeeds.
  - This overlap is brief and deliberate: the user sees no gap.
- Non-leader slots stay current *passively*:
  - The leader's session sees the chat and log of the whole room.
  - `OnLeaderMessageReceived` and `OnHintsUpdated` copy each item send or
    hint for another configured slot into that slot's own history.
  - The app opens no session for that other slot.
- A slot can miss activity while the app is closed, or the user can add a
  new slot. Then the slot gets a catch-up: the app logs in as that slot,
  pulls the backlog and disconnects again. All catch-ups of all groups go into one app-wide queue
  (`Services/CatchUpQueue.cs`). One worker processes the queue in order, one
  slot at a time.
  - `SwitchLeaderAsync` appends every other configured slot to the queue.
    It does this only when the connect brings the group online
    (`groupJustCameOnline`, that is `previousLeaderId is null`). No timer
    and no `AutoConnect` setting queues catch-ups on its own.
    `SwitchLeaderAsync` does not wait for these catch-ups.
  - A leader switch while the group is online (the "Chat as" dropdown)
    adds no catch-ups. The outgoing leader kept every sibling slot current
    through its passive broadcast coverage (previous bullet).
  - A group with `AutoConnect` off has zero network activity at startup
    (`InitializeGroupAsync` does nothing for it). When the user connects it,
    `SwitchLeaderAsync` queues every configured slot.
  - When the group goes offline (manual Disconnect or an unexpected leader
    drop), the app removes the group's pending entries from the queue
    (`CatchUpQueue.RemoveGroup`). The next successful connect queues every
    slot again.
  - `CatchUpSyncAsync` (public, for example from "Add slot") appends one
    slot to the same queue. The queue does not add a slot again while that
    slot is pending. `CatchUpSyncCoreAsync` does nothing for a slot that was removed
    from the group's configuration.
  - Startup starts each *group's* leader connect `StartupGroupSpacing`
    apart, but does not wait for the previous one
    (`MainWindowViewModel.StartSpacedAsync`). Thus an open password prompt
    for one server does not stop the other servers.

  See Umsetzungsplan.md, "Globale Connect-Sperre und Catch-up-Queue".
- When `SwitchLeaderAsync` succeeds, it does these steps:
  - It sets `AutoConnect = true` on the group.
  - It sets `PreferredLeaderSlotId = <that slot>` on the group.
  - It raises `GroupPersistNeeded`, always on the UI thread.

  Thus each leader connect makes the group come back at the next app start.
  The source of the connect is not important: the account dropdown, the
  first slot of a new server, or an auto-reconnect after a drop. There is no
  separate opt-in checkbox. `DisconnectGroupAsync` clears `AutoConnect` in
  the same way. `MainWindowViewModel` subscribes to `GroupPersistNeeded` and
  calls `PersistGroups()`. It does not know *why* persistence is necessary.
- `GroupViewModel.Slots` and `SlotFilterOptions` are **not** passthroughs to
  `Group.Slots`.
  - They are separate copies with their own order: the current leader
    first, then alphabetical (see `RefreshSlotOrder`).
  - The view model rebuilds them when a slot is added or removed, or when
    the leader changes.
  - If you change this code, do not add a plain `=> Group.Slots`
    passthrough again. It loses the order.

To change connection, leader or reconnect behavior, start in
`ConnectionManager.cs`.

- It is one file. It grew far past its original ~800 lines, because much of
  the behavior in this document went into it.
- Each method has a short doc comment that tells its purpose.
- The *extended* why-rationale for its difficult parts is not inline. It is
  in Umsetzungsplan.md, section "ConnectionManager: Locking,
  Nebenläufigkeit und Workarounds im Detail". Examples: the locking order,
  the transient-retry/password-retry flow, the socket-cleanup reflection
  workaround, the global connect gate and the catch-up queue.
- This move happened on 2026-09-24. At that time the file had ~2100 lines,
  and approximately half of them were comments.
- Each related comment in the code points to its subsection by name. Read
  that subsection before you change the behavior that it explains.
- If the reasoning changes, update the Umsetzungsplan.md section, not only
  the one-line pointer.

## Where things live

- `Models/` - plain data:
  - `ServerConnectionGroup`, `SlotProfile`, `EventEntry`, `HintEntry`,
    `ReceivedItemEntry` and the filter enums.
  - Small UI helper records, for example `StagedSlot`, `ConfiguredSlotRow`
    and `PlayerChoice`. Only the connection editor dialog uses them.
- `Services/ConnectionManager.cs` owns all `IArchipelagoSession` instances
  (`ArchipelagoSession` already implements this interface). It also owns all
  connection, leader and locking state (`_sessions`, `_leaderSlotByGroup`,
  `_groupLocks`, ...).
  - It talks to `Archipelago.MultiClient.Net` only through
    `ISessionFactory` (`Services/ISessionFactory.cs`/`ArchipelagoSessionFactoryAdapter.cs`).
  - This seam exists only for `AvaloniaApplication1.Tests`. The tests
    replace it with a `FakeArchipelagoSession`/`FakeSessionFactory`
    (Kategorie B, see `Test-Umsetzungsplan.md`). Thus the tests exercise the
    real locking and ordering logic of this class without a real server.
    The real app behaves the same.
  - Two parts needed almost none of that state. They are in their own
    classes. `ConnectionManager` creates one instance of each itself; DI
    does not register them separately:
    - `Services/SocketCleanup.cs` closes the socket of a session. It also
      holds the reflection-based memory-leak workaround.
    - `Services/SessionEventTranslator.cs` turns raw session events (chat,
      item receipts, hint updates, location progress) into
      `IMessageHistoryService`/`IHintService` calls. It also owns the
      missing-locations cache of the Hint picker.
  - `ConnectionManager` still *subscribes* to the session events (inside
    `ConnectSlotSessionAsync`). From those subscriptions, it calls the
    methods of `SessionEventTranslator`.
  - `ConnectionManager` also creates one instance of
    `Services/ConnectGate.cs` and one of `Services/CatchUpQueue.cs`. They
    hold the app-wide connect gate and the catch-up queue (see the gotcha
    "Only one connect handshake runs at a time").
- `Services/AttentionTracker.cs` (`IAttentionTracker`) is the single place
  for every "this deserves attention" trigger: hint, DeathLink, item, chat
  (see `Models/AttentionCategory.cs`). Every trigger reports to it.
  - It applies the per-category settings, the per-server mute and the blink
    throttling.
  - It keeps each `GroupViewModel.UnreadAttentionCount`. This count drives
    the tab badge, the Dashboard and the window titles. It replaced the
    older `UnreadEventCount`, which counted every event of an own slot.
  - It resets the count via `RefreshSeenState` when the group is on screen
    (`IGroupHostWindow`). The Dashboard deliberately does not count as on
    screen.
  - The same count also drives the native taskbar-overlay/Dock badge of
    `IUnreadBadgeService`. The code-behind of `MainWindow` and
    `DetachedGroupWindow` wires this badge.
  - That code-behind applies the badge again on `ScalingChanged`. The
    Avalonia Win32 backend itself clears overlays on each DPI change.
  - Then `AttentionTracker` calls `IWindowAttentionService`
    (`Services/WindowAttentionService.cs`). That service is only the
    platform layer: it resolves the window, checks is-active, and does the
    actual FlashWindowEx/Dock-bounce/X11 call.
  - Trigger sites report only *live* activity, never catch-up backlog.
  - See `Benachrichtigungen.md` in the feature-plan archive. Its status
    section lists how the build differs from the plan. More ideas (toasts,
    sound, ...) are in `Benachrichtigungen-Erweiterungen.md` in the
    feature-plan directory.
- `Services/MessageHistoryService.cs` and `HintService.cs` turn raw session
  data into `EventEntry`/`HintEntry`. They append these entries to the
  collections of a `GroupViewModel`. They also own the per-slot "what have I
  already shown" bookkeeping (`ProfileSyncState`: last seen item index, seen
  hint ids).
- `Services/PersistenceService.cs` writes JSON in the data directory
  (`groups.json`, `sync-state/<slotId>.json`, `settings.json`).
  - The data directory is `%AppData%/Archipolygo`. In portable mode it is
    `data/` next to the executable.
  - `Services/DataDirectoryResolver.cs` decides this path once at startup.
    `App.axaml.cs` gives the path to `PersistenceService` and
    `DiagnosticLogger`. Never compute that path anywhere else.
  - `PersistenceService` contains a one-time migration (`MigrateLegacyProfilesIfNeeded`) from
    the pre-Phase-6 flat-profile format. The migration keeps the old profile
    `Id`s as the new `SlotProfile.Id`. Thus the existing per-slot sync state
    still applies after the migration.
- `ViewModels/GroupViewModel.cs` - one per tab. It holds:
  - the merged event, hint and item collections for every slot on that
    server,
  - the account ("Chat as") dropdown,
  - the three independent slot filters.

  It is a `partial class`, split by topic:
  - `GroupViewModel.cs` - core: group, connection state, ctor,
    connect/disconnect.
  - `.Slots.cs` - slot order, leader/"Chat as".
  - `.Panels.cs` - Events/Hints/Items contents, filters, counters.
  - `.Progress.cs` - Tier 1/2 location progress.
  - `.Attention.cs` - unread/badge/mute.
  - `.Chat.cs` - message box and recall history.
  - `.Goals.cs` - goal state of each slot in the room (`RoomSlotStatus`).
    Entries, segments and hints keep a reference to it. Thus a later
    goal also marks and filters older entries.
  - `.ItemLinks.cs` - item-link groups of the room (the "Item links"
    panel). Their member list also goes into `RoomSlotStatus`, for the
    tooltip on the group name. See `Item-Link-Uebersicht.md` in the
    feature-plan archive.

  Add new members to the file of their topic, not to the core file.
- `ViewModels/MainWindowViewModel.cs` owns `Groups`, the add/remove/edit
  operations for groups, the startup sequence and the startup sync progress
  counters (`StartupSyncTotal`/`StartupSyncCompleted`). The counters get
  their data from `IConnectionManager.SlotInitialSyncCompleted`. It is also a
  `partial class`:
  - `MainWindowViewModel.cs` - core: ctor, startup, settings.
  - `.Servers.cs` - add, edit and remove servers and slots.
  - `.ConfigImport.cs` - applies a config import.
  - `.TabLayout.cs` - reorder, detach/redock.
  - `.PasswordPrompt.cs`.
  - `.StatusDisplay.cs` - sync banner, unread title/badge.
  - `.Toolbar.cs` - theme button, update indicator.
- `ViewModels/ConnectionEditorViewModel.cs` +
  `Views/ConnectionEditorWindow.axaml(.cs)` - one dialog with three modes
  (`ConnectionEditorMode`: `NewGroup`/`AddSlot`/`EditGroup`).
  - Each mode shows only its fields. `IsVisible` bindings on bool properties
    of the view model control this (`ShowSlotName`, `ShowPlayerPicker`,
    `ShowSlotManagement`, ...).
  - To add slots, the user fills a staging list (`StagedSlots`/`StageSelectedPlayer`).
    There is not one dialog round-trip per slot.
  - In `EditGroup` mode, the dialog also manages the configured slots of the
    group in place: default-leader pick and remove.
- `ViewModels/AdminPanelViewModel.cs` (+ `AdminPlayerRowViewModel`,
  `AdminLoginViewModel`, `AdminPlayerDialogViewModel`,
  `AdminSettingsViewModel`, `Views/Admin*.axaml`, `Services/AdminCommands.cs`)
  is the Admin view. It is a third `RightPanelView` next to Hints/Items. It
  exists only for a group with `ServerConnectionGroup.IsAdmin` set (the "I'm
  the admin" checkbox). What it does:
  - It sends `!admin ...` lines through the plain `SendMessageAsync` on the
    leader.
  - It reads the answers of the server from
    `IConnectionManager.ServerReplyReceived`.
  - The server never links a reply to its command. A successful
    `/send`/`/release` produces only a room-wide broadcast. Thus the view
    sends one command at a time. All lines that arrive within a short
    window count as the answer.
  - It logs in again on `IConnectionManager.LeaderConnected`. The server
    logs out each new leader socket.
  - It also logs in again after a "You must first login" reply. This reply
    means that another client took the one admin seat.
  - Admin-related leader lines become `EventType.Admin`.
    `Services/AdminEventFormatter.cs` colors them. It finds player names in
    the plain text of the server.
  - The same class masks password values (`MaskPasswords`) before any text
    gets to the event log or a dialog. The app keeps the admin password in
    memory only and never persists it.
  - The player list joins `GetRoomPlayersAsync` with the
    `activity_timers`/`player_status`/`player_checks_done` data of the
    webhost tracker (see `PlayerProgress`).

  See `Admin-Funktionen.md` in the feature-plan archive, and
  Umsetzungsplan.md's section "Admin-Ansicht: ServerReplyReceived und
  LeaderConnected".
- Config export/import and portable mode (Settings' "Servers and data"
  section, wired through `ViewModels/DataManagementContext.cs`):
  - `Services/ConfigExportService.cs` reads and writes the single export
    file. The file holds a format version, the groups and optional
    settings. It never holds sync state, caches or logs.
  - Imported groups go through `PersistenceService.DeserializeGroups`. This
    is the same migration path as `LoadGroups`.
  - `Services/ConfigImportMerger.cs` is the pure mapping of imported groups
    onto local groups:
    - An imported group with the same Host+Port overwrites the local group.
    - A slot with a matching name keeps its local Id. Thus its sync state
      stays valid.
  - `MainWindowViewModel.ApplyConfigImportAsync` then applies that plan in
    place: disconnect, overwrite, reconnect with `StartupGroupSpacing`
    between the groups.
  - `Services/PortableDataSetup.cs` + `Views/PortableStartupWindow.axaml`
    run before the app builds any service. The reason: the logger creates
    its folder when it is constructed. They do the write check and the
    first-start copy/move from AppData.
  - See `Config-Verwaltung.md` in the feature-plan archive.
- `Views/MainWindow.axaml(.cs)` holds the tab strip and the panels of each
  group (Events/Hints/Items with their filters). It also holds some
  workarounds for Avalonia quirks (see below). These workarounds are in the
  code-behind, not in XAML, because they need imperative logic.
- `AvaloniaApplication1.Tests/` - xUnit + `Avalonia.Headless.XUnit`. See
  `Test-Umsetzungsplan.md` for the three test categories.
  `AvaloniaApplication1.TestSupport/` holds the fakes that this project and
  `TestHarness/` share:
  - `FakeConnectionManager`/`FakePersistenceService` for UI prototypes
    without a real connection.
  - `FakeArchipelagoSession`/`FakeSessionFactory` to exercise
    `ConnectionManager` itself.

  Never copy one of these fakes into one of the two consuming projects. Add
  to `AvaloniaApplication1.TestSupport/` instead.
- `Feature-Plaene/` is **the canonical location for feature plans, their
  archive, and mockups.**
  - **This bullet is the only place that names the actual directory
    names.** Thus a future rename changes only this bullet.
  - Everywhere else (README, the plan files, doc comments), refer to these
    by description: "the feature-plan directory", "the feature-plan
    archive", "the mockup file `xyz.html`". Use only the filename, never
    the full path. Then a moved or renamed folder does not break the
    references.
  - `Feature-Plaene/` holds one `.md` per feature idea that is not built
    yet. It is the master list (since 2026-09-14).
  - A single index file was the master list before that date. That file is
    now in `Claude outputs/Archiv/`, only as a historical record. Do not
    treat it as current.
  - Each file has its own status at the top:
    - 🗨️ **Discuss** - a rough first outline. It is not ready for
      implementation and needs a design discussion first (for example
      `Item-Trap-Icons.md`, `Tastenkuerzel.md`).
    - 📝 - an actual Umsetzungsplan-style doc, ready to implement. None is
      open now: each plan that got to this stage was built. For an example
      of such a plan before its implementation, see `Tab-Reihenfolge.md`
      below.
  - When the code implements a plan, its file moves into the `Archiv/`
    subdirectory of `Feature-Plaene/`. It gets a "Status: ✅ Umgesetzt"
    section at the top. That section documents how the real implementation
    differs from the original plan. Examples:
    - Tier 2's UI in `Fortschrittsanzeigen.md`,
    - macOS's exclusion in `Auto-Update.md`,
    - the insertion-line indicator of manual tab-reordering and its
      Dashboard-Overview support in `Tab-Reihenfolge.md`,
    - ...
  - Read that section before you touch related code. Do not trust the rest
    of the plan doc to agree exactly with the current code.
  - Mockup images/HTML that a plan file refers to (for example
    `dashboard-tab-mockup.html`, `passwort-dialog-mockup.html`) are in
    `Claude outputs/`, next to the historical archive.

## Non-obvious gotchas already worked around here

Do not "fix" these in a different way before you understand why they have
this shape. Each was a real bug with a specific root cause.

- **Avalonia `TabControl.ContentTemplate` materializes lazily.**
  - A tab that the user did not select since app start has no visual tree. This
    includes each `ComboBox` inside it. Avalonia builds the tree only when
    the user clicks the tab.
  - At that time, the value in the view model can already be correct. But a
    *brand-new* `ComboBox` can still resolve its initial `SelectedItem`
    against `ItemsSource` incorrectly.
  - The fix: `Loaded` handlers assign `SelectedItem` from the view model
    again when the control loads (see `OnChatSlotComboBoxLoaded` in
    `MainWindow.axaml.cs`).
- **The children of a horizontal `StackPanel` have
  `VerticalAlignment="Stretch"` by default.** A plain `TextBlock` next to a
  taller `Button` looks top-aligned. Give it its own
  `VerticalAlignment="Center"`.
- **A local XAML attribute value always wins over a `Style` `Setter` for the
  same property, also when the selector of the style matches.**
  - This is the same precedence rule as in WPF: local value > style setter >
    default.
  - Example: you set `BorderBrush="Transparent"` directly on an element.
    You also use a `Style Selector="Border.some-class"` to set `BorderBrush`
    conditionally. Then that Setter is permanently dead code. The local
    value always wins, with or without the class.
  - Real incident: the first drop-target design of manual tab-reordering
    (`Tab-Reihenfolge.md`) put a border around the hovered tab
    header/Overview row. An insertion-line indicator has since replaced
    that design.
  - The border never appeared. That `Border` had a local
    `BorderBrush="Transparent"`, and a `Border.drop-target` style was to
    override it to `DodgerBlue`.
  - Rule: a property can need a "normal" value *and* a conditional override
    from a style. Then only one of the two can set it locally. The rest
    goes into styles: a style rule for the default state, or nothing if the
    default of the property already matches.
  - The `Border.drag-handle` style in `DashboardView.axaml` (added for the
    reordering grip of the same feature) obeys this rule. Use it as a live
    reference.
- **A `SizeToContent="Height"`/`CanResize="False"` dialog can grow taller
  than a short screen.** Real case: a 2560x720 ultrawide, window-snapped to
  half. The Save button was off-screen, and the user was not able to resize
  the dialog.
  - Settings and the connection editor handle this with
    `Views/DialogScreenFit.cs` (called from their `ShowDialogAsync`).
  - It caps the height to the screen of the owner and moves the window back
    onto that screen.
  - In their XAML, the buttons dock at the bottom of a `DockPanel`, and the
    body sits in a `ScrollViewer`.
  - A new tall dialog must do the same.
- **A headless test hung forever on a wrapping text that contains `\n`
  inside a `SizeToContent="Height"` window.**
  - This occurred with `PortableStartupWindow`. Nobody investigated the
    root cause.
  - That dialog now shows a list of paragraphs, not one string with line
    breaks.
  - Run tests with `--blame-hang-timeout 60s`. Then a hang names its test
    and does not stall the run.
- **A plain `[Fact]` must not touch `Application.Current`** (for example
  via `ThemeService.Apply`).
  - A parallel `[AvaloniaFact]` can own it on another thread.
  - Then the call throws "a different thread owns it". This occurs only
    when the tests overlap by chance.
  - Use `[AvaloniaFact]` for such a test.
- **The `[RelayCommand]` of `CommunityToolkit.Mvvm` removes a trailing
  `Async` from the generated command name.**
  `RemoveConfiguredSlotAsync()` → `RemoveConfiguredSlotCommand`, not
  `RemoveConfiguredSlotAsyncCommand`. This error is easy in `Click` handlers
  in code-behind. The compiler does not find it. You see it only in the
  generated partial.
- **`System.Text.Json` silently drops a get-only `ObservableCollection<T>`
  property on deserialize.** To prevent this, set
  `PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate`
  (see `PersistenceService`). Without this setting,
  `ServerConnectionGroup.Slots` is empty after each load from disk, and no
  exception tells you.
- **The room roster of Archipelago includes slot 0 and item-link group
  entries (`PlayerInfo.IsGroup`).** The server itself uses slot 0. The name
  of slot 0 is usually literally "Server".
  - Neither is a real player that you can log in as.
  - `ConnectionManager.FilterToRealPlayers` removes them at every roster
    read.
  - If you add a new call site that reads `session.Players.AllPlayers`,
    route it through that helper too.
- **Only one connect handshake runs at a time in the whole app, across all
  servers.** Many simultaneous handshakes flood the Archipelago server, and
  it rejects or blocks some logins. `Services/ConnectGate.cs` enforces this
  rule. `ConnectionManager.OpenSocketAndLoginAsync` holds the gate only for
  `ConnectAsync()` and `LoginAsync()`, never during a password prompt.
  - Interactive connects (leader, Hint picker, roster probe) get the gate
    before queued catch-ups. Thus a leader connect pauses the catch-up
    queue after the current catch-up login.
  - A hung handshake releases the gate after 30 seconds.
  - Lock order is always the group's `_groupLocks` gate first, then the
    `ConnectGate`. Do not change this order, or a deadlock can occur.
  - Never open a session outside `ConnectSlotSessionAsync`. It is the only
    path through the gate.
- **`ConnectSlotSessionAsync` treats a `TaskCanceledException` or a timeout
  as transient and retries.** Each retry uses a fresh session, up to
  `MaxTransientConnectRetries` times (see `IsTransientConnectFailure`).
  - This covers a real, observed failure: a leader switch shortly after
    another slot on the same server reconnected.
  - A genuine login rejection (bad password) never throws. It comes back as
    a `LoginFailure` result, and the code never retries it.
- **Admin command quoting differs per command.**
  - MultiServer.py parses `/send`, `/send_multiple`, `/send_location` and
    `/option` with `shlex.split`. Thus these commands always get
    double-quoted names (`AdminCommands.Quote`).
  - `/release`, `/collect` and the other `@mark_raw` commands compare the
    raw rest of the line to the player name *exactly*. Quotes become part
    of the name and never match. Thus these commands send the bare name.
  - Build admin lines through `Services/AdminCommands.cs`, never by hand.
- **Each UI-observable change from inside `ConnectionManager` goes through
  `Dispatcher.UIThread.Post`.** Session callbacks fire on the thread that
  the underlying library uses. If you add a new `ArchipelagoSession` event
  subscription, wrap in the same way each access to a
  `GroupViewModel`/`ObservableCollection`.

## Building, running, releasing

- Build and run from the repo root with `dotnet build` /
  `dotnet run --project AvaloniaApplication1`. The .NET 10 SDK is necessary.
- `AvaloniaApplication1.Tests` (xUnit + `Avalonia.Headless.XUnit`) replaces
  the former manual screenshot/click checks.
  - Those checks were the `app-testen` skill. The developer removed that
    skill when this project covered the same ground (see
    `Test-Umsetzungsplan.md`).
  - Run the tests with `dotnet test`, or with
    `dotnet test AvaloniaApplication1.sln` for the whole solution.
    `.github/workflows/tests.yml` runs the latter on every push.
  - There are three categories. `Test-Umsetzungsplan.md` tells which one to
    use for which kind of fix:
    - plain `[Fact]` logic tests (no Avalonia) for helpers and services
      without a session;
    - `[AvaloniaFact]` tests that exercise the real locking and ordering
      logic of `ConnectionManager` (leader switches, the catch-up queue,
      reconnects). They use a `FakeArchipelagoSession`/`FakeSessionFactory`
      (see `AvaloniaApplication1.TestSupport`) instead of a real
      Archipelago connection;
    - `[AvaloniaFact]` tests that build the real `.axaml` views, for fixes
      to layout, bindings or click behavior.
  - The app is an Avalonia app, thus it also builds and runs on macOS
    (`osx-x64`/`osx-arm64`, see the release workflow below). `dotnet test`
    needs no macOS-specific setup. The old screenshot/click scripts needed
    such setup.
- For a new feature *idea* that is mainly about the UI, use the
  `ui-feature-prototyp` skill (`.claude/skills/ui-feature-prototyp/SKILL.md`).
  Examples of such ideas: a new panel, a new interaction, a layout change. Build the idea roughly in
  `TestHarness/` first. Let the developer click through it live. Only then
  write the idea into the real app.
- `.github/workflows/release.yml` builds self-contained single-file zips
  (win-x64, linux-x64, osx-x64, osx-arm64). It attaches them to a GitHub
  Release when someone publishes a release. It does not run on plain pushes.
  - The workflow forces `--self-contained true`/`-p:PublishSingleFile=true`
    via the CLI. These override the dev-time defaults of the csproj
    (`SelfContained=false`).
  - This mismatch is intentional: small, fast local builds and
    dependency-free release downloads. Do not "fix" it in the csproj.

## Conventions to follow when editing this codebase

- **Project documents are written in "Deutsch-STE"**, a German variant of
  ASD-STE100.
  - This covers feature plans (also in the feature-plan archive),
    `Umsetzungsplan.md`, `Test-Umsetzungsplan.md`, skills under
    `.claude/skills/` and new documents of the same kind.
  - It does not cover `README.md`, UI text, code or code comments. Those
    stay English.
  - **Exception: this file (`CLAUDE.md`) is English and uses ASD-STE100
    (Simplified Technical English), not Deutsch-STE.** Keep that style when
    you change it: short sentences (instructions ≤20 words, descriptions
    ≤25), active voice, present tense, one idea per sentence, lists for
    parallel points.
  - Load the `deutsch-ste` skill (`.claude/skills/deutsch-ste/SKILL.md`)
    before you write a Deutsch-STE document. It holds the rules and the
    glossary.
  - An existing document that is not in Deutsch-STE yet gets additions in
    its current language and style. Never mix styles. Convert the document
    as a whole only when the developer asks for it.
  - A task can read or change such a not-yet-converted document. Then tell
    the developer immediately (by file name). Ask directly whether to
    convert it. If the task edits the document, ask before the change. See
    "Hinweis bei nicht umgestellten Dokumenten" in the skill.
- Let the code carry the meaning.
  - Use an expressive method or variable name, or a small extracted helper,
    instead of a comment that explains what a block does.
    `ConnectionManager`, `GroupViewModel` and `MainWindowViewModel` follow
    this rule (refactored on 2026-10-13, with fewer comments).
  - Keep comments short: usually one line, at most a few.
  - A comment states only the *why* that the code cannot express: a
    workaround, a race, a protocol fact, a deliberate non-obvious choice.
  - Do not narrate history ("used to", "per dev request on ..."). Do not
    repeat the signature.
  - Put longer rationale into Umsetzungsplan.md. For `ConnectionManager`,
    use its section "Locking, Nebenläufigkeit und Workarounds im Detail".
    Put a one-line `See Umsetzungsplan.md, "<section>"` pointer in the
    code.
  - If the reasoning changes, update that section, not the pointer.
- Compare slot names with `StringComparison.OrdinalIgnoreCase` everywhere
  (dedup checks, roster matching). Stay consistent.
- Write new UI text in English. The whole codebase and the README are
  English. The language of the conversation with the user does not change
  this.
- A dialog or view can need a new mode or branch. Then prefer a computed
  bool property (`ShowXxx`) driven by an existing mode enum over a new code
  path. `ConnectionEditorViewModel` shows this established pattern.
- **Blanket rule for every NuGet package that this project references.**
  - It applies to all packages, not only the three most-touched ones
    (`Archipelago.MultiClient.Net`, `CommunityToolkit.Mvvm`, Avalonia
    itself).
  - It also applies to newer packages, for example `Velopack` (see
    `Services/UpdateService.cs` and `Auto-Update.md` in the feature-plan
    archive). It also applies to packages that a feature-plan doc only
    proposes to add.
  - To understand the behavior of a package, never decompile the locally
    installed package DLL.
  - Instead, read the official documentation of that package for the exact
    version that this project references. The relevant `.csproj` has the
    pinned version.
  - If that documentation does not cover the question, read the package
    source on GitHub (or its other host) at the matching tag/version.
  - Do not use decompiled output. Do not use the docs of an unrelated or
    newer version.
  - This applies to Avalonia as much as to `Archipelago.MultiClient.Net`.
    Avalonia is the UI framework of the whole app, but that does not make
    it exempt.
