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
        if (ViewModel.TryBuildProfile(out var profile))
        {
            Close(profile);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    /// <summary>
    /// Shows the editor as a modal dialog and returns the resulting profile,
    /// or null if the user cancelled.
    /// </summary>
    public static Task<ServerProfile?> ShowDialogAsync(Window owner, ConnectionEditorViewModel viewModel)
    {
        var window = new ConnectionEditorWindow { DataContext = viewModel };
        return window.ShowDialog<ServerProfile?>(owner);
    }
}
