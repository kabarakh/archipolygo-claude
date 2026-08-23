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

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.TryBuildResult(out var result))
        {
            Close(result);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    /// <summary>
    /// Removes one queued slot from <see cref="ConnectionEditorViewModel.StagedSlots"/>
    /// again. The button's own DataContext (from the ItemsControl's
    /// per-item DataTemplate) is the <see cref="StagedSlot"/> to remove -
    /// this handler just forwards it to the view model's command, which is
    /// otherwise out of reach of a plain relative binding from inside that
    /// per-item template.
    /// </summary>
    private void OnUnstageSlotClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StagedSlot slot })
        {
            ViewModel.UnstageSlotCommand.Execute(slot);
        }
    }

    /// <summary>Marks a row's slot as the server's default leader - same DataContext situation as <see cref="OnUnstageSlotClick"/>.</summary>
    private void OnMakeDefaultLeaderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguredSlotRow row })
        {
            ViewModel.MakeDefaultLeaderCommand.Execute(row);
        }
    }

    /// <summary>
    /// Removes a row's slot right away - same DataContext situation as
    /// <see cref="OnUnstageSlotClick"/>. Bound command name is
    /// "RemoveConfiguredSlotCommand", not "...AsyncCommand" - the
    /// [RelayCommand] source generator drops the "Async" suffix from the
    /// method name (<c>RemoveConfiguredSlotAsync</c>) when naming the
    /// generated command property.
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
