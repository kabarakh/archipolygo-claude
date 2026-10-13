using System;
using Avalonia.Controls;

namespace Archipolygo.Services;

/// <summary>
/// Resolves which <see cref="Window"/> shows a group (main or detached). Mutated only from the UI
/// layer, never from view models - views own Window instances.
/// </summary>
public interface IGroupWindowLocator
{
    /// <summary>The default for every group without a detached window.</summary>
    void RegisterMainWindow(Window mainWindow);

    /// <summary>Overrides the main-window default for <paramref name="groupId"/>.</summary>
    void RegisterDetachedWindow(Guid groupId, Window window);

    void UnregisterDetachedWindow(Guid groupId);

    /// <summary>Null if no window was registered yet (e.g. early at startup).</summary>
    Window? Resolve(Guid groupId);
}
