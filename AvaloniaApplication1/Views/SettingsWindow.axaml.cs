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

    /// <summary>
    /// Saves the current diagnostic log (see <see cref="SettingsViewModel.GetDiagnosticLogTextOrNull"/>)
    /// to a user-chosen file. Owned by this window's code-behind rather than
    /// a <c>[RelayCommand]</c> on the view model, same reasoning as the
    /// password-prompt/confirmation dialogs in App.axaml.cs - only an actual
    /// <c>Window</c> has a <see cref="StorageProvider"/> to show a save
    /// dialog with.
    /// </summary>
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

    /// <summary>
    /// Shows the settings editor as a modal dialog and returns the resulting
    /// settings, or null if the user cancelled.
    /// </summary>
    public static Task<AppSettings?> ShowDialogAsync(Window owner, SettingsViewModel viewModel)
    {
        var window = new SettingsWindow { DataContext = viewModel };
        return window.ShowDialog<AppSettings?>(owner);
    }
}
