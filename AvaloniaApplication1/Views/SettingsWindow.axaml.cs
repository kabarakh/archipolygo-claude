using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.TryBuildSettings(out var settings))
        {
            Close(settings);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    /// <summary>
    /// Shows the settings editor as a modal dialog and returns the resulting
    /// settings, or null if the user cancelled.
    /// </summary>
    public static Task<AppSettings?> ShowDialogAsync(Window owner, SettingsViewModel viewModel)
    {
        var window = new SettingsWindow { DataContext = viewModel };
        return window.ShowDialog<AppSettings?>(owner);
    }
}
