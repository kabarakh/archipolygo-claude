using System.Threading.Tasks;
using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Archipolygo.Views;

public partial class PortableStartupWindow : Window
{
    // Closing the window without a button changes nothing on disk, so it counts as Quit.
    private PortableStartupChoice _choice = PortableStartupChoice.Quit;

    public PortableStartupWindow()
    {
        InitializeComponent();
    }

    private void CloseWith(PortableStartupChoice choice)
    {
        _choice = choice;
        Close();
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e) => CloseWith(PortableStartupChoice.CopyFromAppData);

    private void OnMoveClick(object? sender, RoutedEventArgs e) => CloseWith(PortableStartupChoice.MoveFromAppData);

    private void OnStartEmptyClick(object? sender, RoutedEventArgs e) => CloseWith(PortableStartupChoice.StartEmpty);

    private void OnQuitClick(object? sender, RoutedEventArgs e) => CloseWith(PortableStartupChoice.Quit);

    /// <summary>Not ShowDialog: this runs before the main window exists, so there is no owner.</summary>
    public static Task<PortableStartupChoice> ShowAndWaitAsync(PortableStartupViewModel viewModel) =>
        new PortableStartupWindow { DataContext = viewModel }.ShowAndWaitForChoiceAsync();

    public Task<PortableStartupChoice> ShowAndWaitForChoiceAsync()
    {
        var closed = new TaskCompletionSource<PortableStartupChoice>();
        Closed += (_, _) => closed.TrySetResult(_choice);
        Show();
        return closed.Task;
    }
}
