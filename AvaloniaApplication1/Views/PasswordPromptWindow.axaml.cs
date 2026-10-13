using System.Threading;
using System.Threading.Tasks;
using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Archipolygo.Views;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow()
    {
        InitializeComponent();
    }

    private void OnConnectClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>
    /// True only if "Connect" was clicked; entered values reach the caller via two-way bindings regardless.
    /// Cancelling <paramref name="cancellationToken"/> force-closes the window, so a Disconnect on a group
    /// waiting here doesn't hang until someone answers.
    /// </summary>
    public static async Task<bool> ShowDialogAsync(Window owner, PasswordPromptViewModel viewModel, CancellationToken cancellationToken)
    {
        var window = new PasswordPromptWindow { DataContext = viewModel };

        await using var registration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() => window.Close(false)));

        return await window.ShowDialog<bool>(owner);
    }
}
