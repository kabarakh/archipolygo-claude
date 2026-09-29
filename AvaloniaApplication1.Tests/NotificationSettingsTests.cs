using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Step 3 of the feature-plan archive's <c>Benachrichtigungen.md</c>: the
/// Settings dialog's "Notifications" section and the per-server mute toggle
/// in Edit server. Kategorie A for the view models, Kategorie C for the
/// real <see cref="SettingsWindow"/>.
/// </summary>
public sealed class NotificationSettingsTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "Archipolygo-Tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    // --- SettingsViewModel --------------------------------------------

    [Fact]
    public void FromSettings_ThenTryBuild_RoundTripsEveryNotificationField()
    {
        var original = new AppSettings { AttentionBlinkMode = "UntilFocus", AttentionBlinkCount = 6, ShowUnreadInTitle = false };
        original.AttentionCategories[nameof(AttentionCategory.Chat)] = new AttentionCategorySetting { Count = true, Blink = false };

        var viewModel = SettingsViewModel.FromSettings(original);
        Assert.Equal(AttentionBlinkMode.UntilFocus, viewModel.SelectedBlinkMode!.Mode);
        Assert.Equal(6, viewModel.BlinkCount);
        Assert.False(viewModel.ShowUnreadInTitle);

        Assert.True(viewModel.TryBuildSettings(out var built));
        Assert.Equal(AttentionBlinkMode.UntilFocus, built.BlinkMode);
        Assert.Equal(6, built.AttentionBlinkCount);
        Assert.False(built.ShowUnreadInTitle);
        Assert.True(built.GetAttentionCategorySetting(AttentionCategory.Chat).Count);
        Assert.False(built.GetAttentionCategorySetting(AttentionCategory.Chat).Blink);
    }

    [Fact]
    public void EditedRows_EndUpInBuiltSettings_WithoutTouchingTheOriginal()
    {
        var original = new AppSettings();
        var viewModel = SettingsViewModel.FromSettings(original);

        var deathLink = viewModel.AttentionCategories.Single(r => r.Category == AttentionCategory.DeathLink);
        deathLink.Blink = false;
        viewModel.SelectedBlinkMode = viewModel.BlinkModes.Single(m => m.Mode == AttentionBlinkMode.Off);

        Assert.True(viewModel.TryBuildSettings(out var built));
        Assert.False(built.GetAttentionCategorySetting(AttentionCategory.DeathLink).Blink);
        Assert.Equal(AttentionBlinkMode.Off, built.BlinkMode);
        Assert.True(original.GetAttentionCategorySetting(AttentionCategory.DeathLink).Blink);
    }

    [Fact]
    public void Categories_CoverEveryAttentionCategoryExactlyOnce()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings());

        Assert.Equal(Enum.GetValues<AttentionCategory>().OrderBy(c => c), viewModel.AttentionCategories.Select(r => r.Category).OrderBy(c => c));
    }

    [Theory]
    [InlineData(true, AttentionBlinkMode.Count, true)]
    [InlineData(true, AttentionBlinkMode.UntilFocus, false)]
    [InlineData(true, AttentionBlinkMode.Off, false)]
    [InlineData(false, AttentionBlinkMode.Count, false)]
    public void ShowBlinkCount_OnlyForCountModeOnPlatformsThatSupportIt(bool supportsBlinkCount, AttentionBlinkMode mode, bool expected)
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings(), supportsBlinkCount: supportsBlinkCount);

        viewModel.SelectedBlinkMode = viewModel.BlinkModes.Single(m => m.Mode == mode);

        Assert.Equal(expected, viewModel.ShowBlinkCount);
    }

    [Fact]
    public void BlinkCountBelowOne_IsRejected()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings());
        viewModel.BlinkCount = 0;

        Assert.False(viewModel.TryBuildSettings(out _));
        Assert.NotNull(viewModel.ValidationError);
    }

    // --- SettingsWindow (Kategorie C) ---------------------------------

    [AvaloniaFact]
    public void SettingsWindow_ShowsOneRowPerCategory_WithCountAndBlinkCheckboxes()
    {
        var window = new SettingsWindow { DataContext = SettingsViewModel.FromSettings(new AppSettings()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Notifications", labels);
        Assert.Contains("Hint for one of my slots", labels);
        Assert.Contains("Any other chat message", labels);

        var itemsControl = window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(i => i.ItemsSource == ((SettingsViewModel)window.DataContext!).AttentionCategories);
        var checkBoxes = itemsControl.GetVisualDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(Enum.GetValues<AttentionCategory>().Length * 2, checkBoxes.Count);
    }

    [AvaloniaFact]
    public void SettingsWindow_TimesField_HiddenWhereBlinkCountIsUnsupported()
    {
        var window = new SettingsWindow { DataContext = SettingsViewModel.FromSettings(new AppSettings(), supportsBlinkCount: false) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var times = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Times");
        Assert.False(times.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void SettingsWindow_TimesField_VisibleOnWindowsInCountMode()
    {
        var window = new SettingsWindow { DataContext = SettingsViewModel.FromSettings(new AppSettings(), supportsBlinkCount: true) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var times = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Times");
        Assert.True(times.IsEffectivelyVisible);
    }

    // --- Per-server mute (Edit server) --------------------------------

    [Fact]
    public void EditGroup_PrefillsMute_AndReturnsIt()
    {
        var group = new ServerConnectionGroup { Name = "Loud", NotificationsMuted = true };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });

        var viewModel = ConnectionEditorViewModel.ForEditGroup(group);
        Assert.True(viewModel.ShowNotificationsMuted);
        Assert.True(viewModel.NotificationsMuted);

        viewModel.NotificationsMuted = false;
        Assert.True(viewModel.TryBuildResult(out var result));
        Assert.False(result.NotificationsMuted);
    }

    [Fact]
    public void NewGroup_DoesNotOfferMute()
    {
        Assert.False(ConnectionEditorViewModel.ForNewGroup().ShowNotificationsMuted);
    }

    [AvaloniaFact]
    public async Task UpdateGroup_Muting_PersistsFlag_AndClearsUnreadCount()
    {
        var persistence = new PersistenceService(_tempDirectory);
        var mainWindowViewModel = new MainWindowViewModel(persistence, new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Loud", "host", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        group.UnreadAttentionCount = 5;

        await mainWindowViewModel.UpdateGroup(group, "Loud", "host", 1, string.Empty, false, null, notificationsMuted: true);

        Assert.True(group.Group.NotificationsMuted);
        Assert.Equal(0, group.UnreadAttentionCount);
        Assert.True(Assert.Single(persistence.LoadGroups()).NotificationsMuted);
    }

    // --- Context-menu toggle + muted icon ------------------------------

    [AvaloniaFact]
    public void ToggleNotificationsMuted_FlipsFlag_Persists_ClearsCount_AndFlipsMenuLabel()
    {
        var persistence = new PersistenceService(_tempDirectory);
        var mainWindowViewModel = new MainWindowViewModel(persistence, new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Loud", "host", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        group.UnreadAttentionCount = 3;
        Assert.Equal("Mute notifications", group.MuteMenuHeader);

        mainWindowViewModel.ToggleNotificationsMuted(group);

        Assert.True(group.IsNotificationsMuted);
        Assert.Equal(0, group.UnreadAttentionCount);
        Assert.Equal("Unmute notifications", group.MuteMenuHeader);
        Assert.True(Assert.Single(persistence.LoadGroups()).NotificationsMuted);

        mainWindowViewModel.ToggleNotificationsMuted(group);

        Assert.False(group.IsNotificationsMuted);
        Assert.False(Assert.Single(persistence.LoadGroups()).NotificationsMuted);
    }

    private static MainWindow ShowMainWindow(MainWindowViewModel viewModel)
    {
        var window = new MainWindow { DataContext = viewModel, Width = 1200, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Avalonia.Controls.Shapes.Path? MutedIconIn(Avalonia.Visual root) =>
        root.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
            .FirstOrDefault(p => p.Data == Avalonia.Application.Current!.FindResource("BellOffGeometry"));

    [AvaloniaFact]
    public void TabHeader_ShowsMutedIcon_OnlyWhileMuted()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Loud", "host", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        mainWindowViewModel.IsDashboardVisible = false;
        var window = ShowMainWindow(mainWindowViewModel);
        var tabItem = window.GetVisualDescendants().OfType<TabItem>().Single();

        var icon = MutedIconIn(tabItem);
        Assert.NotNull(icon);
        Assert.False(icon!.IsEffectivelyVisible);

        mainWindowViewModel.ToggleNotificationsMuted(group);
        Dispatcher.UIThread.RunJobs();

        Assert.True(icon.IsEffectivelyVisible);
        Assert.NotNull(icon.Stroke);
    }

    [AvaloniaFact]
    public void DashboardRow_ShowsMutedIcon_WhileMuted()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Loud", "host", 1, string.Empty, "Alice", autoConnect: false);
        mainWindowViewModel.Groups[0].Group.NotificationsMuted = true;
        mainWindowViewModel.IsDashboardVisible = true;
        var window = ShowMainWindow(mainWindowViewModel);
        var row = window.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext == mainWindowViewModel.Groups[0]);

        var icon = MutedIconIn(row);
        Assert.NotNull(icon);
        Assert.True(icon!.IsEffectivelyVisible);
        Assert.NotNull(icon.Stroke);
    }

    [AvaloniaFact]
    public async Task UpdateGroup_WithoutMuteArgument_LeavesMuteUntouched()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Loud", "host", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        group.Group.NotificationsMuted = true;

        await mainWindowViewModel.UpdateGroup(group, "Renamed", "host", 1, string.Empty, false, null);

        Assert.True(group.Group.NotificationsMuted);
    }
}
