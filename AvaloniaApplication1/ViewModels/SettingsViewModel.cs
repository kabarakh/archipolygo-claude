using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Checkbox form of <see cref="AppSettings.UiDensity"/> - only two values exist, so a bool reads simpler in the dialog than a two-item dropdown.</summary>
    [ObservableProperty]
    private bool _compactDensity = true;

    [ObservableProperty]
    private string? _validationError;

    // --- Notifications (feature-plan archive's Benachrichtigungen.md) ---

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

    /// <summary>
    /// Whether platform-specific blink options apply - only Windows can
    /// blink a set number of times (macOS bounces once, Linux leaves it to
    /// the window manager; see <see cref="Services.WindowAttentionService"/>).
    /// Settable so tests don't depend on the OS they run on.
    /// </summary>
    public bool SupportsBlinkCount { get; init; } = OperatingSystem.IsWindows();

    /// <summary>Native badges exist only on Windows and macOS (see <see cref="Services.IUnreadBadgeService"/>) - settable for tests, like <see cref="SupportsBlinkCount"/>.</summary>
    public bool SupportsUnreadBadge { get; init; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public bool ShowBlinkCount => SupportsBlinkCount && SelectedBlinkMode?.Mode == AttentionBlinkMode.Count;

    /// <summary>The per-category Count/Blink table, in the order shown in the dialog.</summary>
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
    /// The settings this dialog was opened with. <see cref="TryBuildSettings"/>
    /// starts from a copy of these, so every field this dialog doesn't edit
    /// itself (the theme, set by MainWindow's theme button; the filter bars'
    /// expanded state, ...) is carried through unchanged. Without this, saving
    /// the dialog built a fresh <see cref="AppSettings"/> and silently reset
    /// the theme back to "System" on disk.
    /// </summary>
    private AppSettings _originalSettings = new();

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
    /// Delegated the same way as <see cref="_checkForUpdatesAsync"/> - reads
    /// <see cref="Services.IDiagnosticLogger"/>'s current contents via
    /// <see cref="ViewModels.MainWindowViewModel.ReadDiagnosticLog"/>. The
    /// actual "save to a file" step needs a real <c>Window</c> for its file
    /// picker, so <see cref="Views.SettingsWindow"/>'s code-behind calls this
    /// to get the text and writes it out itself, same division of labor as
    /// that window's own Save/Cancel buttons.
    /// </summary>
    private Func<string>? _readDiagnosticLog;

    /// <summary>Result of the last "Export diagnostic log..." click - cleared again whenever a fresh export starts.</summary>
    [ObservableProperty]
    private string? _exportStatusText;

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

    /// <summary>
    /// The log text to write out, or null if there's nothing to export
    /// (empty log, or this view model was constructed without a callback -
    /// e.g. the design-time/parameterless path). <see cref="Views.SettingsWindow"/>'s
    /// export button handler calls this rather than invoking the delegate
    /// directly, so it doesn't need to duplicate the "empty means nothing to
    /// do" check itself.
    /// </summary>
    public string? GetDiagnosticLogTextOrNull()
    {
        var text = _readDiagnosticLog?.Invoke();
        return string.IsNullOrEmpty(text) ? null : text;
    }

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
