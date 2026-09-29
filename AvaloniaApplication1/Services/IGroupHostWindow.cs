using System;

namespace Archipolygo.Services;

/// <summary>
/// Implemented by every window that can show a group (<see cref="Views.MainWindow"/>,
/// <see cref="Views.DetachedGroupWindow"/>) so <see cref="WindowAttentionService.IsGroupSeen"/>
/// can ask whether a group is actually on screen, not just whether its
/// window is focused - the main window hosts many groups but shows only one
/// tab (or the Dashboard) at a time.
/// </summary>
public interface IGroupHostWindow
{
    bool IsShowingGroup(Guid groupId);
}
