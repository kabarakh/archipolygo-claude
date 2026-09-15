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
    /// Shows the dialog and returns whether "Connect" was clicked (as
    /// opposed to Cancel, or the window closing any other way, e.g. the
    /// title bar's own close button - both count as declined). Values
    /// entered into any field are visible to the caller regardless, via the
    /// two-way bindings straight through to <see cref="Models.SlotProfile.Password"/>/
    /// <see cref="Models.ServerConnectionGroup.Password"/> - see
    /// <see cref="PasswordPromptViewModel"/>'s own doc comment.
    ///
    /// <paramref name="cancellationToken"/> forces the window closed (as if
    /// Cancel were clicked) the moment it's cancelled - see
    /// <see cref="Services.IConnectionManager.PasswordRequested"/>'s own doc
    /// comment for why: without this, a Disconnect click on a group whose
    /// connect attempt is sitting here waiting on human input would hang
    /// until someone actually answers the dialog.
    /// </summary>
    public static async Task<bool> ShowDialogAsync(Window owner, PasswordPromptViewModel viewModel, CancellationToken cancellationToken)
    {
        var window = new PasswordPromptWindow { DataContext = viewModel };

        await using var registration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() => window.Close(false)));

        return await window.ShowDialog<bool>(owner);
    }
}
