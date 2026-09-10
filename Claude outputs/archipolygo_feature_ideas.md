# Archipolygo — Feature Ideas

Archipolygo is a desktop client (Avalonia/.NET) for managing several simultaneous [Archipelago](https://archipelago.gg/) multiworld connections at once — one tab per server room, with one live "leader" connection per room and passive updates for every other configured slot in that room. Phases 1–6 of the original build plan are implemented (tabs, events, hints, chat, auto-reconnect, catch-up sync). This is a shortlist of where it could go next.

**Status markers**: ✅ **Done** = built and shipped - its plan doc moved to `Feature-Plaene/Archiv/` with a "Status" section documenting how the real implementation ended up differing from the original plan (read that before touching related code). 📝 Plan (not yet built) = a plan doc exists in `Feature-Plaene/` but nothing's implemented yet.

## Archipelago-specific features

- **Check progress per slot** — a progress bar ("X of Y checks done"), using the location data the Archipelago session already exposes. ✅ **Done** (2026-09-09) — Plan: [`Feature-Plaene/Archiv/Fortschrittsanzeigen.md`](../Feature-Plaene/Archiv/Fortschrittsanzeigen.md) (covers per-slot *and* multiworld-wide progress; see that file's own "Status" section for how the actual Tier 2 UI ended up differing from the original plan).
- **DeathLink support** — surface and relay DeathLink events for games that use it. ✅ **Done** (2026-09-10) — Plan: [`Feature-Plaene/Archiv/DeathLink.md`](../Feature-Plaene/Archiv/DeathLink.md) (display-only, as planned; ended up with no per-group enable/disable toggle at all - see that file's own "Status" section for why).
- **One-click `!hint`** — a button that sends the hint chat command instead of typing it manually. ✅ **Done** (2026-09-10) — Plan: [`Feature-Plaene/Archiv/Hint-Eingabefeld.md`](../Feature-Plaene/Archiv/Hint-Eingabefeld.md) (ended up as a real titled dialog, not a Flyout, with the button in the tab's info/status row rather than the message row - see that file's own "Status" section).
- **Who's online** — show which other players in the room are currently connected.
- **Better item/trap icons** — game-specific icons for progression/useful/trap items instead of just color and text.

## Notifications & visibility

These three were planned early on and deliberately dropped — worth a fresh look:

- **Desktop notifications** — e.g. when a hint arrives or an important item is received while the app is in the background.
- **Tray icon** — keep running minimized, with an unread-events badge.
- **Log export** — write events/hints/chat out as text or CSV, e.g. to share in Discord.

## Overview across many tabs

- **A summary/dashboard tab** — all servers at a glance: total open hints, total unread events. 📝 Plan (not yet built): [`Feature-Plaene/Dashboard-Tab.md`](../Feature-Plaene/Dashboard-Tab.md)
- **Free-text search** across events/hints (today there's only category/slot filtering).
- **Manual tab reordering** (drag & drop) instead of only automatic host/port grouping. 📝 Plan (not yet built): [`Feature-Plaene/Tab-Reihenfolge.md`](../Feature-Plaene/Tab-Reihenfolge.md)

## Robustness & quality

- **Config export/import** — back up and restore the server/slot list (`groups.json`) when moving to another machine.
- **Encrypted password storage / OS keychain integration** — passwords currently live in plain text in `groups.json`.

## Convenience & distribution

- **Keyboard shortcuts** for common actions (next tab, focus chat input, …).
- **Pop a tab into its own window** (for multi-monitor setups).
- **Auto-update** instead of manually downloading GitHub releases. ✅ **Done** (2026-09-09) — Plan: [`Feature-Plaene/Archiv/Auto-Update.md`](../Feature-Plaene/Archiv/Auto-Update.md) (Windows/Linux only via Velopack, side-by-side with the existing manual .zip; macOS deliberately kept manual-only - see that file's own "Status" section).

## Open questions

- Were the notifications/tray-icon/log-export ideas dropped for good reasons, or just deprioritized at the time?
- Which of these areas matters most right now: Archipelago-specific features, visibility/notifications, overview at scale, or robustness/testing?
- Worth prototyping one of these in `TestHarness/` first (the project's existing workflow for UI ideas) before building it into the real app?
