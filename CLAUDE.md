# Archipolygo - notes for AI coding assistants

This file exists so a fresh Claude session can make a correct, minimal change
without re-deriving the whole architecture from scratch by reading every
file. Read this first; it links to the exact files you'll actually need.

For what the app does and how a *user* uses it, read `README.md` first -
especially its "How connections work" and "Using the app" sections. This
file is the code-level companion to that: how the same concepts are actually
implemented, and the non-obvious gotchas you'll otherwise rediscover the hard
way.

`Umsetzungsplan.md` (German) has since been updated to match reality - it
now documents both the original Phase 1-5 plan *and* the actual Phase 6
server/slot/leader rewrite that replaced it, plus everything built on top of
that rewrite since. It's a reasonable second source for *why* something is
shaped the way it is, alongside the code's own doc comments (which
consistently explain reasoning, not just mechanics).

## The core model: groups, slots, one leader connection

- `ServerConnectionGroup` (`Models/ServerConnectionGroup.cs`) = one tab = one
  Archipelago room (Host, Port, shared Password). Holds an
  `ObservableCollection<SlotProfile> Slots`.
- `SlotProfile` (`Models/SlotProfile.cs`) = one of *your* accounts in that
  room. Almost nothing lives here except `SlotName` and an optional
  per-slot `Password` override - Host/Port/shared-Password live on the group.
- `ConnectionManager` (`Services/ConnectionManager.cs`, contract in
  `Services/IConnectionManager.cs`) keeps **at most one live
  `ArchipelagoSession` per group** at a time - the "leader" - keyed by
  `GroupId -> SlotProfile.Id` in `_leaderSlotByGroup`. Switching which slot
  is the leader (`SwitchLeaderAsync`) connects the new one first and only
  disconnects the old one after that succeeds (brief deliberate overlap, no
  visible gap).
- Non-leader slots stay current *passively*: the leader's session sees the
  whole room's chat/log, so `OnLeaderMessageReceived`/`OnHintsUpdated` mirror
  any item send or hint that concerns another configured slot straight into
  that slot's own history, without ever opening a session for it.
- A slot that missed activity while the app was closed (or was just added)
  gets `CatchUpSyncAsync`: log in just long enough to pull the backlog, then
  disconnect again. This runs for every OTHER configured slot right after a
  leader connect *that brings the group online* - i.e. from inside
  `SwitchLeaderAsync` itself, guarded on `previousLeaderId is null` (no
  leader was live a moment ago), not on a timer or "regardless of
  AutoConnect" basis. A group with `AutoConnect` off gets zero network
  activity at startup (`InitializeGroupAsync` is a no-op for it) - it stays
  fully offline until the user explicitly reconnects it, at which point
  `SwitchLeaderAsync` catches every configured slot up from scratch. A
  same-group leader switch while already online (the "Chat as" dropdown)
  does **not** re-run this sweep - every sibling slot has already been kept
  current the whole time via the outgoing leader's own passive broadcast
  coverage (previous bullet), so there's no gap to close and doing it anyway
  would just be a redundant round of logins on every account switch. The
  sweep aborts immediately - not just on a manual Disconnect, but also if
  the leader itself drops unexpectedly mid-pass (checked each iteration via
  `_autoReconnectSuppressed` *or* `targetSlot` no longer being
  `_leaderSlotByGroup`'s recorded leader) - there's no group connection left
  for the remaining siblings to piggyback on either way; the next successful
  connect simply reruns the whole pass for every slot rather than trying to
  resume only whatever got skipped. `CatchUpSyncAsync` (public) and
  `SwitchLeaderAsync`/`DisconnectGroupAsync` all serialize on the same
  per-group `_groupLocks` gate now, so a slot added mid-sweep (see
  `MainWindowViewModel.AddSlotsToGroup`) just queues its own `CatchUpSyncAsync`
  call behind the gate instead of racing a second concurrent connection for
  the same server; `SwitchLeaderAsync`'s own sweep calls the ungated
  `CatchUpSyncCoreAsync` directly to avoid deadlocking on its own
  non-reentrant gate. `CatchUpSyncCoreAsync` also no-ops for a slot that's
  been removed from the group's configuration by the time its queued turn
  comes up (whether it was still waiting in the sweep's own list, or in a
  separate queued call) - a removed slot never gets connected for, sweep or
  not. Startup itself still spaces each *group's* leader-connect attempt
  `StartupGroupSpacing` apart (not per slot) so a restart with several
  AutoConnect servers doesn't hit them all with a burst of logins - see
  `MainWindowViewModel.InitializeGroupsAsync`.
- `SwitchLeaderAsync`, on success, sets `AutoConnect = true` and
  `PreferredLeaderSlotId = <that slot>` on the group and raises
  `GroupPersistNeeded` (always on the UI thread) - connecting a leader *by
  any means* (account dropdown, a brand-new server's first slot, an
  auto-reconnect after a drop) is what makes that group come back at the
  next app start, not a separate opt-in checkbox. `DisconnectGroupAsync`
  clears `AutoConnect` the same way. `MainWindowViewModel` just subscribes
  to `GroupPersistNeeded` and calls `PersistGroups()` - it never has to know
  *why* persistence was needed.
- `GroupViewModel.Slots`/`SlotFilterOptions` are **not** passthroughs to
  `Group.Slots` - they're separately maintained, reordered copies (current
  leader first, then alphabetical; see `RefreshSlotOrder`), rebuilt whenever
  a slot is added/removed or the leader changes. Don't reintroduce a plain
  `=> Group.Slots` passthrough if you touch this; it'll lose the ordering.

If you're asked to change connection/leader/reconnect behavior, start in
`ConnectionManager.cs` - it's one file, ~800 lines, and every method has a
doc comment explaining what it's for and why it's shaped that way.

## Where things live

- `Models/` - plain data (`ServerConnectionGroup`, `SlotProfile`,
  `EventEntry`, `HintEntry`, `ReceivedItemEntry`, filter enums, plus small UI
  helper records like `StagedSlot`/`ConfiguredSlotRow`/`PlayerChoice` used
  only by the connection editor dialog).
- `Services/ConnectionManager.cs` - owns all `IArchipelagoSession` instances
  (the interface `ArchipelagoSession` already implements). Talks to
  `Archipelago.MultiClient.Net` only through `ISessionFactory`
  (`Services/ISessionFactory.cs`/`ArchipelagoSessionFactoryAdapter.cs`) -
  that seam exists purely so `AvaloniaApplication1.Tests` can substitute a
  `FakeArchipelagoSession`/`FakeSessionFactory` (Kategorie B, see
  `Test-Umsetzungsplan.md`) to exercise this class's real locking/ordering
  logic without a real server; no behavior difference for the real app.
- `Services/MessageHistoryService.cs` / `HintService.cs` - turn raw session
  data into `EventEntry`/`HintEntry` and append them to a `GroupViewModel`'s
  collections; also own the per-slot "what have I already shown"
  bookkeeping (`ProfileSyncState`: last seen item index, seen hint ids).
- `Services/PersistenceService.cs` - JSON under
  `%AppData%/Archipolygo` (`groups.json`, `sync-state/<slotId>.json`,
  `settings.json`). Contains a one-time migration
  (`MigrateLegacyProfilesIfNeeded`) from the pre-Phase-6 flat-profile format
  - old profile `Id`s are preserved as the new `SlotProfile.Id` so existing
    per-slot sync state still applies after migrating.
- `ViewModels/GroupViewModel.cs` - one per tab; merged event/hint/item
  collections for every slot on that server, the account ("Chat as")
  dropdown, and the three independent slot filters.
- `ViewModels/MainWindowViewModel.cs` - owns `Groups`, add/remove/edit-group
  operations, startup sequencing, and the startup sync progress counters
  (`StartupSyncTotal`/`StartupSyncCompleted`, fed by
  `IConnectionManager.SlotInitialSyncCompleted`).
- `ViewModels/ConnectionEditorViewModel.cs` +
  `Views/ConnectionEditorWindow.axaml(.cs)` - one dialog, three modes
  (`ConnectionEditorMode`: `NewGroup`/`AddSlot`/`EditGroup`), each showing
  only the relevant fields via `IsVisible` bindings on view-model bool
  properties (`ShowSlotName`, `ShowPlayerPicker`, `ShowSlotManagement`, ...).
  Adding slots is a staging-list flow (`StagedSlots`/`StageSelectedPlayer`),
  not one dialog round-trip per slot; editing a group also manages its
  already-configured slots in place (default-leader pick, remove).
- `Views/MainWindow.axaml(.cs)` - the tab strip, per-group panels
  (Events/Hints/Items with their filters), and a handful of Avalonia-quirk
  workarounds (see below) that live in the code-behind rather than XAML
  because they need imperative logic.
- `AvaloniaApplication1.Tests/` - xUnit + `Avalonia.Headless.XUnit`, see
  `Test-Umsetzungsplan.md` for the three test categories.
  `AvaloniaApplication1.TestSupport/` holds the fakes both this project and
  `TestHarness/` share (`FakeConnectionManager`/`FakePersistenceService` for
  UI prototyping without a real connection, `FakeArchipelagoSession`/
  `FakeSessionFactory` for exercising `ConnectionManager` itself) - never
  duplicate one of these into either consuming project instead of adding to
  it here.
- `Feature-Plaene/` - one Umsetzungsplan-style `.md` per not-yet-built
  feature idea from `Claude outputs/archipolygo_feature_ideas.md` (that file
  is the index - check it first for which ideas have a plan at all, and
  whether it's done). Once a plan is actually implemented, its file moves to
  `Feature-Plaene/Archiv/` and gains a "Status: ✅ Umgesetzt" section at the
  top documenting how the real implementation ended up differing from the
  original plan (Tier 2's UI in `Fortschrittsanzeigen.md`, macOS's exclusion
  in `Auto-Update.md`, ...) - read that section before touching related code
  instead of trusting the rest of the plan doc to still match reality
  exactly.

## Non-obvious gotchas already worked around here

Don't "fix" these differently without understanding why they're shaped this
way first - each was a real bug with a specific root cause.

- **Avalonia `TabControl.ContentTemplate` materializes lazily.** A tab that
  was never the selected one since app start doesn't build its visual tree
  (including any `ComboBox` inside it) until the user actually clicks it -
  by then the view model's value may already be correct, but a *brand-new*
  `ComboBox` can still resolve its initial `SelectedItem` against
  `ItemsSource` incorrectly. Fixed with `Loaded` handlers that re-assign
  `SelectedItem` from the view model once the control actually loads (see
  `MainWindow.axaml.cs`: `OnChatSlotComboBoxLoaded`, `OnEventsListLoaded`).
- **A horizontal `StackPanel`'s children default to `VerticalAlignment="Stretch"`.**
  A plain `TextBlock` next to a taller `Button` looks top-aligned unless it
  gets an explicit `VerticalAlignment="Center"` of its own.
- **`CommunityToolkit.Mvvm`'s `[RelayCommand]` strips a trailing `Async`
  from the generated command name.** `RemoveConfiguredSlotAsync()` →
  `RemoveConfiguredSlotCommand`, not `RemoveConfiguredSlotAsyncCommand`. Easy
  to get wrong in code-behind `Click` handlers since nothing catches it at
  compile time until you look at the generated partial.
- **`System.Text.Json` silently drops a get-only `ObservableCollection<T>`
  property on deserialize** unless `PreferredObjectCreationHandling =
  JsonObjectCreationHandling.Populate` is set (see `PersistenceService`) -
  without it, `ServerConnectionGroup.Slots` comes back empty from disk every
  time, with no exception to warn you.
- **Archipelago's room roster includes slot 0 (reserved for the server
  itself, commonly named literally "Server") and item-link group entries
  (`PlayerInfo.IsGroup`).** Neither is a real, loggable-into player,
  filtered out via `ConnectionManager.FilterToRealPlayers` at every roster
  read. If you add a new call site that reads `session.Players.AllPlayers`,
  route it through that helper too.
- **Multiple slots must never connect concurrently.** When adding several
  slots at once, or catching every other configured slot up right after a
  leader connects, each `CatchUpSyncAsync` call is `await`ed one at a time
  (see `MainWindowViewModel.CatchUpNewSlotsSequentiallyAsync` and
  `ConnectionManager.SwitchLeaderAsync`'s own sibling-sync loop) - firing
  them concurrently floods the Archipelago server with simultaneous
  handshakes and some get rejected/blocked. This is now also enforced inside
  `ConnectionManager` itself, not just by callers being disciplined about
  it: `CatchUpSyncAsync` (public) acquires the same per-group `_groupLocks`
  gate as `SwitchLeaderAsync`/`DisconnectGroupAsync`, so an externally
  triggered catch-up (e.g. "Add slot" on an already-connected group) queues
  behind an in-flight sweep/switch/disconnect instead of racing it for the
  same server's one-at-a-time connection slot.
- **A `TaskCanceledException`/timeout from `ConnectSlotSessionAsync` is
  treated as transient and retried** (fresh session, up to
  `MaxTransientConnectRetries` times, see `IsTransientConnectFailure`) -
  this covers a real observed failure mode (switching leader shortly after
  another slot on the same server reconnected). A genuine login rejection
  (bad password) never throws in the first place - it comes back as a
  `LoginFailure` result and is never retried.
- **Every UI-observable mutation made from inside `ConnectionManager` goes
  through `Dispatcher.UIThread.Post`** (session callbacks fire on
  whatever thread the underlying library uses). If you add a new
  `ArchipelagoSession` event subscription, wrap anything it touches on a
  `GroupViewModel`/`ObservableCollection` the same way.

## Building, running, releasing

- `dotnet build` / `dotnet run --project AvaloniaApplication1` from the
  repo root. .NET 10 SDK required. `AvaloniaApplication1.Tests` (xUnit +
  `Avalonia.Headless.XUnit`) replaces what used to be manual screenshot/
  click verification (the former `app-testen` skill, removed once this
  project covered the same ground - see `Test-Umsetzungsplan.md`) - run it
  with `dotnet test` (or `dotnet test AvaloniaApplication1.sln` for the
  whole solution, matching what `.github/workflows/tests.yml` runs on every
  push). Three categories, see `Test-Umsetzungsplan.md` for the full
  breakdown of which to reach for depending on what kind of fix you're
  making: plain `[Fact]` logic tests (no Avalonia involved) for
  session-less helpers/services; `[AvaloniaFact]` tests exercising
  `ConnectionManager`'s real locking/ordering logic (leader switches,
  catch-up sweeps, reconnects) against a `FakeArchipelagoSession`/
  `FakeSessionFactory` (see `AvaloniaApplication1.TestSupport`) instead of a
  real Archipelago connection; and `[AvaloniaFact]` tests building the real
  `.axaml` views for layout/binding/click-behavior fixes. Being an Avalonia
  app, this also builds and runs on macOS (`osx-x64`/`osx-arm64`, see the
  release workflow below) - `dotnet test` itself needs no macOS-specific
  setup (unlike the old screenshot/click scripts it replaced).
- For a new feature *idea* that's mainly about the UI (a new panel, a new
  interaction, a layout change, ...), use the `ui-feature-prototyp` skill
  (`.claude/skills/ui-feature-prototyp/SKILL.md`) - build it roughly in
  `TestHarness/` first and have the developer click through it live, before
  writing that idea into the real app.
- `.github/workflows/release.yml` builds self-contained single-file zips
  (win-x64, linux-x64, osx-x64, osx-arm64) and attaches them to a GitHub
  Release whenever one is published - it does not run on plain pushes.
  `--self-contained true`/`-p:PublishSingleFile=true` are forced there via
  the CLI, overriding the csproj's own dev-time defaults
  (`SelfContained=false`) - don't "fix" that mismatch by changing the
  csproj, it's intentional (small/fast local builds vs. dependency-free
  release downloads).

## Conventions to follow when editing this codebase

- Doc comments explain *why*, not just *what* - match that style; a comment
  that only restates the method signature isn't pulling its weight here.
- Slot name comparisons are `StringComparison.OrdinalIgnoreCase`
  throughout (dedup checks, roster matching) - stay consistent.
- New UI text lives in English (the whole codebase and README are English),
  regardless of what language the conversation with the user is in.
- When a dialog or view needs a new mode/branch, prefer adding a bool
  computed property (`ShowXxx`) driven off an existing mode enum over a new
  code path - that's the established pattern in `ConnectionEditorViewModel`.
- **Blanket rule for every NuGet package this project references** - not
  just the three most-touched ones (`Archipelago.MultiClient.Net`,
  `CommunityToolkit.Mvvm`, Avalonia itself), but any of them, including newer
  additions like `Velopack` (see `Services/UpdateService.cs` and
  `Feature-Plaene/Archiv/Auto-Update.md`) or ones a `Feature-Plaene/*.md`
  doc still only proposes adding: when you need to understand a package's
  behavior, never decompile the locally
  installed package DLL to figure it out. Instead check that package's
  official documentation for the exact version this project references (see
  the relevant `.csproj` for the pinned version), and only if that
  documentation doesn't cover it, look at its source on GitHub (or wherever
  it's hosted) at the matching tag/version - not decompiled output, not an
  unrelated/newer version's docs. This applies to Avalonia itself just as
  much as to `Archipelago.MultiClient.Net` - Avalonia being the UI framework
  the whole app is built on doesn't make it exempt.
