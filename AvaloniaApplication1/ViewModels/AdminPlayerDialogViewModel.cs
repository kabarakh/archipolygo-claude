using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>
/// The per-player admin dialog (Admin-Funktionen.md in the feature-plan
/// archive), built on the "Hint..." picker's pattern: a mode switch, a search
/// box and one button per row that sends right away while the window stays
/// open. The player is fixed by the row it was opened from. Item/location
/// names come from the room's DataPackage for that player's game.
/// </summary>
public partial class AdminPlayerDialogViewModel : ViewModelBase
{
    private readonly AdminPanelViewModel _admin;
    private IReadOnlyList<string> _itemNames = Array.Empty<string>();
    private IReadOnlyDictionary<string, long> _locationIds = new Dictionary<string, long>();

    public AdminPlayerDialogViewModel(AdminPanelViewModel admin, AdminPlayerRowViewModel player)
    {
        _admin = admin;
        Player = player;
    }

    public AdminPlayerRowViewModel Player { get; }

    public string Title => $"Admin - {Player.DisplayName}";

    public string SubHeader => Player.HasTrackerData
        ? $"{Player.Game} · {Player.StatusText} · {Player.ChecksText} · {Player.InactiveText}"
        : Player.Game;

    /// <summary>Set by the view: shows the confirmation dialog, true = confirmed. Release/Collect are the only actions that ask (decided 2026-09-29).</summary>
    public Func<ConfirmationViewModel, Task<bool>>? ConfirmAsync { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocationMode))]
    [NotifyPropertyChangedFor(nameof(ShowHideCheckedCheckbox))]
    [NotifyPropertyChangedFor(nameof(ShowNoTrackerNote))]
    private bool _isItemMode = true;

    public bool IsLocationMode
    {
        get => !IsItemMode;
        set => IsItemMode = !value;
    }

    /// <summary>Only meaningful with tracker data - the only source of which locations another player already checked.</summary>
    public bool ShowHideCheckedCheckbox => IsLocationMode && Player.HasTrackerData;

    public bool ShowNoTrackerNote => IsLocationMode && !Player.HasTrackerData;

    [ObservableProperty]
    private decimal? _amount = 1;

    [ObservableProperty]
    private bool _hideChecked = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    public bool HasResult => !string.IsNullOrEmpty(ResultText);

    [ObservableProperty]
    private bool _isSending;

    public ObservableCollection<string> FilteredRows { get; } = new();

    public bool HasNoMatches => !IsLoading && FilteredRows.Count == 0;

    [ObservableProperty]
    private string? _loadError;

    public async Task LoadAsync()
    {
        IsLoading = true;
        var data = await _admin.GetGameDataAsync(Player.Game);
        if (data is null)
        {
            LoadError = $"Couldn't load item and location names for {Player.Game}.";
        }
        else
        {
            _itemNames = data.ItemNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            _locationIds = data.LocationIds;
        }

        IsLoading = false;
        RefreshRows();
    }

    partial void OnIsItemModeChanged(bool value) => RefreshRows();
    partial void OnHideCheckedChanged(bool value) => RefreshRows();
    partial void OnSearchTextChanged(string value) => RefreshRows();

    private void RefreshRows()
    {
        IEnumerable<string> names = IsItemMode
            ? _itemNames
            : _locationIds
                .Where(pair => !(HideChecked && Player.HasTrackerData && Player.IsLocationChecked(pair.Value)))
                .Select(pair => pair.Key)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        FilteredRows.Clear();
        foreach (var name in names.Where(n => n.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredRows.Add(name);
        }

        OnPropertyChanged(nameof(HasNoMatches));
    }

    [RelayCommand]
    private async Task SendRowAsync(string name)
    {
        IsSending = true;
        try
        {
            if (IsItemMode)
            {
                ResultText = await _admin.SendItemAsync(Player, name, (int)(Amount ?? 1));
            }
            else
            {
                ResultText = await _admin.SendLocationAsync(Player, name);
                if (_locationIds.TryGetValue(name, out var locationId))
                {
                    Player.MarkLocationChecked(locationId);
                    RefreshRows();
                }
            }
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private async Task ReleaseAsync()
    {
        if (await ConfirmAsync!(new ConfirmationViewModel
            {
                Title = "Release items?",
                Message = $"Send all of {Player.DisplayName}'s remaining items to their owners? This can't be undone.",
                ConfirmText = "Release",
            }))
        {
            ResultText = await _admin.ReleaseAsync(Player);
        }
    }

    [RelayCommand]
    private async Task CollectAsync()
    {
        if (await ConfirmAsync!(new ConfirmationViewModel
            {
                Title = "Collect items?",
                Message = $"Give {Player.DisplayName} all of their remaining items from other worlds? This can't be undone.",
                ConfirmText = "Collect",
            }))
        {
            ResultText = await _admin.CollectAsync(Player);
        }
    }
}
