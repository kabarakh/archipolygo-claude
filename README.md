# Archipolygo

Archipolygo is a desktop client for [Archipelago](https://archipelago.gg/) multiworld games. It connects to many Archipelago rooms at the same time. Each connection has its own tab with an event log, a hint list, a chat and connection controls. Thus, you can monitor and play many slots in parallel. You do not need a separate client or window for each slot.

## Purpose of the app

In Archipelago multiworld games, one person often has many active connections (for example, many slots or alternative accounts). The official client uses only one connection at a time. Archipolygo puts [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) into an Avalonia UI with tabs. Each connection has:

- A profile that the app keeps after a restart.
- An event log with a color for each type of entry: your slot, your other connected slots, other players, traps, and progression, useful and other items.
- Hint tracking.
- A chat.
- A badge on its tab that shows the number of unread events.

## How connections work

Each tab is a **server**. A server is one Archipelago room. You can configure many of your own **slots** on one server (for example, many characters or accounts in the same multiworld).

A server keeps only *one* connection open at a time. This connection belongs to the slot that is the **leader** at that time. The app does not open one connection for each slot. Many connections from the same person at the same time can cause problems in an Archipelago room. With one connection for each server, the load stays small, and the app still tracks all configured slots.

The other configured slots on the server also stay current:

- **Passive updates.** The leader connection receives the chat and the item log of the full room, not only of its own slot. When an item send or a hint concerns one of your *other* configured slots, Archipolygo copies it into the item or hint history of that slot. This occurs live. The app does not open a second connection for that slot.
- **Catch-up.** A slot can miss activity while the app is closed. A slot that you just added also has no history. For these slots, the app opens one short connection (a catch-up). The slot logs in, gets the data that it missed (received items and hints), and then disconnects. At startup, the banner "Catching up slots: N/M" shows the progress. The app does a catch-up for each configured slot on each server, one slot at a time. Thus, a restart of the app does not send many logins to one Archipelago server at the same time.
- **Leader switch.** To make a different slot the leader, select it in the account dropdown ("Chat as"). The new slot connects first. The previous leader disconnects only after the new connection is successful. Thus, you see no gap.
- **Automatic connection.** The app remembers the last leader as the default slot of that server. At the next start of the app, this slot connects automatically. When you click "Disconnect", the app stops the automatic connection for that server. The automatic connection starts again after you connect the server manually.
- **Passwords.** The app does not save passwords. It uses a room or slot password only for the current connection and does not write it to the disk. The app only remembers that a server needs a password. If a server needed a password before and the app connects it automatically, the app asks for the password first. One dialog shows all servers that need a password at that time, so the app does not interrupt you for each server. If you leave a field empty, that server stays disconnected. You can enter the password later in the same dialog, or in "Edit server...".

## How to use the app

- **Add server...** creates a new tab for a new Archipelago room, with its first slot. The app connects the server immediately.
- **Add slot...** adds one or more slots to the selected server in one step:
  1. Select players from the roster of the room. If the room uses a password for each slot, you can give a player its own password.
  2. Add as many players to the list as necessary.
  3. Confirm one time to add all players on the list.
- **Edit server...** changes the name, host, port, password and automatic connection of a server. It also manages the configured slots of the server:
  - Select the slot that is the default leader.
  - Remove a slot.

  Both lists are in alphabetical order, with the default leader always first. In this dialog, you can also set a **multiworld tracker** (a room ID or a tracker URL). The combined progress bar uses the tracker (see "Progress bar" below).
- **Chat as** is the account dropdown in the message bar of each server. It selects the configured slot that has the live connection. See "How connections work" above.
- **Tab order.** Drag the tab of a server and drop it before or after a different tab. A thin blue line shows the new position. The app keeps the new order after a restart.
- **Tab in its own window.** Right-click the tab of a server (or its row in the Dashboard "Overview"). Then select "Open in new window", for example for a second monitor. To move the tab back, click "Dock to main window" in that window, or close the window. The app does not remember these windows: at each start, all servers are in the main window. If you move your last tab into its own window, the main window has no tab left. Then the main window shows the Dashboard automatically.
- **Taskbar and title bar flash.** A server window (the main window or a separate window) flashes a few times for these events when it does not have the focus:
  - A hint arrives.
  - A DeathLink arrives.
  - You receive a progression item.
  - A player writes one of your slot names in the chat.

  After a flash, the window does not flash again until you look at it. Only live activity causes a flash. Data from a catch-up never causes a flash.
- **Unread count.** The app counts the same events for each server while you do not look at it. The count shows in these places:
  - On the red badge of the tab.
  - On the Dashboard.
  - In the window title (for example "(3) Archipolygo").
  - As a badge on the taskbar button (Windows) or on the Dock icon (macOS).

  The count of a server goes back to zero when its tab shows in the focused window, or when its own window has the focus. The Dashboard does not reset the count. Thus, the Dashboard continues to show where events occurred.

  You can mute a server that has too many events. Right-click its tab or its Dashboard row and select "Mute notifications". Or use the checkbox in **Edit server**. A muted server shows a small crossed-out bell next to its name.
- **Admin view.** For a server that you host, select **I'm the admin** in "Add server" or "Edit server". Then an **Admin** button shows next to "Hints" and "Items" in the tab of that server.
  - **Login:** The button asks for the admin password of the server (`server_password`). Then it logs in with `!admin login`. The app does not save the password. On archipelago.gg, the room owner must first set a password in the command field of the room page (`/option server_password ...`).
  - **Player list:** The list shows all players in the room with their status, their checks and the time since their last new check. A player becomes yellow after 3 days without a new check, and red after 5 days. "Exclude goaled slots" hides players that are finished. The player list needs a multiworld tracker for the server.
  - **Admin lines in the event log:** Command echoes, server answers and "Cheat console" sends have their own **Admin** type and filter button. They use the usual colors for players and items.
  - **Actions...** on a player: Send any item (with an amount) to the player. Or send one of the player's locations, as if the player checked it. **Release...** and **Collect...** force a release or a collect after you confirm.
  - **Server settings...** changes the release, collect and remaining modes, the hint cost, the points for each check, and the room and admin passwords.
  - **Automatic login:** The server logs you out when you switch accounts with "Chat as" or when you reconnect. The server also logs you out when a different client logs in as admin. While the app runs, Archipolygo logs you in again automatically.
  - **Visibility:** All players in the room see admin commands in the chat. The server hides passwords.
- **Hint...** opens a picker window for the leader of that server. You can browse the full item list of the game, not only the items that you saw before. Then you can send a `!hint` or `!hint_location` request without typing it. A checkbox hides items that you already received or hinted.
- **Progress bar.** The progress bar is in the status row of each tab. It has up to four colored segments:
  - Your checks that are done.
  - Your checks that remain.
  - The checks of all other players in the room that are done (only with a multiworld tracker).
  - The checks of all other players in the room that remain (only with a multiworld tracker).

  Move the mouse over the bar to see the exact numbers. Without a tracker, the bar shows only your two segments and a note about how to add a tracker.
- **DeathLink.** If a game supports DeathLink, DeathLink events from other players show in the event log as "DeathLink: ...". Archipolygo only shows these events. It never sends a DeathLink for you and never reacts to one (no pause, no simulated death). Thus, there is no setting for DeathLink.
- **Panel filters.** The **Events**, **Hints** and **Items** panels each have their own filters and their own view for each slot. You can filter by relevance or category, and by the configured slot that an entry concerns. To collapse a filter row, click the ▸ chevron on its left. The row then shows the active filters in one line, and the list gets more space. The app keeps this choice after a restart.
- **Slots that reached their goal.** The app shows 🏁 before the name of each slot that completed its goal. In the Events and Hints filters, **Hide goaled** hides items and hints for a receiver that completed its goal. Items that such a slot releases to other players stay visible. An item-link group counts as finished when all of its members completed their goal. A slot that completed its goal no longer flashes the taskbar for its items or hints. The Dashboard has its own "Hide goaled" checkboxes. The app does not save these checkboxes.
- **Copy lines.** In Events and Hints, you can select many lines and copy them with Ctrl+C or Cmd+C. "Copy from here" (floating button or right-click menu) copies all lines from the selected line to the end.
- **Disconnect** closes the leader connection of a server. The server then does not connect automatically until you connect it manually. **Disconnect all** does this for all servers.
- **Settings...** contains the preferences for the full app:
  - "Compact layout" makes lists and controls smaller. It is on by default.
  - The "Notifications" section sets, for each type of event, if it increases the unread count and if it flashes the taskbar button. It also sets how the flash behaves.
  - On Windows and Linux, the "Check for updates" button looks for a new version. The app also does this check at startup, without a message. When an update is available, a small dot shows on this button. On macOS, you must download updates manually.
  - The "Servers and data" section moves your configuration to a different computer:
    - "Export servers..." writes the servers that you select to one file. You can also include the app settings. The file contains no passwords, because the app never stores them.
    - "Import servers..." reads such a file and shows its servers. Clear the servers that you do not want. A server with the same host and port as one of your servers is marked "replaces existing". The import replaces that server, but slots with the same name keep their local history. If a server was connected, it connects again after the import. An imported server with auto-connect on connects immediately.
    - "Open data folder" opens the folder that contains all data of the app.

## Portable mode

The zip download of Windows and Linux can keep all its data next to the executable, so that you can move the app with its folder. To turn on portable mode, put an empty file `portable.txt` next to the executable. The app then stores its data in a `data` folder next to the executable, and Settings shows "Portable mode".

- At the first start in portable mode, the app finds the data that you already have in your user profile. It asks if you want to copy that data, move it, or start empty.
- If the app cannot write to its folder (for example, below `C:\Program Files`), it shows an error and stops. It does not use the user profile instead.
- Portable mode does not work with the installer version, because each update replaces the app folder. It also does not work on macOS yet. In these cases, the app ignores `portable.txt` and tells you so in Settings.

## Dashboard

Click **Dashboard** in the toolbar to change the full window from the server tabs to a view of all servers. The app opens on the Dashboard by default. On the Dashboard, the same button shows "Tab View" and changes the window back. The Dashboard is a different view, not a tab. Your tabs and their live connections continue to operate below it.

- **Overview** (left, default) shows all configured servers. Each row shows the connection status, the badges for unread events and open hints, and the same combined progress bar as the tab. Each row also has small icons for common actions (Connect/Disconnect, Add slot, Edit server, Remove server), so you do not have to go to the tab of the server. Click a row (not an icon) to go to the tab of that server. To change the order of the servers, drag the small grip icon on the left of a row. This works the same as dragging a tab.
- **Events** (left, toggle next to "Overview") shows one event log for all servers. By default, it shows the activity of all servers together. Use the Server and Slot filters to show less. Below the log, a separate "Send as" Server/Slot picker selects the server and the slot for a chat message. Thus, you can send chat messages from the Dashboard. This picker is intentionally independent from the filters above it.
- **Hints** (right, always visible) shows the open hints (hints that are not found yet) of all servers. It has its own Server, Slot and item category filters. These filters are independent from the Hints filters in each tab.

## How this project was built

An AI wrote all of the code in this project. No person wrote or changed a line of code manually. The project is an exercise with two goals:

- To learn how to work effectively with Claude.
- To have a real C# codebase for comparison. The author rebuilds the same app manually, from the beginning, without AI help.

### Steps

1. The author gave the app idea to ChatGPT. Together with ChatGPT, the author defined the concept and the technologies.
2. ChatGPT changed this idea into a concrete implementation plan.
3. Claude improved the implementation plan and divided it into phases.
4. Claude implemented the phases one at a time. Each phase had tests and bug fixes before the next phase started.

## Next steps

The feature-plan directory of this repository contains one file for each feature idea that is not built yet. Each file has a status tag:

- 🗨️ **Discuss**: A rough outline. It needs a design discussion before implementation.
- 📝: A plan that is ready for implementation.

After the implementation of a plan, its file moves to the feature-plan archive. The file then gets a "Status" section that tells how the result is different from the plan.

Some ideas that are not built yet:

- **Better item and trap icons.** Icons for each game. At this time, the app uses only color and text (see `Item-Trap-Icons.md`).
- **Tray icon and log export.** Options for when the app runs in the background. The project removed these ideas early, but they can be useful (see `Tray-Icon.md`, `Log-Export.md`).
- **Keyboard shortcuts** (see `Tastenkuerzel.md`).

## Technology

- [.NET 10](https://dotnet.microsoft.com/) and C#.
- [Avalonia UI](https://avaloniaui.net/) 12.0.4 (desktop UI for many platforms) with the Fluent theme.
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) 8.4.1 for MVVM (observable properties, relay commands).
- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) 6.7.1 for the Archipelago protocol and the connections.
- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) 10.0.0 for service singletons in a container (configured in `App.axaml.cs`).

## How to build the app

### Prerequisites

- The [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Windows, macOS or Linux (Avalonia supports all three).

### Build and run

```bash
# from the repository root
dotnet restore
dotnet build
dotnet run --project AvaloniaApplication1/AvaloniaApplication1.csproj
```

You can also open `AvaloniaApplication1.sln` in an IDE with .NET support (for example JetBrains Rider or Visual Studio). Then run or debug the `AvaloniaApplication1` project there.

### Publish a standalone build

```bash
dotnet publish AvaloniaApplication1/AvaloniaApplication1.csproj -c Release -r <RID> --self-contained
```

Replace `<RID>` with the runtime identifier of your target (for example `win-x64`, `osx-arm64` or `linux-x64`).
