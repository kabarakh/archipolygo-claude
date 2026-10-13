using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Archipolygo.Models;
using Avalonia.Controls;

namespace Archipolygo.Services;

/// <inheritdoc cref="IWindowAttentionService"/>
public sealed class WindowAttentionService : IWindowAttentionService
{
    private readonly IGroupWindowLocator _locator;

    /// <summary>Hooked lazily on first resolve, since detached windows come and go.</summary>
    private readonly HashSet<Window> _hookedWindows = new(ReferenceEqualityComparer.Instance);

    public WindowAttentionService(IGroupWindowLocator locator)
    {
        _locator = locator;
    }

    public event Action<object>? WindowAcknowledged;

    public object? ResolveWindow(Guid groupId)
    {
        var window = _locator.Resolve(groupId);
        if (window is not null && _hookedWindows.Add(window))
        {
            window.Activated += OnWindowActivated;
            window.Closed += OnWindowClosed;
        }

        return window;
    }

    public bool IsActive(object window) => window is Window { IsActive: true };

    public bool IsGroupSeen(Guid groupId) =>
        ResolveWindow(groupId) is Window { IsActive: true } window &&
        (window is not IGroupHostWindow host || host.IsShowingGroup(groupId));

    public void RequestAttention(object window, AttentionBlinkMode mode, int blinkCount)
    {
        if (mode == AttentionBlinkMode.Off || window is not Window avaloniaWindow)
        {
            return;
        }

        // Best-effort: a failed interop call must never take the app down over a cosmetic request.
        try
        {
            var untilFocus = mode == AttentionBlinkMode.UntilFocus;
            if (OperatingSystem.IsWindows())
            {
                WindowsWindowAttention.Flash(avaloniaWindow, untilFocus, (uint)Math.Max(1, blinkCount));
            }
            else if (OperatingSystem.IsMacOS())
            {
                MacWindowAttention.RequestAttention(untilFocus);
            }
            else if (OperatingSystem.IsLinux())
            {
                // No duration knob: the window manager decides how to show the hint.
                LinuxWindowAttention.RequestAttention(avaloniaWindow);
            }
        }
        catch
        {
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            WindowAcknowledged?.Invoke(window);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Activated -= OnWindowActivated;
        window.Closed -= OnWindowClosed;
        _hookedWindows.Remove(window);
        WindowAcknowledged?.Invoke(window);
    }
}

/// <summary>
/// Windows: <c>FlashWindowEx</c>. After a counted flash Windows keeps the taskbar button highlighted
/// until activation; <c>FLASHW_TIMERNOFG</c> flashes for as long as the window stays unfocused.
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

    /// <summary>FLASHW_CAPTION | FLASHW_TRAY.</summary>
    private const uint FlashwAll = 0x00000003;

    /// <summary>FLASHW_TIMERNOFG - until the window comes to the foreground.</summary>
    private const uint FlashwTimerNoFg = 0x0000000C;

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    public static void Flash(Window window, bool untilFocus, uint count)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero)
        {
            return;
        }

        var info = new FLASHWINFO
        {
            hwnd = handle.Handle,
            dwFlags = untilFocus ? FlashwAll | FlashwTimerNoFg : FlashwAll,
            uCount = untilFocus ? 0 : count,
            dwTimeout = 0
        };
        info.cbSize = (uint)Marshal.SizeOf<FLASHWINFO>();

        FlashWindowEx(ref info);
    }
}

/// <summary>
/// macOS: no per-window flash, so bounce the Dock icon via the ObjC runtime. Informational bounces
/// once (no "N times" option); Critical bounces until activation (<see cref="Models.AttentionBlinkMode.UntilFocus"/>).
/// </summary>
internal static class MacWindowAttention
{
    private const long NsCriticalRequest = 0;
    private const long NsInformationalRequest = 10;

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_get(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_long(IntPtr receiver, IntPtr selector, IntPtr arg);

    public static void RequestAttention(bool untilFocus)
    {
        var nsApplicationClass = objc_getClass("NSApplication");
        var sharedApplication = objc_msgSend_get(nsApplicationClass, sel_registerName("sharedApplication"));
        objc_msgSend_long(sharedApplication, sel_registerName("requestUserAttention:"), (IntPtr)(untilFocus ? NsCriticalRequest : NsInformationalRequest));
    }
}

/// <summary>
/// Linux/X11: appends <c>_NET_WM_STATE_DEMANDS_ATTENTION</c> via <c>XChangeProperty</c> instead of the
/// ClientMessage path, avoiding hand-marshaled structs; many WMs react to it, Wayland may not.
/// Reads the property first so repeated requests don't append the atom again.
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
    private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr longOffset, IntPtr longLength, bool delete, IntPtr reqType,
                                                 out IntPtr actualType, out int actualFormat, out IntPtr itemCount, out IntPtr bytesAfter, out IntPtr data);

    [DllImport("libX11.so.6")]
    private static extern int XFree(IntPtr data);

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

            if (HasAtom(display, handle.Handle, stateAtom, attentionAtom))
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

    /// <summary>Format-32 property data is an array of C <c>long</c>s, i.e. pointer-sized on Linux.</summary>
    private static bool HasAtom(IntPtr display, IntPtr window, IntPtr property, IntPtr atom)
    {
        const int success = 0;
        if (XGetWindowProperty(display, window, property, IntPtr.Zero, (IntPtr)1024, false, XaAtom,
                               out _, out var format, out var itemCount, out _, out var data) != success)
        {
            return false;
        }

        try
        {
            if (data == IntPtr.Zero || format != 32)
            {
                return false;
            }

            for (var i = 0; i < (long)itemCount; i++)
            {
                if (Marshal.ReadIntPtr(data, i * IntPtr.Size) == atom)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (data != IntPtr.Zero)
            {
                XFree(data);
            }
        }
    }
}
