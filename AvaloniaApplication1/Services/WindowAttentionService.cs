using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Archipolygo.Services;

/// <inheritdoc cref="IWindowAttentionService"/>
public sealed class WindowAttentionService : IWindowAttentionService
{
    private readonly IGroupWindowLocator _locator;

    public WindowAttentionService(IGroupWindowLocator locator)
    {
        _locator = locator;
    }

    public void RequestAttention(Guid groupId)
    {
        var window = _locator.Resolve(groupId);
        if (window is null || window.IsActive)
        {
            return;
        }

        // Every platform call below is best-effort - a failed/unsupported
        // interop call here must never take the whole app down over a
        // cosmetic attention request. See each helper's own doc comment for
        // the platform-specific API this wraps (Feature-Plaene/Tab-Eigenes-Fenster.md,
        // Phase 2's API research).
        try
        {
            if (OperatingSystem.IsWindows())
            {
                WindowsWindowAttention.Flash(window);
            }
            else if (OperatingSystem.IsMacOS())
            {
                MacWindowAttention.RequestAttention();
            }
            else if (OperatingSystem.IsLinux())
            {
                LinuxWindowAttention.RequestAttention(window);
            }
        }
        catch
        {
            // Best-effort only - see this method's own doc comment.
        }
    }
}

/// <summary>
/// Windows: <c>FlashWindowEx</c> (user32.dll) - flashes the taskbar button
/// until the window receives focus (<c>FLASHW_TIMERNOFG</c>), matching this
/// feature's decided stop condition (see Feature-Plaene/Tab-Eigenes-Fenster.md).
/// </summary>
internal static class WindowsWindowAttention
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FlashwTray = 0x00000002;

    /// <summary>Flash until the window comes to the foreground - combines FLASHW_TIMER with FLASHW_CAPTION per the Win32 docs' own definition of this constant.</summary>
    private const uint FlashwTimerNoFg = 0x0000000C;

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    public static void Flash(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero)
        {
            return;
        }

        var info = new FLASHWINFO
        {
            hwnd = handle.Handle,
            dwFlags = FlashwTray | FlashwTimerNoFg,
            uCount = uint.MaxValue,
            dwTimeout = 0
        };
        info.cbSize = (uint)Marshal.SizeOf<FLASHWINFO>();

        FlashWindowEx(ref info);
    }
}

/// <summary>
/// macOS: no per-window title-bar flash concept exists at all - the closest
/// equivalent is bouncing the whole app's Dock icon via AppKit's
/// <c>NSApplication.requestUserAttention:</c>, called through the
/// Objective-C runtime directly (no AppKit binding is otherwise referenced
/// by this cross-platform Avalonia app - see Feature-Plaene/Tab-Eigenes-Fenster.md).
/// <c>NSCriticalRequest</c> (0) bounces continuously until the app is
/// activated, matching this feature's decided stop condition; the milder
/// <c>NSInformationalRequest</c> (10) would only bounce once.
/// </summary>
internal static class MacWindowAttention
{
    private const long NsCriticalRequest = 0;

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_get(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_long(IntPtr receiver, IntPtr selector, IntPtr arg);

    public static void RequestAttention()
    {
        var nsApplicationClass = objc_getClass("NSApplication");
        var sharedApplication = objc_msgSend_get(nsApplicationClass, sel_registerName("sharedApplication"));
        objc_msgSend_long(sharedApplication, sel_registerName("requestUserAttention:"), (IntPtr)NsCriticalRequest);
    }
}

/// <summary>
/// Linux/X11: appends the <c>_NET_WM_STATE_DEMANDS_ATTENTION</c> EWMH atom
/// directly onto the window's <c>_NET_WM_STATE</c> property via
/// <c>XChangeProperty</c> - not the fully spec-compliant ClientMessage/
/// <c>XSendEvent</c> path (which needs a hand-marshaled <c>XClientMessageEvent</c>
/// struct whose layout risks a native crash if ever gotten wrong), so this
/// is a deliberately simpler, lower-risk best-effort: only scalar
/// <see cref="IntPtr"/>/<c>long[]</c> P/Invoke signatures, no custom struct
/// marshaling. Many window managers already react to a direct property
/// change; whichever doesn't (or ignores this entirely under Wayland) was
/// already an accepted limitation, not something this feature promises to
/// fix - see Feature-Plaene/Tab-Eigenes-Fenster.md's own API research.
/// </summary>
internal static class LinuxWindowAttention
{
    private const int PropModeAppend = 1;
    private static readonly IntPtr XaAtom = (IntPtr)4;

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, long[] data, int nelements);

    [DllImport("libX11.so.6")]
    private static extern void XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    public static void RequestAttention(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero)
        {
            return;
        }

        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var stateAtom = XInternAtom(display, "_NET_WM_STATE", false);
            var attentionAtom = XInternAtom(display, "_NET_WM_STATE_DEMANDS_ATTENTION", false);
            if (stateAtom == IntPtr.Zero || attentionAtom == IntPtr.Zero)
            {
                return;
            }

            XChangeProperty(display, handle.Handle, stateAtom, XaAtom, 32, PropModeAppend, new[] { (long)attentionAtom }, 1);
            XFlush(display);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }
}
