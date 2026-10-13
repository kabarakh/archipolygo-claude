using System.Threading.Tasks;
using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Archipolygo.Views;

public partial class AdminLoginWindow : Window
{
    public AdminLoginWindow()
    {
        InitializeComponent();
        Opened += (_, _) => PasswordBox.Focus();
    }

    /// <summary>True once "!admin login" succeeded, false if cancelled/closed.</summary>
    public static async Task<bool> ShowDialogAsync(Window owner, AdminPanelViewModel admin)
    {
        var window = new AdminLoginWindow { DataContext = new AdminLoginViewModel(admin) };
        return await window.ShowDialog<bool?>(owner) == true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnLoginClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AdminLoginViewModel viewModel && await viewModel.TryLoginAsync())
        {
            Close(true);
        }
    }
}
