# Archipolygo — Feature Ideas (archiviert)

**Archiviert (2026-09-14)**: dieser Index ist retired - jede einzelne Idee
lebt jetzt in einer eigenen Datei unter `Feature-Plaene/` (offene, noch zu
diskutierende/bauende Ideen) bzw. `Feature-Plaene/Archiv/` (bereits
umgesetzte). Diese Datei bleibt nur als historischer Diskussionsstand
erhalten - für den aktuellen Stand einer Idee immer die verlinkte Datei
lesen, nicht diese. Zuordnung:

| Idee | Status | Datei |
| --- | --- | --- |
| Check progress per slot | ✅ Done | [`Feature-Plaene/Archiv/Fortschrittsanzeigen.md`](../../Feature-Plaene/Archiv/Fortschrittsanzeigen.md) |
| DeathLink support | ✅ Done | [`Feature-Plaene/Archiv/DeathLink.md`](../../Feature-Plaene/Archiv/DeathLink.md) |
| One-click `!hint` | ✅ Done | [`Feature-Plaene/Archiv/Hint-Eingabefeld.md`](../../Feature-Plaene/Archiv/Hint-Eingabefeld.md) |
| Who's online | ❌ Rejected | - (siehe Begründung unten, keine eigene Datei) |
| Better item/trap icons | 🗨️ Discuss | [`Feature-Plaene/Item-Trap-Icons.md`](../../Feature-Plaene/Item-Trap-Icons.md) |
| Desktop notifications | ➡️ Superseded | durch [`Feature-Plaene/Fenster-Blinken.md`](../../Feature-Plaene/Fenster-Blinken.md) |
| Tray icon | 🗨️ Discuss | [`Feature-Plaene/Tray-Icon.md`](../../Feature-Plaene/Tray-Icon.md) |
| Log export | 🗨️ Discuss | [`Feature-Plaene/Log-Export.md`](../../Feature-Plaene/Log-Export.md) |
| A summary/dashboard tab | ✅ Done | [`Feature-Plaene/Archiv/Dashboard-Tab.md`](../../Feature-Plaene/Archiv/Dashboard-Tab.md) |
| Manual tab reordering | 📝 Plan | [`Feature-Plaene/Tab-Reihenfolge.md`](../../Feature-Plaene/Tab-Reihenfolge.md) |
| Free-text search | ❌ Rejected | - (siehe Begründung unten, keine eigene Datei) |
| Config export/import | 🗨️ Discuss | [`Feature-Plaene/Config-Export-Import.md`](../../Feature-Plaene/Config-Export-Import.md) |
| Encrypted password storage | ➡️ Superseded | durch [`Feature-Plaene/Passwort-Speicherung.md`](../../Feature-Plaene/Passwort-Speicherung.md) |
| Keyboard shortcuts | 🗨️ Discuss | [`Feature-Plaene/Tastenkuerzel.md`](../../Feature-Plaene/Tastenkuerzel.md) |
| Pop a tab into its own window | 🗨️ Discuss | [`Feature-Plaene/Tab-Eigenes-Fenster.md`](../../Feature-Plaene/Tab-Eigenes-Fenster.md) |
| Auto-update | ✅ Done | [`Feature-Plaene/Archiv/Auto-Update.md`](../../Feature-Plaene/Archiv/Auto-Update.md) |

---

Archipolygo is a desktop client (Avalonia/.NET) for managing several simultaneous [Archipelago](https://archipelago.gg/) multiworld connections at once — one tab per server room, with one live "leader" connection per room and passive updates for every other configured slot in that room. Phases 1–6 of the original build plan are implemented (tabs, events, hints, chat, auto-reconnect, catch-up sync). This is a shortlist of where it could go next.

**Status markers**: ✅ **Done** = built and shipped - its plan doc moved to `Feature-Plaene/Archiv/` with a "Status" section documenting how the real implementation ended up differing from the original plan (read that before touching related code). 📝 Plan (not yet built) = a plan doc exists in `Feature-Plaene/` but nothing's implemented yet. 🗨️ **Discuss** = kept as an idea, but needs a design discussion before it's ready for a `Feature-Plaene/*.md` plan. ❌ **Rejected** = discarded, not going to be built.

## Archipelago-specific features

- **Check progress per slot** — a progress bar ("X of Y checks done"), using the location data the Archipelago session already exposes. ✅ **Done** (2026-09-09) — Plan: [`Feature-Plaene/Archiv/Fortschrittsanzeigen.md`](../../Feature-Plaene/Archiv/Fortschrittsanzeigen.md) (covers per-slot *and* multiworld-wide progress; see that file's own "Status" section for how the actual Tier 2 UI ended up differing from the original plan).
- **DeathLink support** — surface and relay DeathLink events for games that use it. ✅ **Done** (2026-09-10) — Plan: [`Feature-Plaene/Archiv/DeathLink.md`](../../Feature-Plaene/Archiv/DeathLink.md) (display-only, as planned; ended up with no per-group enable/disable toggle at all - see that file's own "Status" section for why).
- **One-click `!hint`** — a button that sends the hint chat command instead of typing it manually. ✅ **Done** (2026-09-10) — Plan: [`Feature-Plaene/Archiv/Hint-Eingabefeld.md`](../../Feature-Plaene/Archiv/Hint-Eingabefeld.md) (ended up as a real titled dialog, not a Flyout, with the button in the tab's info/status row rather than the message row - see that file's own "Status" section).
- ~~Who's online~~ — ❌ **Rejected** (2026-09-13): the info isn't very goal-directed, and the usual implementation of this (seen in other Archipelago tools) isn't wanted here.
- **Better item/trap icons** — game-specific icons for progression/useful/trap items instead of just color and text. 🗨️ **Discuss** — see [`Feature-Plaene/Item-Trap-Icons.md`](../../Feature-Plaene/Item-Trap-Icons.md).

## Notifications & visibility

These three were planned early on and deliberately dropped — worth a fresh look:

- ~~Desktop notifications (toast popups)~~ — ➡️ **Superseded** (2026-09-13) by "taskbar-button/title-bar flash" below: user preference is to not use or like toast-style desktop notifications at all — prefers a taskbar-button/title-bar flash instead (the "Skype is calling" kind of attention-getter), so a plain toast popup is *not* the direction this gets built in.
- **Taskbar-button / title-bar flash** — e.g. when a hint arrives or an important item is received while the app is in the background; replaces the toast-notification idea above. See [`Feature-Plaene/Fenster-Blinken.md`](../../Feature-Plaene/Fenster-Blinken.md) for the API research and open questions.
- **Tray icon** — keep running minimized, with an unread-events badge. 🗨️ **Discuss** — see [`Feature-Plaene/Tray-Icon.md`](../../Feature-Plaene/Tray-Icon.md) for the "does this still add value next to the flash idea?" discussion.
- **Log export** — write events/hints/chat out as text or CSV, e.g. to share in Discord. 🗨️ **Discuss** — see [`Feature-Plaene/Log-Export.md`](../../Feature-Plaene/Log-Export.md).

## Overview across many tabs

- **A summary/dashboard tab** — all servers at a glance: total open hints, total unread events. ✅ **Done** (2026-09-11) — Plan: [`Feature-Plaene/Archiv/Dashboard-Tab.md`](../../Feature-Plaene/Archiv/Dashboard-Tab.md) (see that file's own "Status" section for the one point where the mockup image, not the plan text, won).
- **Manual tab reordering** (drag & drop) instead of only automatic host/port grouping. 📝 Plan (not yet built): [`Feature-Plaene/Tab-Reihenfolge.md`](../../Feature-Plaene/Tab-Reihenfolge.md)
- ~~Free-text search across events/hints~~ — ❌ **Rejected** (2026-09-13): events are only useful in the moment, not worth searching later; the actually-worth-searching information already lives in the hint/item lists the app has, so this isn't needed as its own feature.

## Robustness & quality

- **Config export/import** — back up and restore the server/slot list (`groups.json`) when moving to another machine. 🗨️ **Discuss** — see [`Feature-Plaene/Config-Export-Import.md`](../../Feature-Plaene/Config-Export-Import.md).
- **Don't store passwords at all** — passwords currently live in plain text in `groups.json` (`ServerConnectionGroup.Password`, `SlotProfile.Password`). See [`Feature-Plaene/Passwort-Speicherung.md`](../../Feature-Plaene/Passwort-Speicherung.md) for the full, already fairly-resolved discussion (preferred direction, startup-blocking dialog, consolidated dialog layout with mockup, per-group validation rule).

## Convenience & distribution

- **Keyboard shortcuts** for common actions (next tab, focus chat input, …). 🗨️ **Discuss** — see [`Feature-Plaene/Tastenkuerzel.md`](../../Feature-Plaene/Tastenkuerzel.md).
- **Pop a tab into its own window** (for multi-monitor setups). 🗨️ **Discuss** — see [`Feature-Plaene/Tab-Eigenes-Fenster.md`](../../Feature-Plaene/Tab-Eigenes-Fenster.md) for the relationship to tab reordering and to the taskbar/title-bar flash idea.
- **Auto-update** instead of manually downloading GitHub releases. ✅ **Done** (2026-09-09) — Plan: [`Feature-Plaene/Archiv/Auto-Update.md`](../../Feature-Plaene/Archiv/Auto-Update.md) (Windows/Linux only via Velopack, side-by-side with the existing manual .zip; macOS deliberately kept manual-only - see that file's own "Status" section).

## Open questions

- Which of these areas matters most right now: Archipelago-specific features, visibility/notifications, overview at scale, or robustness/testing?
- Worth prototyping one of these in `TestHarness/` first (the project's existing workflow for UI ideas) before building it into the real app?
