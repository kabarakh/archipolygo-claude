using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Archipolygo.Views;

public partial class AdminPanelView : UserControl
{
    public AdminPanelView()
    {
        InitializeComponent();
    }

    private AdminPanelViewModel? ViewModel => DataContext as AdminPanelViewModel;

    // Dialogs are owned by whichever window hosts this control - MainWindow
    // or a detached tab window, same as the "Hint..." picker.
    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    private async void OnLoginClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } admin && Owner is { } owner)
        {
            await AdminLoginWindow.ShowDialogAsync(owner, admin);
        }
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } admin && Owner is { } owner)
        {
            AdminSettingsWindow.Show(owner, new AdminSettingsViewModel(admin, admin.GetRoomSettings()));
        }
    }

    private void OnPlayerActionsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } admin && Owner is { } owner && sender is Control { DataContext: AdminPlayerRowViewModel player })
        {
            AdminPlayerWindow.Show(owner, new AdminPlayerDialogViewModel(admin, player));
        }
    }
}
