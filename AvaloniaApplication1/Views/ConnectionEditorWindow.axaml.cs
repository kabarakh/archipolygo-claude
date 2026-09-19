using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

public partial class ConnectionEditorWindow : Window
{
    public ConnectionEditorWindow()
    {
        InitializeComponent();
        ShowConfirmationDialogAsync = viewModel => ConfirmationWindow.ShowDialogAsync(this, viewModel);
    }

    private ConnectionEditorViewModel ViewModel => (ConnectionEditorViewModel)DataContext!;

    /// <summary>
    /// Shows the "remove slot?" confirmation before staging a removal (see
    /// <see cref="OnRemoveConfiguredSlotClick"/>). Defaults to a real
    /// <see cref="ConfirmationWindow"/> owned by this window; tests substitute
    /// a canned answer instead of actually popping a nested dialog - same
    /// Func-property seam <see cref="ViewModels.MainWindowViewModel.ShowConfirmationDialogAsync"/>
    /// uses for the app's other confirmation.
    /// </summary>
    public Func<ConfirmationViewModel, Task<bool>> ShowConfirmationDialogAsync { get; set; }

    /// <summary>
    /// Resolves the two fields that can each need an actual network
    /// round-trip before the rest of the usual synchronous validation runs:
    /// <see cref="ConnectionEditorViewModel.HostPortInput"/> first (it may
    /// fill in <see cref="ConnectionEditorViewModel.TrackerReferenceInput"/>
    /// along the way, see <see cref="ConnectionEditorViewModel.TryResolveHostPortAsync"/>),
    /// then the Tier 2 tracker reference itself (Feature-Plaene/Archiv/Fortschrittsanzeigen.md,
    /// see <see cref="ConnectionEditorViewModel.TryResolveTrackerReferenceAsync"/>).
    /// </summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (!await ViewModel.TryResolveHostPortAsync())
        {
            return; // ValidationError already set.
        }

        if (!await ViewModel.TryResolveTrackerReferenceAsync())
        {
            return; // ValidationError already set.
        }

        if (ViewModel.TryBuildResult(out var result))
        {
            Close(result);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    /// <summary>Marks a row's slot as the server's default leader. The button's own DataContext (from the ItemsControl's per-item DataTemplate) is the <see cref="ConfiguredSlotRow"/> to act on - this handler just forwards it to the view model's command, which is otherwise out of reach of a plain relative binding from inside that per-item template.</summary>
    private void OnMakeDefaultLeaderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguredSlotRow row })
        {
            ViewModel.MakeDefaultLeaderCommand.Execute(row);
        }
    }

    /// <summary>
    /// Asks for confirmation, then stages a row's slot for removal (applied
    /// on Save, see <see cref="ConnectionEditorViewModel.RemoveConfiguredSlot"/>)
    /// - same DataContext situation as <see cref="OnMakeDefaultLeaderClick"/>.
    /// A decline leaves the row exactly as it was; staging (and the row
    /// disappearing from the list) only happens after the user actually
    /// confirms, since re-adding a removed slot later means re-syncing its
    /// whole history from scratch.
    /// </summary>
    private async void OnRemoveConfiguredSlotClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguredSlotRow row })
        {
            var confirmed = await ShowConfirmationDialogAsync(new ConfirmationViewModel
            {
                Title = "Remove slot?",
                Message = $"Remove \"{row.Slot.DisplayName}\" from this server? " +
                          "If you add it back later, its whole history will need to sync again from scratch.",
            });

            if (confirmed)
            {
                ViewModel.RemoveConfiguredSlotCommand.Execute(row);
            }
        }
    }

    /// <summary>
    /// Shows the editor as a modal dialog and returns the resulting profile,
    /// or null if the user cancelled.
    /// </summary>
    public static Task<ConnectionEditorResult?> ShowDialogAsync(Window owner, ConnectionEditorViewModel viewModel)
    {
        var window = new ConnectionEditorWindow { DataContext = viewModel };
        return window.ShowDialog<ConnectionEditorResult?>(owner);
    }
}
