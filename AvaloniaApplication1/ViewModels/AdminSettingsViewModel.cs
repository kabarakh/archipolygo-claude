using System.Collections.Generic;
using System.Threading.Tasks;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>
/// The Admin view's "Server settings..." dialog (Admin-Funktionen.md in the
/// feature-plan archive). Current values come from the live room state; Save
/// sends one "/option" per CHANGED value only. countdown_mode and item_cheat
/// aren't part of the room state, so those start at "(unchanged)". There is
/// deliberately no way to shut the server down from here ("/exit").
/// </summary>
public partial class AdminSettingsViewModel : ViewModelBase
{
    public const string Unchanged = "(unchanged)";

    private readonly AdminPanelViewModel _admin;
    private readonly RoomSettingsSnapshot? _original;

    public AdminSettingsViewModel(AdminPanelViewModel admin, RoomSettingsSnapshot? current)
    {
        _admin = admin;
        _original = current;
        _releaseMode = current?.ReleaseMode ?? Unchanged;
        _collectMode = current?.CollectMode ?? Unchanged;
        _remainingMode = current?.RemainingMode ?? Unchanged;
        _hintCost = current?.HintCostPercentage;
        _locationCheckPoints = current?.LocationCheckPoints;
    }

    public string Title => $"Server settings - {_admin.ServerName}";

    // Valid values exactly as MultiServer.py's "/option" checks them.
    public IReadOnlyList<string> ReleaseCollectModes { get; } = [Unchanged, "disabled", "enabled", "goal", "auto", "auto_enabled"];
    public IReadOnlyList<string> RemainingModes { get; } = [Unchanged, "disabled", "enabled", "goal"];
    public IReadOnlyList<string> CountdownModes { get; } = [Unchanged, "enabled", "disabled", "auto"];
    public IReadOnlyList<string> ItemCheatValues { get; } = [Unchanged, "on", "off"];

    [ObservableProperty]
    private string _releaseMode;

    [ObservableProperty]
    private string _collectMode;

    [ObservableProperty]
    private string _remainingMode;

    [ObservableProperty]
    private string _countdownMode = Unchanged;

    [ObservableProperty]
    private string _itemCheat = Unchanged;

    [ObservableProperty]
    private decimal? _hintCost;

    [ObservableProperty]
    private decimal? _locationCheckPoints;

    public string RoomPasswordStatus => _original is null ? string.Empty
        : _original.HasPassword ? "The room currently has a password." : "The room currently has no password.";

    [ObservableProperty]
    private string _newRoomPassword = string.Empty;

    [ObservableProperty]
    private bool _removeRoomPassword;

    [ObservableProperty]
    private string _newAdminPassword = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    public bool HasResult => !string.IsNullOrEmpty(ResultText);

    /// <summary>Sends every changed value; returns true if nothing needed sending or everything was sent.</summary>
    public async Task<bool> SaveAsync()
    {
        var changes = new List<(string Option, string Value)>();

        void AddIfChanged(string option, string value, string? original)
        {
            if (value != Unchanged && value != original)
            {
                changes.Add((option, value));
            }
        }

        AddIfChanged("release_mode", ReleaseMode, _original?.ReleaseMode);
        AddIfChanged("collect_mode", CollectMode, _original?.CollectMode);
        AddIfChanged("remaining_mode", RemainingMode, _original?.RemainingMode);
        AddIfChanged("countdown_mode", CountdownMode, null);
        if (ItemCheat != Unchanged)
        {
            changes.Add(("item_cheat", ItemCheat == "on" ? "true" : "false"));
        }

        if (HintCost is { } hintCost && (int)hintCost != _original?.HintCostPercentage)
        {
            changes.Add(("hint_cost", ((int)hintCost).ToString()));
        }

        if (LocationCheckPoints is { } points && (int)points != _original?.LocationCheckPoints)
        {
            changes.Add(("location_check_points", ((int)points).ToString()));
        }

        if (RemoveRoomPassword)
        {
            changes.Add(("password", "null"));
        }
        else if (NewRoomPassword.Length > 0)
        {
            changes.Add(("password", NewRoomPassword));
        }

        if (NewAdminPassword.Length > 0)
        {
            changes.Add(("server_password", NewAdminPassword));
        }

        if (changes.Count == 0)
        {
            return true;
        }

        IsSaving = true;
        try
        {
            var results = new List<string>();
            foreach (var (option, value) in changes)
            {
                var answer = await _admin.SetOptionAsync(option, value);
                results.Add(answer);
                if (option == "server_password" && answer.StartsWith("Set option server_password", System.StringComparison.OrdinalIgnoreCase))
                {
                    _admin.UpdateStoredPassword(value);
                }
            }

            ResultText = string.Join(System.Environment.NewLine, results);
            return true;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
