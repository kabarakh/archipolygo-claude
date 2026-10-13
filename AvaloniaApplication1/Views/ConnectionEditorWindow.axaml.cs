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

    /// <summary>Test seam: tests substitute a canned answer instead of popping a nested dialog.</summary>
    public Func<ConfirmationViewModel, Task<bool>> ShowConfirmationDialogAsync { get; set; }

    // Host/port first: resolving it may fill in the tracker reference, which needs its own network round-trip.
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

    // Forwards the row from the per-item DataTemplate, where a relative binding can't reach the view-model command.
    private void OnMakeDefaultLeaderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguredSlotRow row })
        {
            ViewModel.MakeDefaultLeaderCommand.Execute(row);
        }
    }

    // Staged only after confirmation, since re-adding a removed slot means re-syncing its whole history.
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

    public static Task<ConnectionEditorResult?> ShowDialogAsync(Window owner, ConnectionEditorViewModel viewModel)
    {
        var window = new ConnectionEditorWindow { DataContext = viewModel };
        DialogScreenFit.Apply(window, owner);
        return window.ShowDialog<ConnectionEditorResult?>(owner);
    }
}
