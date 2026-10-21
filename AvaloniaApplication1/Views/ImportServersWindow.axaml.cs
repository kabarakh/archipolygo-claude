using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Archipolygo.Views;

public partial class ImportServersWindow : Window
{
    public ImportServersWindow()
    {
        InitializeComponent();
    }

    private ImportServersViewModel ViewModel => (ImportServersViewModel)DataContext!;

    private void OnImportClick(object? sender, RoutedEventArgs e) => Close(ViewModel.CreateSelection());

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    /// <summary>Null = cancelled at any step, or the file could not be read (the dialog then showed why).</summary>
    public static async Task<ConfigImportSelection?> PickFileAndShowAsync(
        Window owner, IConfigExportService exportService, IReadOnlyCollection<ServerConnectionGroup> localGroups)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import servers",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Archipolygo export")
                {
                    Patterns = new[] { "*.json" },
                    AppleUniformTypeIdentifiers = new[] { "public.json" },
                    MimeTypes = new[] { "application/json" }
                }
            }
        });

        if (files.Count == 0)
        {
            return null;
        }

        return await ShowDialogAsync(owner, await ReadExportAsync(files[0], exportService, localGroups));
    }

    private static async Task<ImportServersViewModel> ReadExportAsync(
        IStorageFile file, IConfigExportService exportService, IReadOnlyCollection<ServerConnectionGroup> localGroups)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var content = exportService.ImportFromJson(await reader.ReadToEndAsync());
            return new ImportServersViewModel(content, localGroups);
        }
        catch (ConfigImportException ex)
        {
            return ImportServersViewModel.ForError(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ImportServersViewModel.ForError($"The file could not be read: {ex.Message}");
        }
    }

    public static Task<ConfigImportSelection?> ShowDialogAsync(Window owner, ImportServersViewModel viewModel)
    {
        var window = new ImportServersWindow { DataContext = viewModel };
        DialogScreenFit.Apply(window, owner);
        return window.ShowDialog<ConfigImportSelection?>(owner);
    }
}
