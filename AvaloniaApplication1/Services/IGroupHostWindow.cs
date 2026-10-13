using System;

namespace Archipolygo.Services;

/// <summary>
/// Lets attention checks ask whether a group is actually on screen, not just whether its window
/// is focused - the main window hosts many groups but shows one tab at a time.
/// </summary>
public interface IGroupHostWindow
{
    bool IsShowingGroup(Guid groupId);
}
