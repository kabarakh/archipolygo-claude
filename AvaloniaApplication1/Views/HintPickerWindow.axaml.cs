using Avalonia.Controls;
using Avalonia.Interactivity;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

public partial class HintPickerWindow : Window
{
    public HintPickerWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Shows the picker as a modal dialog, same as ConnectionEditorWindow's
    /// own <c>ShowDialogAsync</c> - re-resolves the default slot (the leader
    /// may have changed since this was last open) and (re)loads its rows
    /// right before showing, see <see cref="HintPickerViewModel.OnOpened"/>.
    /// No result to return: sending happens live, one row click at a time,
    /// while the window stays open for as long as the user wants. Once it
    /// closes (however that happens - the "Close" button, Escape, the OS
    /// close button), <see cref="HintPickerViewModel.OnClosedAsync"/> releases
    /// any connection the session held open for whichever slot was still
    /// selected (see that class's doc comment) - a same-slot re-send/re-browse
    /// no longer needs one once the picker itself isn't around to use it.
    /// </summary>
    public static async void Show(Window owner, HintPickerViewModel viewModel)
    {
        viewModel.OnOpened();
        var window = new HintPickerWindow { DataContext = viewModel };
        await window.ShowDialog(owner);
        await viewModel.OnClosedAsync();
    }
}
