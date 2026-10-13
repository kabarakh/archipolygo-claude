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
    /// Modal; hints are sent live per row click, so there's no result. On close, releases any
    /// connection held open for the selected slot.
    /// </summary>
    public static async void Show(Window owner, HintPickerViewModel viewModel)
    {
        viewModel.OnOpened();
        var window = new HintPickerWindow { DataContext = viewModel };
        await window.ShowDialog(owner);
        await viewModel.OnClosedAsync();
    }
}
