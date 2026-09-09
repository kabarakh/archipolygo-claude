using System;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>
/// Editor view model for the global <see cref="AppSettings"/>.
/// Shown as a dialog by <see cref="Views.SettingsWindow"/>.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    /// <summary>
    /// "dev" for a local build, the release tag for a published one - see
    /// <see cref="AppVersionInfo"/>. Never changes at runtime, so a plain
    /// property is enough; no <c>[ObservableProperty]</c> needed.
    /// </summary>
    public string AppVersion => AppVersionInfo.Current;

    [ObservableProperty]
    private bool _defaultAutoConnect;

    [ObservableProperty]
    private int _eventHistoryLimit = 500;

    [ObservableProperty]
    private string? _validationError;

    /// <summary>
    /// Delegated to whoever opened this dialog (see <see cref="FromSettings"/>) -
    /// same callback-based design as the rest of this codebase's dialog view
    /// models (e.g. <see cref="ConnectionEditorViewModel"/>'s <c>resolveTrackerId</c>),
    /// since this lightweight view model has no <c>IUpdateService</c> of its
    /// own. Null (the design-time/parameterless-construction default) just
    /// means the button below silently does nothing.
    /// </summary>
    private Func<Task<string?>>? _checkForUpdatesAsync;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    /// <summary>Result of the last "Check for updates" click - cleared again whenever a fresh check starts.</summary>
    [ObservableProperty]
    private string? _updateCheckStatusText;

    /// <summary>
    /// Whether this is a manually downloaded/unzipped build rather than one
    /// installed via Velopack's own installer - see
    /// <see cref="MainWindowViewModel.ShowUnmanagedInstallHint"/>, which this
    /// is set from (the same "delegated to whoever opened this dialog"
    /// reasoning as <see cref="_checkForUpdatesAsync"/>: this lightweight
    /// view model has no <c>IUpdateService</c> of its own to ask directly).
    /// When true, "Check for updates" below is replaced by an explanatory
    /// hint instead - clicking it would only ever silently find nothing,
    /// which would misleadingly read as "you're up to date" rather than
    /// "this install can't check at all".
    /// </summary>
    public bool ShowUnmanagedInstallHint { get; private init; }

    public static SettingsViewModel FromSettings(AppSettings settings, Func<Task<string?>>? checkForUpdatesAsync = null, bool showUnmanagedInstallHint = false) => new()
    {
        DefaultAutoConnect = settings.DefaultAutoConnect,
        EventHistoryLimit = settings.EventHistoryLimit,
        _checkForUpdatesAsync = checkForUpdatesAsync,
        ShowUnmanagedInstallHint = showUnmanagedInstallHint
    };

    /// <summary>
    /// Manual equivalent of the app's own startup check (see
    /// <see cref="MainWindowViewModel.CheckForUpdatesAsync"/>, which this
    /// calls into via the same callback) - lets a user confirm they're on
    /// the latest version on demand, not just whatever the app happened to
    /// find at launch. No-op if <see cref="ShowUnmanagedInstallHint"/> - the
    /// button that triggers this is hidden in that case anyway (see
    /// SettingsWindow.axaml), but this guard keeps the two in sync
    /// regardless of how the command ends up invoked.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (_checkForUpdatesAsync is null || ShowUnmanagedInstallHint)
        {
            return;
        }

        IsCheckingForUpdates = true;
        UpdateCheckStatusText = null;
        try
        {
            var version = await _checkForUpdatesAsync();
            UpdateCheckStatusText = version is not null
                ? $"Update available: version {version}"
                : "You're up to date.";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    public bool TryBuildSettings(out AppSettings settings)
    {
        if (EventHistoryLimit < 1)
        {
            ValidationError = "Event history limit must be at least 1.";
            settings = null!;
            return false;
        }

        ValidationError = null;
        settings = new AppSettings
        {
            DefaultAutoConnect = DefaultAutoConnect,
            EventHistoryLimit = EventHistoryLimit
        };
        return true;
    }
}
