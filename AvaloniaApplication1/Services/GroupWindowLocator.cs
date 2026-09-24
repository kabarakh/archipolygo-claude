using System;
using System.Collections.Concurrent;
using Avalonia.Controls;

namespace Archipolygo.Services;

/// <inheritdoc cref="IGroupWindowLocator"/>
public sealed class GroupWindowLocator : IGroupWindowLocator
{
    private Window? _mainWindow;
    private readonly ConcurrentDictionary<Guid, Window> _detachedWindows = new();

    public void RegisterMainWindow(Window mainWindow) => _mainWindow = mainWindow;

    public void RegisterDetachedWindow(Guid groupId, Window window) => _detachedWindows[groupId] = window;

    public void UnregisterDetachedWindow(Guid groupId) => _detachedWindows.TryRemove(groupId, out _);

    public Window? Resolve(Guid groupId) =>
        _detachedWindows.TryGetValue(groupId, out var window) ? window : _mainWindow;
}
