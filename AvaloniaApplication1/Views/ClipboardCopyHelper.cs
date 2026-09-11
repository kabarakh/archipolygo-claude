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

/// <summary>
/// Shared "Ctrl/Cmd+C copies the selected rows' text" mechanism - every
/// multi-select list in this app that supports it (MainWindow's own
/// Events/Hints lists, DashboardView's shared Events list) used to
/// hand-roll an identical copy of the actual selection-to-clipboard logic;
/// this is that logic, written once. Only "what counts as the copy
/// shortcut" and "what text to build per row" are still each caller's own
/// concern (see e.g. <see cref="MainWindow.OnEventsListKeyDown"/> vs.
/// <see cref="DashboardView.OnDashboardEventsListKeyDown"/>).
/// </summary>
public static class ClipboardCopyHelper
{
    public static bool IsCopyShortcut(KeyEventArgs e) =>
        e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));

    /// <summary>
    /// Builds one line of text per selected item (via <paramref name="toText"/>)
    /// and copies them all, newline-separated, to the clipboard - in the
    /// order the items appear in the list, not selection order, so a
    /// ctrl-clicked-out-of-order selection still copies chronologically.
    /// No-op (returns <c>false</c>) if nothing is selected or the resulting
    /// text would be empty.
    /// </summary>
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
}
