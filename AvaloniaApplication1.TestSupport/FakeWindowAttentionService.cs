using System;
using System.Collections.Generic;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>
/// Records every <see cref="RequestAttention"/> call instead of touching a
/// real <see cref="Avalonia.Controls.Window"/> - see Feature-Plaene/Tab-Eigenes-Fenster.md,
/// Phase 2. Lets a test assert exactly when/how often a hint/DeathLink/
/// progression item should have triggered the window flash, without needing
/// an actual window (the interface itself never touches one - only the real
/// <c>WindowAttentionService</c> does).
/// </summary>
public sealed class FakeWindowAttentionService : IWindowAttentionService
{
    public List<Guid> RequestedGroupIds { get; } = new();

    public void RequestAttention(Guid groupId) => RequestedGroupIds.Add(groupId);
}
