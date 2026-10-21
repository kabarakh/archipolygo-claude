using System.Threading.Tasks;
using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Archipolygo.Views;

public partial class ExportServersWindow : Window
{
    public ExportServersWindow()
    {
        InitializeComponent();
    }

    private ExportServersViewModel ViewModel => (ExportServersViewModel)DataContext!;

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    // Code-behind rather than a view-model command: only a Window has a StorageProvider for the save dialog.
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export servers",
            SuggestedFileName = ExportServersViewModel.SuggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { FilePickerFileTypes.Json }
        });

        if (file is null)
        {
            return;
        }

        var filePath = file.TryGetLocalPath();
        if (filePath is null)
        {
            ViewModel.ErrorText = "This location can't be written to. Choose a folder on this computer.";
            return;
        }

        if (ViewModel.TryExport(filePath))
        {
            Close(true);
        }
    }

    /// <returns>True if a file was written.</returns>
    public static Task<bool> ShowDialogAsync(Window owner, ExportServersViewModel viewModel)
    {
        var window = new ExportServersWindow { DataContext = viewModel };
        DialogScreenFit.Apply(window, owner);
        return window.ShowDialog<bool>(owner);
    }
}
