using System;
using System.IO;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Headless.XUnit;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// The unread attention count in window titles and the "is this group on
/// screen" rule that resets it (feature-plan archive's
/// <c>Benachrichtigungen.md</c>, step 2). Kategorie A for the view-model
/// side (<see cref="MainWindowViewModel.MainWindowTitle"/>,
/// <see cref="GroupViewModel.DetachedWindowTitle"/>, which view changes ask
/// <see cref="IAttentionTracker"/> to re-check), Kategorie C for the real
/// <see cref="MainWindow"/>'s <see cref="IGroupHostWindow.IsShowingGroup"/>
/// and title binding.
/// </summary>
public sealed class UnreadAttentionTitleTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly PersistenceService _persistenceService;
    private readonly FakeAttentionTracker _tracker = new();

    public UnreadAttentionTitleTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Archipolygo-Tests-" + Guid.NewGuid());
        _persistenceService = new PersistenceService(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private MainWindowViewModel CreateViewModel() =>
        new(_persistenceService, new FakeConnectionManager(), new MultiworldTrackerService(), attentionTracker: _tracker);

    private static MainWindowViewModel WithTwoGroups(MainWindowViewModel vm, out GroupViewModel alpha, out GroupViewModel beta)
    {
        vm.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        vm.AddNewGroup("Beta", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        alpha = vm.Groups[0];
        beta = vm.Groups[1];
        return vm;
    }

    [Fact]
    public void MainWindowTitle_NoUnread_IsPlainAppName()
    {
        Assert.Equal("Archipolygo", CreateViewModel().MainWindowTitle);
    }

    [Fact]
    public void MainWindowTitle_SumsDockedGroups_AndUpdatesLive()
    {
        var vm = WithTwoGroups(CreateViewModel(), out var alpha, out var beta);
        var changes = 0;
        vm.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(MainWindowViewModel.MainWindowTitle) ? 1 : 0;

        alpha.UnreadAttentionCount = 2;
        beta.UnreadAttentionCount = 1;

        Assert.Equal("(3) Archipolygo", vm.MainWindowTitle);
        Assert.Equal(2, changes);
    }

    /// <summary>A detached group's count belongs to its own window's title, not the main window's.</summary>
    [Fact]
    public void DetachedGroup_CountsTowardItsOwnTitle_NotMainWindowTitle()
    {
        var vm = WithTwoGroups(CreateViewModel(), out var alpha, out var beta);
        vm.DetachGroup(beta);

        alpha.UnreadAttentionCount = 1;
        beta.UnreadAttentionCount = 4;

        Assert.Equal("(1) Archipolygo", vm.MainWindowTitle);
        Assert.Equal("(4) Beta", beta.DetachedWindowTitle);

        // No longer subscribed once undocked - and resubscribed once back.
        vm.RedockGroup(beta);
        Assert.Equal("(5) Archipolygo", vm.MainWindowTitle);
    }

    /// <summary>[AvaloniaFact]: SaveSettings also applies the UI density to the Fluent theme, which needs the UI thread.</summary>
    [AvaloniaFact]
    public void ShowUnreadInTitleOff_KeepsPlainTitles_ForMainAndDetachedWindows()
    {
        var vm = WithTwoGroups(CreateViewModel(), out var alpha, out _);
        alpha.UnreadAttentionCount = 2;

        vm.SaveSettings(new AppSettings { ShowUnreadInTitle = false });

        Assert.Equal("Archipolygo", vm.MainWindowTitle);
        Assert.Equal("Alpha", alpha.DetachedWindowTitle);
    }

    /// <summary>[AvaloniaFact]: SaveSettings also applies the UI density to the Fluent theme, which needs the UI thread.</summary>
    [AvaloniaFact]
    public void ShowUnreadInTitleOff_AppliesToGroupsCreatedLater_AndAfterRestart()
    {
        CreateViewModel().SaveSettings(new AppSettings { ShowUnreadInTitle = false });

        var vm = WithTwoGroups(CreateViewModel(), out var alpha, out _);
        alpha.UnreadAttentionCount = 1;

        Assert.Equal("Alpha", alpha.DetachedWindowTitle);
        Assert.Equal("Archipolygo", vm.MainWindowTitle);
    }

    [Fact]
    public void ViewChanges_AskTrackerToRecheckWhatIsSeen()
    {
        var vm = WithTwoGroups(CreateViewModel(), out var alpha, out var beta);

        var before = _tracker.RefreshSeenStateCalls;
        vm.SelectedGroup = alpha;
        Assert.Equal(before + 1, _tracker.RefreshSeenStateCalls);

        vm.IsDashboardVisible = !vm.IsDashboardVisible;
        Assert.Equal(before + 2, _tracker.RefreshSeenStateCalls);

        var beforeDetach = _tracker.RefreshSeenStateCalls;
        vm.DetachGroup(beta);
        vm.RedockGroup(beta);
        Assert.True(_tracker.RefreshSeenStateCalls >= beforeDetach + 2);
    }

    // --- Kategorie C: the real MainWindow -----------------------------

    [AvaloniaFact]
    public void MainWindow_ShowsOnlyTheSelectedTab_AndNothingWhileDashboardIsVisible()
    {
        var vm = WithTwoGroups(CreateViewModel(), out var alpha, out var beta);
        var window = new MainWindow { DataContext = vm };

        vm.IsDashboardVisible = false;
        vm.SelectedGroup = alpha;
        Assert.True(window.IsShowingGroup(alpha.Group.Id));
        Assert.False(window.IsShowingGroup(beta.Group.Id));

        vm.IsDashboardVisible = true;
        Assert.False(window.IsShowingGroup(alpha.Group.Id));
    }

    [AvaloniaFact]
    public void MainWindow_TitleBindsToMainWindowTitle()
    {
        var vm = WithTwoGroups(CreateViewModel(), out var alpha, out _);
        var window = new MainWindow { DataContext = vm };

        alpha.UnreadAttentionCount = 7;

        Assert.Equal("(7) Archipolygo", window.Title);
    }

    [AvaloniaFact]
    public void DetachedWindow_TitleBindsToDetachedWindowTitle_AndShowsItsGroup()
    {
        var vm = WithTwoGroups(CreateViewModel(), out var alpha, out var beta);
        var window = DetachedGroupWindow.Create(alpha, new GroupWindowLocator(), _ => { });

        alpha.UnreadAttentionCount = 2;

        Assert.Equal("(2) Alpha", window.Title);
        Assert.True(window.IsShowingGroup(alpha.Group.Id));
        Assert.False(window.IsShowingGroup(beta.Group.Id));
    }
}
