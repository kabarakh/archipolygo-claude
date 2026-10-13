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

    /// <summary>True only if Confirm was clicked; Cancel, Escape and the close button all count as declined.</summary>
    public static async Task<bool> ShowDialogAsync(Window owner, ConfirmationViewModel viewModel)
    {
        var window = new ConfirmationWindow { DataContext = viewModel };
        return await window.ShowDialog<bool>(owner);
    }
}
