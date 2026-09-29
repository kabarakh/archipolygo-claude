using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Archipolygo.Services;

/// <inheritdoc cref="IUnreadBadgeService"/>
public sealed class UnreadBadgeService : IUnreadBadgeService
{
    public void SetWindowBadge(Window window, int count)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Best-effort, same as WindowAttentionService: a failed interop call
        // must never take the app down over a cosmetic badge.
        try
        {
            WindowsTaskbarBadge.Set(window, count);
        }
        catch
        {
        }
    }

    public void SetAppBadge(int count)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            MacDockBadge.Set(count);
        }
        catch
        {
        }
    }

    /// <summary>What the badge shows - capped so it stays readable in the few pixels each platform gives it. Null = no badge.</summary>
    public static string? BadgeText(int count, int max) =>
        count <= 0 ? null : count > max ? $"{max}+" : count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Windows: <c>ITaskbarList3::SetOverlayIcon</c> (shell32 COM object
/// <c>CLSID_TaskbarList</c>), the same overlay Discord's red number uses.
/// Interface GUID and vtable order cross-checked against Avalonia 12.0.4's
/// own <c>Avalonia.Win32.Interop.TaskBarList</c>/<c>ITaskBarList3VTable</c>,
/// which uses this very call - with a null icon, from its <c>RefreshIcon</c>
/// on every DPI change and window-icon change, which wipes our overlay.
/// Callers re-apply on <see cref="TopLevel.ScalingChanged"/> for that reason
/// (Avalonia raises it right after that clear). The icon itself is drawn
/// with Avalonia and handed to <c>CreateIconFromResourceEx</c> as PNG bytes -
/// "Since Windows Vista RT_ICON ... may contain PNG-compressed image data"
/// (Microsoft Learn, Resource File Formats).
/// </summary>
internal static class WindowsTaskbarBadge
{
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        // ITaskbarList3 (only SetOverlayIcon is called; the rest just keep the vtable order)
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarListClass
    {
    }

    private const uint IconResourceVersion = 0x00030000;
    private const int SmCxSmIcon = 49;
    private const int BadgeMax = 9;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(byte[] presbits, uint dwResSize, [MarshalAs(UnmanagedType.Bool)] bool fIcon,
                                                          uint dwVer, int cxDesired, int cyDesired, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private static ITaskbarList3? _taskbar;

    /// <summary>One icon per badge text, created once and kept for the app's lifetime (at most ten: "1"-"9", "9+") - so replacing a window's overlay never has to destroy an icon another window might still show.</summary>
    private static readonly Dictionary<string, IntPtr> IconCache = new();

    public static void Set(Window window, int count)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero)
        {
            return;
        }

        if (_taskbar is null)
        {
            var taskbar = (ITaskbarList3)new TaskbarListClass();
            taskbar.HrInit();
            _taskbar = taskbar;
        }

        var text = UnreadBadgeService.BadgeText(count, BadgeMax);
        if (text is null)
        {
            _taskbar.SetOverlayIcon(handle.Handle, IntPtr.Zero, null);
            return;
        }

        if (!IconCache.TryGetValue(text, out var icon))
        {
            icon = CreateBadgeIcon(text);
            if (icon == IntPtr.Zero)
            {
                return;
            }

            IconCache[text] = icon;
        }

        _taskbar.SetOverlayIcon(handle.Handle, icon, $"{count} unread");
    }

    /// <summary>Red circle with a white number, sized to the system's small-icon metric (16px at 100% scaling).</summary>
    private static IntPtr CreateBadgeIcon(string text)
    {
        var size = Math.Max(16, GetSystemMetrics(SmCxSmIcon));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext(true))
        {
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE0, 0x3A, 0x2F)), null, new Rect(0, 0, size, size));
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), size * (text.Length > 1 ? 0.55 : 0.7), Brushes.White);
            context.DrawText(formatted, new Point((size - formatted.Width) / 2, (size - formatted.Height) / 2));
        }

        using var png = new MemoryStream();
        bitmap.Save(png);
        var bytes = png.ToArray();
        return CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, IconResourceVersion, size, size, 0);
    }
}

/// <summary>
/// macOS: <c>NSApplication.sharedApplication.dockTile.badgeLabel</c> (an
/// optional string - nil removes the badge, per Apple's NSDockTile docs),
/// through the Objective-C runtime like <see cref="MacWindowAttention"/>.
/// The Dock badge is per app, so it shows the total across all windows.
/// </summary>
internal static class MacDockBadge
{
    private const int BadgeMax = 99;

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_get(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

    private static int? _lastCount;

    public static void Set(int count)
    {
        // Every group count change lands here - skip redundant AppKit calls.
        if (_lastCount == count)
        {
            return;
        }

        var app = objc_msgSend_get(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        var dockTile = objc_msgSend_get(app, sel_registerName("dockTile"));
        if (dockTile == IntPtr.Zero)
        {
            return;
        }

        var text = UnreadBadgeService.BadgeText(count, BadgeMax);
        var label = IntPtr.Zero;
        if (text is not null)
        {
            var utf8 = Marshal.StringToCoTaskMemUTF8(text);
            try
            {
                // Autoreleased (we run inside AppKit's run loop, which drains it); the setter below keeps its own reference.
                label = objc_msgSend_ptr(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), utf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        objc_msgSend_ptr(dockTile, sel_registerName("setBadgeLabel:"), label);
        _lastCount = count;
    }
}
