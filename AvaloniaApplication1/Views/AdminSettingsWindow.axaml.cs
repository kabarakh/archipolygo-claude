using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Archipolygo.Views;

public partial class AdminSettingsWindow : Window
{
    public AdminSettingsWindow()
    {
        InitializeComponent();
    }

    public static void Show(Window owner, AdminSettingsViewModel viewModel)
    {
        var window = new AdminSettingsWindow { DataContext = viewModel };
        _ = window.ShowDialog(owner);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // Stays open afterwards so the server's answers can be read.
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AdminSettingsViewModel viewModel)
        {
            await viewModel.SaveAsync();
        }
    }
}
