# Archipolygo

A desktop client for managing several simultaneous [Archipelago](https://archipelago.gg/) multiworld game connections at once. Each connection lives in its own tab with its own event log, hint list, chat, and connection controls, so you can keep an eye on (and play) multiple slots in parallel without juggling separate clients or windows.

## What is this for?

Archipelago multiworld games are often played with several active connections per person (e.g. multiple slots, or alts), and the official client is built around a single connection at a time. Archipolygo wraps [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) in a tabbed Avalonia UI so each connection gets its own persistent profile, color-coded event log (your own slot, other connected slots, other players, traps, progression/useful/other items), hint tracking, chat, and an unread-events badge per tab.

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
