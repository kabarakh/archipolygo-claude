using System;
using Avalonia;
using Avalonia.Controls;

namespace Archipolygo.Views;

/// <summary>
/// Keeps a <c>SizeToContent="Height"</c> dialog inside the working area of
/// its owner's screen. Without it, a tall dialog (Edit server, Settings) on a
/// short screen - e.g. a 2560x720 ultrawide, or any window-snapped half of
/// one - ran past the bottom of the screen with its Save button out of reach,
/// since <c>CanResize="False"</c> leaves no way to shrink it by hand. The
/// dialog's XAML still has to wrap its body in a <c>ScrollViewer</c> (with
/// the buttons docked outside it) for the capped height to scroll instead of
/// clip.
/// </summary>
internal static class DialogScreenFit
{
    /// <summary>
    /// Call before <c>ShowDialog</c>: caps the dialog's height up front so
    /// <c>CenterOwner</c> already positions the capped size, then nudges the
    /// window back on screen once it's open (and whenever it grows later,
    /// e.g. a validation error appearing) in case centering on the owner put
    /// part of it past a screen edge.
    /// </summary>
    public static void Apply(Window dialog, Window owner)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var working = screen.WorkingArea;

        // MaxHeight limits the client area only; the owner's own title bar is
        // the best estimate of how much the dialog's frame adds on top of it.
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
