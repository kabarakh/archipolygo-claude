using System;
using System.Collections.ObjectModel;

namespace Archipolygo.ViewModels;

/// <summary>Tab order (drag-reordering) and detaching a server into its own window.</summary>
public partial class MainWindowViewModel
{
    /// <summary>Set by the view to open a detached window. Null = no window appears.</summary>
    public Action<GroupViewModel>? OpenDetachedWindow { get; set; }

    /// <summary>Set by the view to close a group's detached window (on redock or removal).</summary>
    public Action<GroupViewModel>? CloseDetachedWindow { get; set; }

    /// <summary>
    /// Moves <paramref name="source"/> before/after <paramref name="target"/>. The order of
    /// <see cref="Groups"/> is what gets persisted - there's no separate order index.
    /// </summary>
    public void ReorderGroup(GroupViewModel source, GroupViewModel target, bool insertAfter)
    {
        if (ReferenceEquals(source, target))
        {
            return;
        }

        // Mirror onto DockedGroups; a no-op there if either side is detached.
        MoveWithinCollection(Groups, source, target, insertAfter);
        MoveWithinCollection(DockedGroups, source, target, insertAfter);
        PersistGroups();
    }

    /// <remarks>
    /// <see cref="ObservableCollection{T}.Move"/>'s <c>newIndex</c> is post-removal, so the
    /// desired index shifts down by one when <paramref name="source"/> sat before it
    /// (see <c>GroupReorderTests</c>).
    /// </remarks>
    private static void MoveWithinCollection(ObservableCollection<GroupViewModel> collection, GroupViewModel source, GroupViewModel target, bool insertAfter)
    {
        var oldIndex = collection.IndexOf(source);
        var targetIndex = collection.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0)
        {
            return;
        }

        var desiredIndex = insertAfter ? targetIndex + 1 : targetIndex;
        var newIndex = oldIndex < desiredIndex ? desiredIndex - 1 : desiredIndex;

        collection.Move(oldIndex, newIndex);
    }

    /// <summary>
    /// Moves a group into its own window. It stays in <see cref="Groups"/> (persisted, on the
    /// Dashboard); only <see cref="DockedGroups"/> loses it.
    /// </summary>
    public void DetachGroup(GroupViewModel group)
    {
        if (group.IsDetached || !DockedGroups.Remove(group))
        {
            return;
        }

        group.IsDetached = true;

        if (SelectedGroup == group)
        {
            var fallbackIndex = Math.Min(DockedGroups.Count - 1, Groups.IndexOf(group));
            SelectedGroup = DockedGroups.Count > 0 ? DockedGroups[Math.Max(fallbackIndex, 0)] : null;
        }

        // No tab left to show: switch to the Dashboard (RedockGroup switches back).
        if (DockedGroups.Count == 0)
        {
            IsDashboardVisible = true;
        }

        OpenDetachedWindow?.Invoke(group);
        _attentionTracker?.RefreshSeenState();
    }

    /// <summary>
    /// Appended at the end of <see cref="DockedGroups"/> (original position isn't tracked) and
    /// selected so the user sees where it landed.
    /// </summary>
    public void RedockGroup(GroupViewModel group)
    {
        if (!group.IsDetached)
        {
            return;
        }

        var wasShowingNoTabs = DockedGroups.Count == 0;

        group.IsDetached = false;
        DockedGroups.Add(group);
        SelectedGroup = group;

        // Only undo DetachGroup's auto-switch; never yank a user off a Dashboard
        // they chose to view.
        if (wasShowingNoTabs)
        {
            IsDashboardVisible = false;
        }

        CloseDetachedWindow?.Invoke(group);
        _attentionTracker?.RefreshSeenState();
    }
}
