using System.Threading.Tasks;
using Archipolygo.ViewModels;
using Avalonia;
using Avalonia.Input;

namespace Archipolygo.Views;

/// <summary>
/// Shared drag-and-drop plumbing for tab reordering (MainWindow tab strip and Dashboard Overview).
/// Payload-only; each caller owns its own threshold tracking and DragOver/Drop target logic.
/// </summary>
internal static class GroupReorderDragDrop
{
    /// <summary>In-process-only format carrying the live <see cref="GroupViewModel"/>; never hits the platform pasteboard.</summary>
    public static readonly DataFormat<GroupViewModel> Format =
        DataFormat.CreateInProcessFormat<GroupViewModel>("ArchipolygoGroupTab");

    private const double DragThresholdPixels = 4;

    /// <summary>
    /// Hold-time gate on top of the distance threshold: trackpads report a few pixels of movement during
    /// ordinary clicks, which alone kept starting unwanted drags.
    /// </summary>
    private const ulong MinPressDurationMs = 150;

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
    /// <paramref name="pressedArgs"/> must be the original <see cref="PointerPressedEventArgs"/>: Avalonia 12's
    /// <c>DoDragDropAsync</c> requires that exact type, not a later PointerMoved's args.
    /// </summary>
    public static Task StartDragAsync(PointerPressedEventArgs pressedArgs, GroupViewModel source)
    {
        // The extra Text representation is required: macOS's drag bridge crashes (NSDraggingSession assertion)
        // when the only item is in-process, since it then contributes zero pasteboard items. Never read on Drop.
        var item = DataTransferItem.Create(Format, source);
        item.Set(DataFormat.Text, source.HeaderText);

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(item);
        return DragDrop.DoDragDropAsync(pressedArgs, dataTransfer, DragDropEffects.Move);
    }

    /// <summary>Null if this drag isn't one of our own group-tab drags.</summary>
    public static GroupViewModel? TryGetSource(IDataTransfer dataTransfer) => dataTransfer.TryGetValue(Format);
}
