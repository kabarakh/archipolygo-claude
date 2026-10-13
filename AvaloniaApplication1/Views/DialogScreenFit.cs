using System;
using Avalonia;
using Avalonia.Controls;

namespace Archipolygo.Views;

/// <summary>
/// Keeps a <c>SizeToContent="Height"</c>, <c>CanResize="False"</c> dialog on its owner's screen: on a short screen
/// (e.g. a 2560x720 ultrawide snapped to half) it otherwise ran off the bottom with Save unreachable.
/// The dialog's body must sit in a <c>ScrollViewer</c> (buttons outside it) so the capped height scrolls, not clips.
/// </summary>
internal static class DialogScreenFit
{
    /// <summary>
    /// Call before <c>ShowDialog</c> so <c>CenterOwner</c> positions the capped size; the dialog is also moved back
    /// on screen when opened and whenever it grows, since centering on the owner can push it past an edge.
    /// </summary>
    public static void Apply(Window dialog, Window owner)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var working = screen.WorkingArea;

        // MaxHeight limits the client area only; the owner's title bar estimates the dialog's frame overhead.
        var decorationHeight = owner.FrameSize is { } ownerFrame
            ? Math.Max(0, ownerFrame.Height - owner.ClientSize.Height)
            : 0;
        dialog.MaxHeight = Math.Max(200, working.Height / screen.Scaling - decorationHeight);

        var isOpen = false;
        dialog.Opened += (_, _) =>
        {
            isOpen = true;
            ClampIntoWorkingArea(dialog, working, screen.Scaling);
        };
        dialog.SizeChanged += (_, _) =>
        {
            if (isOpen)
            {
                ClampIntoWorkingArea(dialog, working, screen.Scaling);
            }
        };
    }

    private static void ClampIntoWorkingArea(Window dialog, PixelRect working, double scaling)
    {
        var frame = dialog.FrameSize ?? dialog.ClientSize;
        var width = (int)Math.Ceiling(frame.Width * scaling);
        var height = (int)Math.Ceiling(frame.Height * scaling);

        var position = dialog.Position;
        var x = Math.Max(working.X, Math.Min(position.X, working.Right - width));
        var y = Math.Max(working.Y, Math.Min(position.Y, working.Bottom - height));

        if (x != position.X || y != position.Y)
        {
            dialog.Position = new PixelPoint(x, y);
        }
    }
}
