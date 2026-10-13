using System;
using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace Archipolygo.TestSupport;

/// <summary>Records every Report call, independent of what the real AttentionTracker would decide.</summary>
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
