using System.Threading.Tasks;
using Archipolygo.ViewModels;
using Avalonia;
using Avalonia.Input;

namespace Archipolygo.Views;

/// <summary>
/// Shared drag-and-drop plumbing for manual tab-reordering (see
/// Feature-Plaene/Tab-Reihenfolge.md) - both <see cref="MainWindow"/>'s tab
/// strip and <see cref="DashboardView"/>'s Overview list drag the same kind
/// of payload (a source <see cref="GroupViewModel"/>) onto another
/// <see cref="GroupViewModel"/>-templated row, so the format key and the
/// actual <see cref="DataTransfer"/>/<see cref="DragDrop.DoDragDropAsync"/>
/// mechanics live here once instead of being duplicated in both
/// code-behinds. Deliberately payload-only, not tied to "same TabControl"
/// or "same ListBox" as a target shape - see Tab-Reihenfolge.md's own note
/// on keeping this generic enough for the later "tab into its own window"
/// follow-up (Tab-Eigenes-Fenster.md) to build on without redoing this
/// part. Each caller still owns its own PointerPressed/PointerMoved
/// threshold-tracking fields and DragOver/Drop handlers, since "what
/// counts as a valid target" and "what else happens on press" (e.g.
/// DashboardView's row click-to-navigate, see its own code-behind) differ
/// per view.
/// </summary>
internal static class GroupReorderDragDrop
{
    /// <summary>
    /// In-process-only drag format carrying the dragged <see cref="GroupViewModel"/>
    /// itself, never serialized to a platform clipboard/drop target (see
    /// <see cref="DataFormat.CreateInProcessFormat{T}"/>'s own doc comment)
    /// - matches the plan's <c>"ArchipolygoGroupTab"</c> format-key idea.
    /// </summary>
    public static readonly DataFormat<GroupViewModel> Format =
        DataFormat.CreateInProcessFormat<GroupViewModel>("ArchipolygoGroupTab");

    /// <summary>
    /// Movement threshold (device-independent pixels) before a pointer-down
    /// turns into an actual drag instead of staying a plain click/tap -
    /// small enough to feel responsive, large enough that a normal click
    /// never accidentally starts one (Avalonia's own drag-and-drop how-to
    /// recommends a threshold in roughly this range for the same reason).
    /// On its own, this wasn't actually enough (see
    /// <see cref="MinPressDurationMs"/>'s own doc comment).
    /// </summary>
    private const double DragThresholdPixels = 4;

    /// <summary>
    /// Minimum time (milliseconds) the pointer must stay pressed before a
    /// move is allowed to start a drag at all, on top of the distance
    /// check below. Real-world pointer input (trackpads especially)
    /// reports a few pixels of incidental movement even during a
    /// perfectly ordinary click - by itself, <see cref="DragThresholdPixels"/>
    /// was enough to cross that on almost every click, starting an
    /// unwanted drag (dev feedback: clicking a tab kept "activating drop
    /// after a short delay"). Nobody moves the pointer this soon after
    /// pressing down for an actual click, but a deliberate drag easily
    /// clears it - a hold-duration gate filters out the false positives
    /// without needing a much larger (and less responsive) distance
    /// threshold instead.
    /// </summary>
    private const ulong MinPressDurationMs = 150;

    /// <summary>
    /// True once <paramref name="current"/> has moved far enough from
    /// <paramref name="pressedPosition"/> (both already resolved against the
    /// same reference element) <em>and</em> enough time has passed since
    /// <paramref name="pressedTimestamp"/> (both <see cref="PointerEventArgs.Timestamp"/>
    /// values) to count as a drag rather than a click - see
    /// <see cref="MinPressDurationMs"/>'s own doc comment for why both are
    /// checked, not just distance.
    /// </summary>
    public static bool ShouldStartDrag(Point pressedPosition, ulong pressedTimestamp, Point current, ulong currentTimestamp)
    {
        if (currentTimestamp - pressedTimestamp < MinPressDurationMs)
        {
            return false;
        }

        var delta = current - pressedPosition;
        return System.Math.Abs(delta.X) >= DragThresholdPixels || System.Math.Abs(delta.Y) >= DragThresholdPixels;
    }

    /// <summary>
    /// Wraps <paramref name="source"/> into a fresh in-process
    /// <see cref="DataTransfer"/> and starts the actual Avalonia drag
    /// operation. <paramref name="pressedArgs"/> must be the very
    /// <see cref="PointerPressedEventArgs"/> instance from the gesture's own
    /// PointerPressed - Avalonia 12's <c>DoDragDropAsync</c> requires that
    /// exact type (not the base <see cref="PointerEventArgs"/> a later
    /// PointerMoved carries), confirmed against the actual 12.0.4 source
    /// (not the newer main-branch docs the plan flagged as unverified) -
    /// see this method's caller for how the value is kept typed correctly
    /// across the press-then-move gap.
    /// </summary>
    public static Task StartDragAsync(PointerPressedEventArgs pressedArgs, GroupViewModel source)
    {
        // The single DataTransferItem also carries a plain-text
        // representation of the same group (its header text) - macOS's
        // native drag bridge crashes ("There are 0 items on the pasteboard,
        // but 1 drag images. There must be 1 draggingItem per
        // pasteboardItem.", a stock AppKit NSDraggingSession assertion)
        // when a DataTransfer's only item has nothing platform-writable in
        // it, since Format alone (an in-process format, see its own doc
        // comment) never touches the real pasteboard at all - it still
        // counts as "1 item" toward the drag-image count, but contributes 0
        // pasteboard items to satisfy it. Avalonia's own ControlCatalog
        // drag-and-drop sample never starts a drag with only a
        // CreateInProcessFormat item either, for the same reason. The Text
        // value itself is never read back on Drop - Format/TryGetSource
        // below still carries the actual live GroupViewModel reference.
        var item = DataTransferItem.Create(Format, source);
        item.Set(DataFormat.Text, source.HeaderText);

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(item);
        return DragDrop.DoDragDropAsync(pressedArgs, dataTransfer, DragDropEffects.Move);
    }

    /// <summary>Reads the dragged source <see cref="GroupViewModel"/> back out on DragOver/Drop, or null if this drag isn't one of our own group-tab drags.</summary>
    public static GroupViewModel? TryGetSource(IDataTransfer dataTransfer) => dataTransfer.TryGetValue(Format);
}
