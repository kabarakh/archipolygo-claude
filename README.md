# Archipolygo

A desktop client for managing several simultaneous [Archipelago](https://archipelago.gg/) multiworld game connections at once. Each connection lives in its own tab with its own event log, hint list, chat, and connection controls, so you can keep an eye on (and play) multiple slots in parallel without juggling separate clients or windows.

## What is this for?

Archipelago multiworld games are often played with several active connections per person (e.g. multiple slots, or alts), and the official client is built around a single connection at a time. Archipolygo wraps [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) in a tabbed Avalonia UI so each connection gets its own persistent profile, color-coded event log (your own slot, other connected slots, other players, traps, progression/useful/other items), hint tracking, chat, and an unread-events badge per tab.

## How connections work

Each tab is a **server**: one Archipelago room, which can have several of your own **slots** configured on it at once (e.g. several characters/accounts you play in the same multiworld). A server only ever keeps *one* live connection open at a time, to whichever slot is currently the **leader** - not one connection per slot. Archipelago rooms don't take kindly to the same person opening many simultaneous connections, so this keeps the footprint minimal while still tracking every configured slot.

The other configured slots on that server aren't just sitting idle, though:

- **Passive updates.** The leader's connection sees the whole room's chat and item-send log, not just its own slot. Archipolygo mirrors any item send or hint that concerns one of your *other* configured slots straight into that slot's own item/hint history, live, without ever opening a second connection for it.
- **Catch-up sync.** A slot that missed activity while the app itself was closed (or that was just added) gets a brief, one-shot connection: it logs in just long enough to pull whatever it missed (received items, hints), then disconnects again. This is what the "Catching up slots: N/M" progress banner at startup is doing - working through every configured slot across every server, one at a time, so a whole app restart doesn't hit any single Archipelago server with a burst of simultaneous logins.
- **Switching leader.** Picking a different slot from the account dropdown ("Chat as") makes *that* slot the leader instead: it connects first, and only once that succeeds does the previous leader disconnect, so there's no visible gap. Whichever slot you leave as leader is remembered as that server's default, and reconnects automatically the next time you start the app - until you hit Disconnect, which turns auto-reconnect back off for that server until you connect it again by hand.

## Using the app

- **Add server...** creates a new tab for a new Archipelago room, with its first slot, and connects it right away.
- **Add slot...** adds one or more further slots to the currently selected server in one go: pick players from the room's actual roster (each can optionally get its own password override, for rooms that use per-slot passwords), queue up as many as you like, then confirm once to add them all together.
- **Edit server...** changes a server's name/host/port/password/auto-connect, and manages its already-configured slots in one place - pick which slot should be the default leader, or remove a slot entirely (both listed alphabetically, with the default leader always shown first).
- **Chat as** (the account dropdown on each server's message bar) switches which configured slot is currently live - see "How connections work" above.
- The **Events**, **Hints**, and **Items** panels each have their own filters (by relevance/category, and by which configured slot something concerns) and their own slot-scoped view. Events and Hints both support selecting several lines at once and copying them all with Ctrl+C/Cmd+C.
- **Disconnect** drops a server's current leader connection (and stops it auto-reconnecting until you reconnect manually); **Disconnect all** does that for every server at once.

## A note on how this was built

This entire codebase is fully AI-generated — no line of code was manually written or edited by hand. The project was built as an exercise to learn how to work with Claude effectively, and to end up with a real, inspectable C# codebase to compare against while independently rebuilding the same app from scratch, by hand, without any AI assistance.

### How it came together

1. The app idea was given to ChatGPT, and the intended technologies and concept were fleshed out together with it.
2. ChatGPT was then used to turn that idea into a concrete implementation plan.
3. The implementation plan was refined with Claude and broken down into phases.
4. Claude implemented the phases step by step, with each phase tested and any resulting bugs fixed before moving on.

## Tech stack

- [.NET 10](https://dotnet.microsoft.com/) / C#
- [Avalonia UI](https://avaloniaui.net/) 12.0.4 (cross-platform desktop UI), with the Fluent theme
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) 8.4.1 for MVVM (observable properties, relay commands)
- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) 6.7.1 for the actual Archipelago protocol/connection handling
- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) 10.0.0 for explicit, container-managed service singletons (wired up in `App.axaml.cs`)

## Building the app

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) installed
- Windows, macOS, or Linux (Avalonia is cross-platform)

### Build and run

```bash
# from the repository root
dotnet restore
dotnet build
dotnet run --project AvaloniaApplication1/AvaloniaApplication1.csproj
```

Alternatively, open `AvaloniaApplication1.sln` in an IDE with .NET support (e.g. JetBrains Rider or Visual Studio) and run/debug the `AvaloniaApplication1` project from there.

### Publish a standalone build

```bash
dotnet publish AvaloniaApplication1/AvaloniaApplication1.csproj -c Release -r <RID> --self-contained
```

Replace `<RID>` with your target runtime identifier (e.g. `win-x64`, `osx-arm64`, `linux-x64`).
