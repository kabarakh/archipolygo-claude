using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>Editor for the global <see cref="AppSettings"/>, shown by <see cref="Views.SettingsWindow"/>.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    public string AppVersion => AppVersionInfo.Current;

    [ObservableProperty]
    private bool _defaultAutoConnect;

    [ObservableProperty]
    private int _eventHistoryLimit = 500;

    [ObservableProperty]
    private bool _compactDensity = true;

    [ObservableProperty]
    private string? _validationError;

    // --- Notifications ---

    public sealed record BlinkModeOption(AttentionBlinkMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<BlinkModeOption> BlinkModes { get; } = new[]
    {
        new BlinkModeOption(AttentionBlinkMode.Count, "Blink a few times"),
        new BlinkModeOption(AttentionBlinkMode.UntilFocus, "Blink until focused"),
        new BlinkModeOption(AttentionBlinkMode.Off, "Don't blink"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBlinkCount))]
    private BlinkModeOption? _selectedBlinkMode;

    [ObservableProperty]
    private int _blinkCount = 4;

    [ObservableProperty]
    private bool _showUnreadInTitle = true;

    [ObservableProperty]
    private bool _showUnreadBadge = true;

    /// <summary>Only Windows can blink a set number of times (macOS bounces once, Linux leaves it to the WM). Settable for tests.</summary>
    public bool SupportsBlinkCount { get; init; } = OperatingSystem.IsWindows();

    /// <summary>Native badges exist only on Windows and macOS. Settable for tests.</summary>
    public bool SupportsUnreadBadge { get; init; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public bool ShowBlinkCount => SupportsBlinkCount && SelectedBlinkMode?.Mode == AttentionBlinkMode.Count;

    public IReadOnlyList<AttentionCategoryRowViewModel> AttentionCategories { get; private init; } = BuildCategoryRows(new AppSettings());

    private static IReadOnlyList<AttentionCategoryRowViewModel> BuildCategoryRows(AppSettings settings) =>
        new (AttentionCategory Category, string Label)[]
            {
                (AttentionCategory.OwnHint, "Hint for one of my slots"),
                (AttentionCategory.DeathLink, "DeathLink"),
                (AttentionCategory.ProgressionItem, "Progression item for me"),
                (AttentionCategory.OtherItem, "Other items for me"),
                (AttentionCategory.ChatMention, "Chat mentioning my slot"),
                (AttentionCategory.Chat, "Any other chat message"),
            }
            .Select(c =>
            {
                var setting = settings.GetAttentionCategorySetting(c.Category);
                return new AttentionCategoryRowViewModel { Category = c.Category, Label = c.Label, Count = setting.Count, Blink = setting.Blink };
            })
            .ToList();

    /// <summary>
    /// <see cref="TryBuildSettings"/> starts from a copy of these so fields this dialog doesn't edit
    /// (theme, filter-bar state, ...) survive; building a fresh AppSettings used to reset the theme.
    /// </summary>
    private AppSettings _originalSettings = new();

    /// <summary>Supplied by the opener, since this view model has no <c>IUpdateService</c>; null makes the button a no-op.</summary>
    private Func<Task<string?>>? _checkForUpdatesAsync;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private string? _updateCheckStatusText;

    /// <summary>Supplied by the opener; the file picker needs a real Window, so SettingsWindow's code-behind does the actual save.</summary>
    private Func<string>? _readDiagnosticLog;

    [ObservableProperty]
    private string? _exportStatusText;

    /// <summary>
    /// True for a manually unzipped (non-Velopack) build: the update check would always find nothing
    /// and misleadingly read as "up to date", so a hint replaces the button.
    /// </summary>
    public bool ShowUnmanagedInstallHint { get; private init; }

    public SettingsViewModel()
    {
        _selectedBlinkMode = BlinkModes[0];
    }

    public static SettingsViewModel FromSettings(AppSettings settings, Func<Task<string?>>? checkForUpdatesAsync = null, bool showUnmanagedInstallHint = false, Func<string>? readDiagnosticLog = null,
                                                 bool? supportsBlinkCount = null)
    {
        var viewModel = new SettingsViewModel
        {
            SupportsBlinkCount = supportsBlinkCount ?? OperatingSystem.IsWindows(),
            AttentionCategories = BuildCategoryRows(settings),
            BlinkCount = settings.AttentionBlinkCount,
            ShowUnreadInTitle = settings.ShowUnreadInTitle,
            ShowUnreadBadge = settings.ShowUnreadBadge,
            DefaultAutoConnect = settings.DefaultAutoConnect,
            EventHistoryLimit = settings.EventHistoryLimit,
            CompactDensity = settings.UiDensity != DensityService.Normal,
            _originalSettings = settings.Clone(),
            _checkForUpdatesAsync = checkForUpdatesAsync,
            ShowUnmanagedInstallHint = showUnmanagedInstallHint,
            _readDiagnosticLog = readDiagnosticLog
        };
        viewModel.SelectedBlinkMode = viewModel.BlinkModes.First(m => m.Mode == settings.BlinkMode);
        return viewModel;
    }

    /// <summary>Null when there's nothing to export (empty log or no callback).</summary>
    public string? GetDiagnosticLogTextOrNull()
    {
        var text = _readDiagnosticLog?.Invoke();
        return string.IsNullOrEmpty(text) ? null : text;
    }

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

        if (BlinkCount < 1)
        {
            ValidationError = "Blink count must be at least 1.";
            settings = null!;
            return false;
        }

        ValidationError = null;
        settings = _originalSettings.Clone();
        settings.DefaultAutoConnect = DefaultAutoConnect;
        settings.EventHistoryLimit = EventHistoryLimit;
        settings.UiDensity = CompactDensity ? DensityService.Compact : DensityService.Normal;
        settings.AttentionBlinkMode = (SelectedBlinkMode?.Mode ?? AttentionBlinkMode.Count).ToString();
        settings.AttentionBlinkCount = BlinkCount;
        settings.ShowUnreadInTitle = ShowUnreadInTitle;
        settings.ShowUnreadBadge = ShowUnreadBadge;
        foreach (var row in AttentionCategories)
        {
            settings.AttentionCategories[row.Category.ToString()] = new AttentionCategorySetting { Count = row.Count, Blink = row.Blink };
        }

        return true;
    }
}
