using System;
using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace Archipolygo.TestSupport;

/// <summary>
/// Records every <see cref="Report"/> call - lets a trigger-site test
/// (hint, DeathLink, item, chat) assert exactly when and with which
/// <see cref="AttentionCategory"/> it reported, independent of what the
/// real <see cref="AttentionTracker"/> would then decide.
/// </summary>
public sealed class FakeAttentionTracker : IAttentionTracker
{
    public List<(Guid GroupId, AttentionCategory Category)> Reports { get; } = new();

    public void Report(GroupViewModel group, AttentionCategory category) => Reports.Add((group.Group.Id, category));

    public int RefreshSeenStateCalls { get; private set; }

    public void RefreshSeenState() => RefreshSeenStateCalls++;

    public void ApplySettings(AppSettings settings)
    {
    }
}
