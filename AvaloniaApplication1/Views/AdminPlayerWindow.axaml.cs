using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Archipolygo.Views;

public partial class AdminPlayerWindow : Window
{
    public AdminPlayerWindow()
    {
        InitializeComponent();
    }

    /// <summary>Modal like the "Hint..." picker; loads the player's game data once shown.</summary>
    public static async void Show(Window owner, AdminPlayerDialogViewModel viewModel)
    {
        var window = new AdminPlayerWindow { DataContext = viewModel };
        viewModel.ConfirmAsync = confirmation => ConfirmationWindow.ShowDialogAsync(window, confirmation);
        var shown = window.ShowDialog(owner);
        await viewModel.LoadAsync();
        await shown;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
