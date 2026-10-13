using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>Location progress: this app's own slots (Tier 1) and the whole multiworld via the webhost tracker (Tier 2).</summary>
public partial class GroupViewModel
{
    private void SubscribeSlotProgress(SlotProfile slot) => slot.PropertyChanged += OnSlotProgressPropertyChanged;

    private void UnsubscribeSlotProgress(SlotProfile slot) => slot.PropertyChanged -= OnSlotProgressPropertyChanged;

    private void OnSlotProgressPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SlotProfile.LocationsChecked) or nameof(SlotProfile.LocationsTotal))
        {
            RaiseRoomProgressChanged();
        }
    }

    /// <summary>Tier 1: own slots' checks; a never-synced slot contributes nothing rather than a misleading 0.</summary>
    public int RoomChecksCompleted => Group.Slots.Where(s => s.LocationsChecked is not null).Sum(s => s.LocationsChecked!.Value);

    public int RoomChecksTotal => Group.Slots.Where(s => s.LocationsTotal is not null).Sum(s => s.LocationsTotal!.Value);

    public bool HasRoomProgress => Group.Slots.Any(s => s.LocationsTotal is not null);

    public string RoomProgressText => HasRoomProgress ? $"{RoomChecksCompleted}/{RoomChecksTotal}" : string.Empty;

    /// <summary>0-100.</summary>
    public double RoomProgressPercent => RoomChecksTotal > 0 ? (double)RoomChecksCompleted / RoomChecksTotal * 100.0 : 0;

    private void RaiseRoomProgressChanged()
    {
        OnPropertyChanged(nameof(RoomChecksCompleted));
        OnPropertyChanged(nameof(RoomChecksTotal));
        OnPropertyChanged(nameof(HasRoomProgress));
        OnPropertyChanged(nameof(RoomProgressText));
        OnPropertyChanged(nameof(RoomProgressPercent));

        // OwnChecks* in the combined bar are Tier 1 values.
        RaiseCombinedProgressChanged();
    }

    /// <summary>Tier 2: whole-multiworld progress via the webhost tracker, if a tracker id is resolved.</summary>
    public bool HasMultiworldTracker => !string.IsNullOrWhiteSpace(Group.TrackerId);

    [ObservableProperty]
    private bool _isRefreshingMultiworldProgress;

    /// <summary>Set on a failed refresh (the tracker service never throws); cleared on the next success.</summary>
    [ObservableProperty]
    private string? _multiworldProgressError;

    /// <summary>Per-player tracker data; only shown aggregated (could be hundreds of players).</summary>
    public ObservableCollection<PlayerProgress> MultiworldProgress { get; } = new();

    /// <summary>Whole room, own slots included.</summary>
    public int MultiworldChecksDone => MultiworldProgress.Sum(p => p.ChecksDone);

    public int MultiworldChecksTotal => MultiworldProgress.Sum(p => p.ChecksTotal ?? 0);

    /// <summary>
    /// Tier 1 data rather than matching tracker players to own slots: the tracker gives
    /// no reliable slot-name mapping, so "others" is whole room minus own.
    /// </summary>
    public int OwnChecksDone => RoomChecksCompleted;

    public int OwnChecksTotal => RoomChecksTotal;

    public int OwnChecksOpen => Math.Max(0, OwnChecksTotal - OwnChecksDone);

    /// <summary>
    /// Clamped at 0: live Tier 1 and polled Tier 2 (up to minutes stale) can briefly
    /// disagree about "own", e.g. right after a check the tracker hasn't seen yet.
    /// </summary>
    public int OtherChecksDone => Math.Max(0, MultiworldChecksDone - OwnChecksDone);

    public int OtherChecksTotal => Math.Max(0, MultiworldChecksTotal - OwnChecksTotal);

    public int OtherChecksOpen => Math.Max(0, OtherChecksTotal - OtherChecksDone);

    /// <summary>Without a tracker the same bar shows with its "other" segments at zero width.</summary>
    public bool HasAnyProgress => OwnChecksTotal + OtherChecksTotal > 0;

    private int GrandTotalChecks => OwnChecksTotal + OtherChecksTotal;

    private static string FormatPercentOfGrandTotal(int part, int grandTotal) =>
        grandTotal > 0 ? $"{Math.Round(part * 100.0 / grandTotal)}%" : "0%";

    // Legend lines for the combined bar's tooltip.
    public string OwnChecksDoneLegendText => $"Own, done: {OwnChecksDone} ({FormatPercentOfGrandTotal(OwnChecksDone, GrandTotalChecks)})";

    public string OwnChecksOpenLegendText => $"Own, open: {OwnChecksOpen} ({FormatPercentOfGrandTotal(OwnChecksOpen, GrandTotalChecks)})";

    public string OtherChecksDoneLegendText => $"Others, done: {OtherChecksDone} ({FormatPercentOfGrandTotal(OtherChecksDone, GrandTotalChecks)})";

    public string OtherChecksOpenLegendText => $"Others, open: {OtherChecksOpen} ({FormatPercentOfGrandTotal(OtherChecksOpen, GrandTotalChecks)})";

    private void RaiseCombinedProgressChanged()
    {
        OnPropertyChanged(nameof(MultiworldChecksDone));
        OnPropertyChanged(nameof(MultiworldChecksTotal));
        OnPropertyChanged(nameof(OwnChecksDone));
        OnPropertyChanged(nameof(OwnChecksTotal));
        OnPropertyChanged(nameof(OwnChecksOpen));
        OnPropertyChanged(nameof(OtherChecksDone));
        OnPropertyChanged(nameof(OtherChecksTotal));
        OnPropertyChanged(nameof(OtherChecksOpen));
        OnPropertyChanged(nameof(HasAnyProgress));
        OnPropertyChanged(nameof(OwnChecksDoneLegendText));
        OnPropertyChanged(nameof(OwnChecksOpenLegendText));
        OnPropertyChanged(nameof(OtherChecksDoneLegendText));
        OnPropertyChanged(nameof(OtherChecksOpenLegendText));
    }

    /// <summary>Safe to call often: the tracker service throttles to the API's cache timers.</summary>
    [RelayCommand]
    private async Task RefreshMultiworldProgressAsync()
    {
        if (_multiworldTrackerService is null || string.IsNullOrWhiteSpace(Group.TrackerId))
        {
            return;
        }

        IsRefreshingMultiworldProgress = true;
        try
        {
            var snapshot = await _multiworldTrackerService.GetProgressAsync(Group.TrackerId);
            if (snapshot is null)
            {
                MultiworldProgressError = "Could not fetch multiworld progress right now.";
                return;
            }

            MultiworldProgressError = null;
            MultiworldProgress.Clear();
            foreach (var player in snapshot.Players.OrderBy(p => p.Player))
            {
                MultiworldProgress.Add(player);
            }

            RaiseCombinedProgressChanged();
        }
        finally
        {
            IsRefreshingMultiworldProgress = false;
        }
    }

    private void OnTrackerIdChanged()
    {
        OnPropertyChanged(nameof(HasMultiworldTracker));

        // Drop progress that belonged to the previous tracker id.
        MultiworldProgressError = null;
        MultiworldProgress.Clear();
        RaiseCombinedProgressChanged();

        if (HasMultiworldTracker && IsSelected)
        {
            _ = RefreshMultiworldProgressAsync();
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        // Fetched lazily on first selection instead of polling every group in the background.
        if (HasMultiworldTracker && MultiworldProgress.Count == 0 && !IsRefreshingMultiworldProgress)
        {
            _ = RefreshMultiworldProgressAsync();
        }
    }
}
