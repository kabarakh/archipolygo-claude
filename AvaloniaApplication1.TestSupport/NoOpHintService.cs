using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace Archipolygo.TestSupport;

/// <summary>No-op <see cref="IHintService"/> - see <see cref="NoOpMessageHistoryService"/>'s doc comment for why.</summary>
public sealed class NoOpHintService : IHintService
{
    public void SyncHints(GroupViewModel group, IReadOnlyList<HintSnapshot> hints) { }
}
