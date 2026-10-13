using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace Archipolygo.TestSupport;

public sealed class NoOpHintService : IHintService
{
    public void SyncHints(GroupViewModel group, IReadOnlyList<HintSnapshot> hints, bool isLive) { }

    public void AddHintFromChat(GroupViewModel group, HintSnapshot snapshot) { }
}
