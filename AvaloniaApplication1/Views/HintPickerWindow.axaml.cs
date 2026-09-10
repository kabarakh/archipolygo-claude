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
    /// while the window stays open for as long as the user wants.
    /// </summary>
    public static void Show(Window owner, HintPickerViewModel viewModel)
    {
        viewModel.OnOpened();
        var window = new HintPickerWindow { DataContext = viewModel };
        window.ShowDialog(owner);
    }
}
