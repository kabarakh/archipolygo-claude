using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>
/// Editor view model for the global <see cref="AppSettings"/>.
/// Shown as a dialog by <see cref="Views.SettingsWindow"/>.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _defaultAutoConnect;

    [ObservableProperty]
    private int _eventHistoryLimit = 500;

    [ObservableProperty]
    private string? _validationError;

    public static SettingsViewModel FromSettings(AppSettings settings) => new()
    {
        DefaultAutoConnect = settings.DefaultAutoConnect,
        EventHistoryLimit = settings.EventHistoryLimit
    };

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
