using System.Threading.Tasks;
using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Archipolygo.Views;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow()
    {
        InitializeComponent();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>
    /// Shows the dialog and returns whether the confirm button was clicked
    /// (as opposed to Cancel, Escape, or the title bar's close button - all
    /// three count as declined, same convention as <see cref="PasswordPromptWindow.ShowDialogAsync"/>).
    /// </summary>
    public static async Task<bool> ShowDialogAsync(Window owner, ConfirmationViewModel viewModel)
    {
        var window = new ConfirmationWindow { DataContext = viewModel };
        return await window.ShowDialog<bool>(owner);
    }
}
