using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Archipolygo.Views;

/// <summary>Shared Ctrl/Cmd+C selection-to-clipboard logic for the app's multi-select lists.</summary>
public static class ClipboardCopyHelper
{
    /// <param name="anchor">Pass the hosting view, not a <see cref="MenuItem"/>, whose TopLevel is the popup rather than the window.</param>
    public static async Task<bool> CopyTextAsync(Visual anchor, string text)
    {
        var clipboard = TopLevel.GetTopLevel(anchor)?.Clipboard;
        if (clipboard is null)
        {
            return false;
        }

        await clipboard.SetTextAsync(text);
        return true;
    }

    public static bool IsCopyShortcut(KeyEventArgs e) =>
        e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));

    /// <summary>Copies selected rows in display order, not selection order, so out-of-order picks stay chronological.</summary>
    /// <param name="anchor">Any control in the same visual tree as <paramref name="listBox"/> - used only to resolve the hosting <see cref="TopLevel"/>'s clipboard.</param>
    public static async Task<bool> CopySelectedLinesAsync<T>(Visual anchor, ListBox listBox, Func<T, string> toText) where T : class
    {
        if (listBox.SelectedItems is not { Count: > 0 } selectedItems)
        {
            return false;
        }

        var selected = new HashSet<object>(selectedItems.Cast<object>());
        var displayOrder = (listBox.ItemsSource as IEnumerable)?.Cast<object>() ?? Enumerable.Empty<object>();
        var text = string.Join(Environment.NewLine,
            displayOrder.OfType<T>().Where(item => selected.Contains(item)).Select(toText));

        if (text.Length == 0)
        {
            return false;
        }

        var clipboard = TopLevel.GetTopLevel(anchor)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }

        return true;
    }

    /// <summary>Copies the topmost selected row (in display order) plus every row after it, selected or not.</summary>
    /// <param name="anchor">Any control in the same visual tree as <paramref name="listBox"/> - used only to resolve the hosting <see cref="TopLevel"/>'s clipboard.</param>
    public static async Task<bool> CopyFromSelectedOnwardsAsync<T>(Visual anchor, ListBox listBox, Func<T, string> toText) where T : class
    {
        if (listBox.SelectedItems is not { Count: > 0 } selectedItems)
        {
            return false;
        }

        var selected = new HashSet<object>(selectedItems.Cast<object>());
        var displayOrder = ((listBox.ItemsSource as IEnumerable)?.Cast<object>() ?? Enumerable.Empty<object>()).ToList();
        var anchorIndex = displayOrder.FindIndex(selected.Contains);
        if (anchorIndex < 0)
        {
            return false;
        }

        var text = string.Join(Environment.NewLine, displayOrder.Skip(anchorIndex).OfType<T>().Select(toText));
        if (text.Length == 0)
        {
            return false;
        }

        var clipboard = TopLevel.GetTopLevel(anchor)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }

        return true;
    }
}
