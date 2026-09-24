using System;
using Avalonia.Controls;

namespace Archipolygo.Services;

/// <summary>
/// Resolves which actual <see cref="Window"/> currently shows a given
/// group, now that a group's tab can live either in the main window (the
/// common case) or in its own detached window (see
/// Feature-Plaene/Tab-Eigenes-Fenster.md) - the shared foundation both that
/// feature and window-flashing (<see cref="IWindowAttentionService"/>) build
/// on, so the latter never has to assume "the" single app window.
///
/// Only ever mutated from the UI layer (<see cref="Archipolygo.Views.MainWindow"/>/
/// <see cref="Archipolygo.Views.DetachedGroupWindow"/>, both real
/// <see cref="Window"/> owners) - deliberately never touched from
/// view-model code, matching this app's existing separation of "view models
/// expose data/commands, views own actual Window instances" (see e.g.
/// <see cref="Archipolygo.ViewModels.MainWindowViewModel.ShowPasswordPromptDialogAsync"/>'s
/// own doc comment for the same rule applied to dialogs).
/// </summary>
public interface IGroupWindowLocator
{
    /// <summary>
    /// Registers the single main window - every group defaults to this
    /// unless overridden by <see cref="RegisterDetachedWindow"/>. Called
    /// once, right after <see cref="Archipolygo.Views.MainWindow"/> is
    /// constructed.
    /// </summary>
    void RegisterMainWindow(Window mainWindow);

    /// <summary>Registers <paramref name="window"/> as the (only) window currently showing <paramref name="groupId"/>, overriding the main-window default.</summary>
    void RegisterDetachedWindow(Guid groupId, Window window);

    /// <summary>Reverts <paramref name="groupId"/> back to the main-window default - called once its detached window closes or it's re-docked.</summary>
    void UnregisterDetachedWindow(Guid groupId);

    /// <summary>The window currently showing <paramref name="groupId"/> - the main window, its own detached window, or null if neither was ever registered (e.g. too early at startup).</summary>
    Window? Resolve(Guid groupId);
}
