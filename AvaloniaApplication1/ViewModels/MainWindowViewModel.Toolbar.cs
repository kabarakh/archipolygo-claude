using System.Threading.Tasks;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>The toolbar's theme button and update indicator.</summary>
public partial class MainWindowViewModel
{
    /// <summary>"System"/"Light"/"Dark"; only drives the theme button - the applied theme lives on the Application.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeButtonGlyph))]
    [NotifyPropertyChangedFor(nameof(ThemeButtonTooltip))]
    private string _themePreference = "System";

    public string ThemeButtonGlyph => ThemePreference switch
    {
        "Light" => "☀",
        "Dark" => "🌙",
        _ => "◐"
    };

    public string ThemeButtonTooltip => $"Theme: {ThemePreference} (click to switch)";

    [RelayCommand]
    private void CycleTheme()
    {
        var settings = LoadSettings();
        settings.ThemePreference = ThemeService.Next(settings.ThemePreference);
        SaveSettings(settings);

        ThemePreference = settings.ThemePreference;
        ThemeService.Apply(ThemePreference);
    }

    /// <summary>Drives the dot next to the "Settings..." button.</summary>
    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _newUpdateVersion;

    /// <summary>
    /// Auto-update is supported on this platform but this install isn't Velopack-managed
    /// (e.g. manually unzipped). Fixed for the process lifetime.
    /// </summary>
    public bool ShowUnmanagedInstallHint =>
        _updateService is not null && _updateService.SupportsManagedInstall && !_updateService.IsManagedInstall;

    /// <summary>Called at startup and from Settings' "Check for updates", so both keep the dot current.</summary>
    public async Task<string?> CheckForUpdatesAsync()
    {
        if (_updateService is null)
        {
            return null;
        }

        var version = await _updateService.CheckForUpdatesAsync();
        NewUpdateVersion = version;
        IsUpdateAvailable = version is not null;
        return version;
    }

    /// <summary>Downloads and applies the last found update, restarting the app.</summary>
    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_updateService is null)
        {
            return;
        }

        await _updateService.DownloadAndApplyUpdateAsync();
    }
}
