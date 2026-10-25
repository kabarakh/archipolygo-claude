using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

public class SessionEventTranslatorNameTests
{
    private sealed class RecordingHintService : IHintService
    {
        public List<HintSnapshot> Synced { get; } = new();

        public void SyncHints(GroupViewModel group, IReadOnlyList<HintSnapshot> hints, bool isLive) => Synced.AddRange(hints);

        public void AddHintFromChat(GroupViewModel group, HintSnapshot snapshot) => Synced.Add(snapshot);
    }

    [AvaloniaFact]
    public void OnHintsUpdated_PlayerNames_UseSlotNameWithAlias_WithoutTheRedundantSuffix()
    {
        var (manager, _) = MakeManager();
        var (vm, _) = MakeGroup(manager, "Alice");
        var hintService = new RecordingHintService();
        var translator = new SessionEventTranslator(new NoOpMessageHistoryService(), hintService);

        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.Players.AllPlayers = new[]
        {
            new PlayerInfo(0, 1, "Alice", "Alice", "Some Game", null, null),
            new PlayerInfo(0, 2, "CaptainHook", "KabaDone (CaptainHook)", "Some Game", null, null),
        };

        translator.OnHintsUpdated(vm, session, new[]
        {
            new Hint { ReceivingPlayer = 1, FindingPlayer = 2, ItemId = 10, LocationId = 20 },
        }, isLive: false);

        var snapshot = Assert.Single(hintService.Synced);
        Assert.Equal("Alice", snapshot.ReceivingPlayerName);
        Assert.Equal("CaptainHook (KabaDone)", snapshot.FindingPlayerName);
    }
}
