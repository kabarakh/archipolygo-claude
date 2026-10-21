using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

public sealed partial class ImportServerRowViewModel : ViewModelBase
{
    public required ServerConnectionGroup Group { get; init; }

    /// <summary>A local server with the same host and port exists; importing replaces it.</summary>
    public required bool OverwritesExisting { get; init; }

    [ObservableProperty]
    private bool _isSelected = true;

    public string HostPort => Group.HostPort;

    public string SlotSummary => string.Join(", ", Group.Slots.Select(s => s.SlotName));
}

/// <summary>Picks which servers (and optionally the app settings) of an export file to import.</summary>
public sealed partial class ImportServersViewModel : ViewModelBase
{
    private readonly AppSettings? _importableSettings;

    public ObservableCollection<ImportServerRowViewModel> Rows { get; } = new();

    /// <summary>Only offered if the file contains settings. Off by default: it replaces the theme and every other local setting.</summary>
    [ObservableProperty]
    private bool _importSettings;

    public string? ErrorMessage { get; private init; }

    public bool ShowError => ErrorMessage is not null;

    public bool ShowServerList => !ShowError;

    public bool ShowSettingsOption => !ShowError && _importableSettings is not null;

    public bool HasSelection => Rows.Any(r => r.IsSelected);

    public ImportServersViewModel(ConfigExportContent content, IReadOnlyCollection<ServerConnectionGroup> localGroups)
    {
        _importableSettings = content.Settings;

        foreach (var group in content.Groups)
        {
            var row = new ImportServerRowViewModel
            {
                Group = group,
                OverwritesExisting = localGroups.Any(local => ConfigImportMerger.IsSameServer(local, group))
            };
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HasSelection));
            Rows.Add(row);
        }
    }

    private ImportServersViewModel()
    {
    }

    public static ImportServersViewModel ForError(string message) => new() { ErrorMessage = message };

    public ConfigImportSelection CreateSelection() => new(
        Rows.Where(r => r.IsSelected).Select(r => r.Group).ToList(),
        ImportSettings ? _importableSettings : null);
}
