using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.TryBuildSettings(out var settings))
        {
            Close(settings);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    // Code-behind rather than a view-model command: only a Window has a StorageProvider for the save dialog.
    private async void OnExportDiagnosticLogClick(object? sender, RoutedEventArgs e)
    {
        var logText = ViewModel.GetDiagnosticLogTextOrNull();
        if (logText is null)
        {
            ViewModel.ExportStatusText = "Nothing to export yet.";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export diagnostic log",
            SuggestedFileName = $"archipolygo-diagnostic-{DateTime.Now:yyyy-MM-dd-HHmmss}",
            DefaultExtension = "log",
            FileTypeChoices = new[] { FilePickerFileTypes.TextPlain }
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(logText);
            ViewModel.ExportStatusText = $"Exported to {file.Name}.";
        }
        catch (Exception)
        {
            ViewModel.ExportStatusText = "Failed to write the file.";
        }
    }

    private async void OnImportServersClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.DataManagement is not { } dataManagement)
        {
            return;
        }

        var selection = await ImportServersWindow.PickFileAndShowAsync(
            this, dataManagement.ConfigExportService, dataManagement.GetGroups());
        if (selection is null)
        {
            return;
        }

        dataManagement.StartImport(selection);

        // Saving this dialog afterwards would write its stale copy over the imported settings.
        if (selection.Settings is not null)
        {
            Close(null);
            return;
        }

        ViewModel.DataStatusText = selection.Groups.Count == 1 ? "Imported 1 server." : $"Imported {selection.Groups.Count} servers.";
    }

    private async void OnOpenDataFolderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.DataFolderPath is not { } path)
        {
            return;
        }

        var directory = new DirectoryInfo(path);
        if (!directory.Exists || !await Launcher.LaunchDirectoryInfoAsync(directory))
        {
            ViewModel.DataStatusText = $"Could not open {path}.";
        }
    }

    public static Task<AppSettings?> ShowDialogAsync(Window owner, SettingsViewModel viewModel)
    {
        var window = new SettingsWindow { DataContext = viewModel };
        DialogScreenFit.Apply(window, owner);
        return window.ShowDialog<AppSettings?>(owner);
    }
}
