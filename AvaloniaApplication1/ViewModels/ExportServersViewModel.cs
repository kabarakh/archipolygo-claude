using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

public sealed partial class ExportServerRowViewModel : ViewModelBase
{
    public ExportServerRowViewModel(ServerConnectionGroup group)
    {
        Group = group;
    }

    public ServerConnectionGroup Group { get; }

    public string Name => Group.Name;

    public string HostPort => Group.HostPort;

    public IBrush ColorBrush => GroupViewModel.ResolveColorBrush(Group.Color);

    [ObservableProperty]
    private bool _isSelected = true;
}

/// <summary>Picks which servers (and whether the app settings) go into a config export file.</summary>
public sealed partial class ExportServersViewModel : ViewModelBase
{
    public const string SuggestedFileName = "archipolygo-servers.json";

    private readonly AppSettings _settings;
    private readonly IConfigExportService _exportService;

    public ExportServersViewModel(IEnumerable<ServerConnectionGroup> groups, AppSettings settings, IConfigExportService exportService)
    {
        _settings = settings;
        _exportService = exportService;
        Servers = groups.Select(group => new ExportServerRowViewModel(group)).ToList();

        foreach (var row in Servers)
        {
            row.PropertyChanged += OnServerRowChanged;
        }
    }

    public IReadOnlyList<ExportServerRowViewModel> Servers { get; }

    [ObservableProperty]
    private bool _includeAppSettings;

    [ObservableProperty]
    private string? _errorText;

    public bool CanExport => Servers.Any(row => row.IsSelected);

    /// <returns>False if writing failed; <see cref="ErrorText"/> then says why and the dialog stays open.</returns>
    public async Task<bool> TryExportAsync(Func<Task<Stream>> openWrite)
    {
        var selectedGroups = Servers.Where(row => row.IsSelected).Select(row => row.Group).ToList();
        var json = _exportService.ExportToJson(selectedGroups, IncludeAppSettings ? _settings : null);

        try
        {
            await using var stream = await openWrite();
            // Overwriting a longer existing file must not leave its tail behind.
            if (stream.CanSeek)
            {
                stream.SetLength(0);
            }

            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            ErrorText = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"Failed to write the file: {e.Message}";
            return false;
        }
    }

    private void OnServerRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExportServerRowViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(CanExport));
        }
    }
}
