using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="AttentionTracker"/>'s
/// decision logic from the feature-plan archive's
/// <c>Benachrichtigungen.md</c> - per-category blink toggle, blink mode,
/// per-server mute, and the "no second blink until the user has looked"
/// throttling - against a <see cref="FakeWindowAttentionService"/> instead of
/// real windows. [AvaloniaFact] only because <see cref="AttentionTracker.Report"/>
/// posts to the UI thread.
/// </summary>
public class AttentionTrackerTests
{
    private readonly FakeWindowAttentionService _windows = new();
    private readonly AttentionTracker _tracker;

    public AttentionTrackerTests()
    {
        _tracker = new AttentionTracker(_windows, new FakePersistenceService());
    }

    private static GroupViewModel MakeGroup(string name = "Test Server") =>
        new(new ServerConnectionGroup { Name = name }, new FakeConnectionManager());

    private void Report(GroupViewModel group, AttentionCategory category)
    {
        _tracker.Report(group, category);
        Dispatcher.UIThread.RunJobs();
    }

    private void Apply(AppSettings settings)
    {
        _tracker.ApplySettings(settings);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void DefaultBlinkCategory_InactiveWindow_BlinksWithDefaultModeAndCount()
    {
        Report(MakeGroup(), AttentionCategory.ProgressionItem);

        var request = Assert.Single(_windows.Requests);
        Assert.Same(_windows.MainWindow, request.Window);
        Assert.Equal(AttentionBlinkMode.Count, request.Mode);
        Assert.Equal(4, request.Count);
    }

    [AvaloniaFact]
    public void CategoryOffByDefault_DoesNotBlink()
    {
        var group = MakeGroup();

        Report(group, AttentionCategory.OtherItem);
        Report(group, AttentionCategory.Chat);

        Assert.Empty(_windows.Requests);
    }

    [AvaloniaFact]
    public void ActiveWindow_DoesNotBlink()
    {
        _windows.MainWindow.IsActive = true;

        Report(MakeGroup(), AttentionCategory.DeathLink);

        Assert.Empty(_windows.Requests);
    }

    [AvaloniaFact]
    public void MutedServer_NeverBlinks()
    {
        var group = MakeGroup();
        group.Group.NotificationsMuted = true;

        Report(group, AttentionCategory.DeathLink);

        Assert.Empty(_windows.Requests);
    }

    [AvaloniaFact]
    public void SecondReportBeforeUserLooked_IsThrottled()
    {
        var group = MakeGroup();

        Report(group, AttentionCategory.ProgressionItem);
        Report(group, AttentionCategory.OwnHint);
        Report(MakeGroup("Other server in the same window"), AttentionCategory.DeathLink);

        Assert.Single(_windows.Requests);
    }

    [AvaloniaFact]
    public void ReportAfterWindowWasActivatedAgain_BlinksAgain()
    {
        var group = MakeGroup();
        Report(group, AttentionCategory.ProgressionItem);

        _windows.Activate(_windows.MainWindow);
        _windows.MainWindow.IsActive = false;
        Report(group, AttentionCategory.ProgressionItem);

        Assert.Equal(2, _windows.Requests.Count);
    }

    [AvaloniaFact]
    public void ThrottlingIsPerWindow_DetachedWindowStillBlinks()
    {
        var mainGroup = MakeGroup("Main");
        var detachedGroup = MakeGroup("Detached");
        var detachedWindow = _windows.Detach(detachedGroup.Group.Id);

        Report(mainGroup, AttentionCategory.ProgressionItem);
        Report(detachedGroup, AttentionCategory.ProgressionItem);

        Assert.Equal(new object[] { _windows.MainWindow, detachedWindow }, _windows.Requests.Select(r => (object)r.Window));
    }

    [AvaloniaFact]
    public void BlinkModeOff_NeverBlinks()
    {
        Apply(new AppSettings { AttentionBlinkMode = "Off" });

        Report(MakeGroup(), AttentionCategory.DeathLink);

        Assert.Empty(_windows.Requests);
    }

    [AvaloniaFact]
    public void AppliedSettings_ModeCountAndCategoryToggleAreUsed()
    {
        var settings = new AppSettings { AttentionBlinkMode = "UntilFocus", AttentionBlinkCount = 9 };
        settings.AttentionCategories[nameof(AttentionCategory.Chat)] = new AttentionCategorySetting { Count = true, Blink = true };
        Apply(settings);

        Report(MakeGroup(), AttentionCategory.Chat);

        var request = Assert.Single(_windows.Requests);
        Assert.Equal(AttentionBlinkMode.UntilFocus, request.Mode);
        Assert.Equal(9, request.Count);
    }

    /// <summary>A category that counts but doesn't blink must stay silent - "Count" and "Blink" are independent toggles.</summary>
    [AvaloniaFact]
    public void CategoryCountOnly_DoesNotBlink()
    {
        var settings = new AppSettings();
        settings.AttentionCategories[nameof(AttentionCategory.DeathLink)] = new AttentionCategorySetting { Count = true, Blink = false };
        Apply(settings);

        Report(MakeGroup(), AttentionCategory.DeathLink);

        Assert.Empty(_windows.Requests);
    }

    /// <summary>The tracker keeps its own copy - editing the settings object afterwards (like the Settings dialog's working copy) mustn't change behavior until applied again.</summary>
    [AvaloniaFact]
    public void AppliedSettings_LaterMutationOfTheSameObject_HasNoEffect()
    {
        var settings = new AppSettings();
        Apply(settings);
        settings.AttentionCategories[nameof(AttentionCategory.DeathLink)].Blink = false;

        Report(MakeGroup(), AttentionCategory.DeathLink);

        Assert.Single(_windows.Requests);
    }

    [AvaloniaFact]
    public void AppSettingsClone_DoesNotShareCategoryDictionary()
    {
        var original = new AppSettings();
        var clone = original.Clone();

        clone.AttentionCategories[nameof(AttentionCategory.OwnHint)].Blink = false;

        Assert.True(original.GetAttentionCategorySetting(AttentionCategory.OwnHint).Blink);
    }

    // --- Counting / "seen" ---------------------------------------------

    [AvaloniaFact]
    public void DefaultCountCategories_NotSeen_RaiseUnreadCount()
    {
        var group = MakeGroup();

        Report(group, AttentionCategory.OwnHint);
        Report(group, AttentionCategory.DeathLink);
        Report(group, AttentionCategory.OtherItem); // off by default

        Assert.Equal(2, group.UnreadAttentionCount);
    }

    [AvaloniaFact]
    public void SeenGroup_NeitherCountsNorBlinks()
    {
        var group = MakeGroup();
        _windows.MainWindow.IsActive = true;
        _windows.MainWindowShowingGroupId = group.Group.Id;

        Report(group, AttentionCategory.ProgressionItem);

        Assert.Equal(0, group.UnreadAttentionCount);
        Assert.Empty(_windows.Requests);
    }

    /// <summary>Main window focused, but a different tab (or the Dashboard) showing: counts, but no blink - the user is already at the window.</summary>
    [AvaloniaFact]
    public void ActiveWindowShowingAnotherTab_CountsButDoesNotBlink()
    {
        var group = MakeGroup();
        _windows.MainWindow.IsActive = true;
        _windows.MainWindowShowingGroupId = null;

        Report(group, AttentionCategory.ProgressionItem);

        Assert.Equal(1, group.UnreadAttentionCount);
        Assert.Empty(_windows.Requests);
    }

    [AvaloniaFact]
    public void CountOnlyCategory_CountsWithoutBlinking_BlinkOnlyCategory_BlinksWithoutCounting()
    {
        var settings = new AppSettings();
        settings.AttentionCategories[nameof(AttentionCategory.OtherItem)] = new AttentionCategorySetting { Count = true, Blink = false };
        settings.AttentionCategories[nameof(AttentionCategory.DeathLink)] = new AttentionCategorySetting { Count = false, Blink = true };
        Apply(settings);
        var group = MakeGroup();

        Report(group, AttentionCategory.OtherItem);
        Assert.Equal(1, group.UnreadAttentionCount);
        Assert.Empty(_windows.Requests);

        Report(group, AttentionCategory.DeathLink);
        Assert.Equal(1, group.UnreadAttentionCount);
        Assert.Single(_windows.Requests);
    }

    [AvaloniaFact]
    public void MutedServer_DoesNotCount()
    {
        var group = MakeGroup();
        group.Group.NotificationsMuted = true;

        Report(group, AttentionCategory.OwnHint);

        Assert.Equal(0, group.UnreadAttentionCount);
    }

    /// <summary>Throttling only affects blinking - every event still counts.</summary>
    [AvaloniaFact]
    public void ThrottledReports_StillCount()
    {
        var group = MakeGroup();

        Report(group, AttentionCategory.ProgressionItem);
        Report(group, AttentionCategory.ProgressionItem);
        Report(group, AttentionCategory.ProgressionItem);

        Assert.Equal(3, group.UnreadAttentionCount);
        Assert.Single(_windows.Requests);
    }

    [AvaloniaFact]
    public void RefreshSeenState_ResetsOnlyTheGroupNowSeen()
    {
        var shown = MakeGroup("Shown");
        var other = MakeGroup("Other");
        Report(shown, AttentionCategory.OwnHint);
        Report(other, AttentionCategory.OwnHint);

        _windows.MainWindow.IsActive = true;
        _windows.MainWindowShowingGroupId = shown.Group.Id;
        _tracker.RefreshSeenState();

        Assert.Equal(0, shown.UnreadAttentionCount);
        Assert.Equal(1, other.UnreadAttentionCount);
    }

    /// <summary>Window activation is picked up by the tracker itself (via WindowAcknowledged), deferred one dispatcher turn - see OnWindowAcknowledged's comment on Avalonia raising Activated before IsActive.</summary>
    [AvaloniaFact]
    public void WindowActivation_ResetsTheGroupItShows()
    {
        var group = MakeGroup();
        Report(group, AttentionCategory.OwnHint);
        _windows.MainWindowShowingGroupId = group.Group.Id;

        _windows.Activate(_windows.MainWindow);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, group.UnreadAttentionCount);
    }

    [AvaloniaFact]
    public void DetachedWindowActivation_ResetsItsGroup_NotMainWindowGroups()
    {
        var detachedGroup = MakeGroup("Detached");
        var mainGroup = MakeGroup("Main");
        var detachedWindow = _windows.Detach(detachedGroup.Group.Id);
        Report(detachedGroup, AttentionCategory.OwnHint);
        Report(mainGroup, AttentionCategory.OwnHint);

        _windows.Activate(detachedWindow);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, detachedGroup.UnreadAttentionCount);
        Assert.Equal(1, mainGroup.UnreadAttentionCount);
    }

    [AvaloniaFact]
    public void UnknownBlinkModeString_FallsBackToCount()
    {
        Assert.Equal(AttentionBlinkMode.Count, new AppSettings { AttentionBlinkMode = "garbage" }.BlinkMode);
    }
}
