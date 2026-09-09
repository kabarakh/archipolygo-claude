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
    }

    private ConnectionEditorViewModel ViewModel => (ConnectionEditorViewModel)DataContext!;

    /// <summary>
    /// Resolves the Tier 2 tracker reference (Feature-Plaene/Archiv/Fortschrittsanzeigen.md -
    /// a room URL needs an actual network round-trip, see
    /// <see cref="ConnectionEditorViewModel.TryResolveTrackerReferenceAsync"/>)
    /// before running the rest of the usual synchronous validation.
    /// </summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
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
    /// Stages a row's slot for removal (applied on Save, see
    /// <see cref="ConnectionEditorViewModel.RemoveConfiguredSlot"/>) - same
    /// DataContext situation as <see cref="OnMakeDefaultLeaderClick"/>.
    /// </summary>
    private void OnRemoveConfiguredSlotClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguredSlotRow row })
        {
            ViewModel.RemoveConfiguredSlotCommand.Execute(row);
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
