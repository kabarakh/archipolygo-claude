using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private async void OnAddServerClick(object? sender, RoutedEventArgs e)
    {
        var defaultAutoConnect = ViewModel.LoadSettings().DefaultAutoConnect;
        var editorViewModel = ConnectionEditorViewModel.ForNewGroup(defaultAutoConnect, ViewModel.GetAllGroups(), ViewModel.ResolveTrackerIdAsync);
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            ViewModel.AddNewGroup(result.Name, result.Host, result.Port, result.Password, result.SlotName, result.AutoConnect, result.TrackerReferenceInput, result.TrackerId);
        }
    }

    private async void OnAddSlotClick(object? sender, RoutedEventArgs e)
    {
        var selectedGroup = ViewModel.SelectedGroup;
        if (selectedGroup is null)
        {
            return;
        }

        // May briefly connect/disconnect under the hood if this server has
        // no live session right now - see MainWindowViewModel.GetAvailableSlotsToAddAsync.
        var availablePlayers = await ViewModel.GetAvailableSlotsToAddAsync(selectedGroup);

        var editorViewModel = ConnectionEditorViewModel.ForAddSlot(selectedGroup.Group, availablePlayers);
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            ViewModel.AddSlotsToGroup(selectedGroup, result.SlotsToAdd);
        }
    }

    private async void OnEditServerClick(object? sender, RoutedEventArgs e)
    {
        var selectedGroup = ViewModel.SelectedGroup;
        if (selectedGroup is null)
        {
            return;
        }

        var editorViewModel = ConnectionEditorViewModel.ForEditGroup(
            selectedGroup.Group,
            ViewModel.GetAllGroups(),
            ViewModel.ResolveTrackerIdAsync);
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            await ViewModel.UpdateGroup(selectedGroup, result.Name, result.Host, result.Port, result.Password, result.AutoConnect, result.PreferredLeaderSlotId, result.SlotsToRemove, result.TrackerReferenceInput, result.TrackerId);
        }
    }

    /// <summary>
    /// Feature-Plaene/Archiv/Auto-Update.md's update-available dot - a plain
    /// <c>Border</c>, not a <c>Button</c> (no <c>Click</c> event of its own),
    /// so this opens its attached Flyout directly off the lower-level
    /// pointer event instead.
    /// </summary>
    private void OnUpdateBadgeClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control)
        {
            FlyoutBase.ShowAttachedFlyout(control);
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settingsViewModel = SettingsViewModel.FromSettings(ViewModel.LoadSettings(), ViewModel.CheckForUpdatesAsync, ViewModel.ShowUnmanagedInstallHint);
        var settings = await SettingsWindow.ShowDialogAsync(this, settingsViewModel);
        if (settings is not null)
        {
            ViewModel.SaveSettings(settings);
        }
    }

    /// <summary>
    /// Copies every selected event's text (without the timestamp), one per
    /// line, to the clipboard on Ctrl+C (Windows/Linux) or Cmd+C (macOS,
    /// where the physical key reports as <see cref="KeyModifiers.Meta"/>).
    /// The Events ListBox uses <c>SelectionMode="Multiple"</c> so several
    /// lines can be selected (ctrl/shift-click) before copying.
    /// </summary>
    private async void OnEventsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsCopyShortcut(e) || sender is not ListBox listBox || !HasSelection(listBox))
        {
            return;
        }

        await CopySelectedLinesAsync<EventEntry>(listBox, entry => entry.Text);
        e.Handled = true;
    }

    /// <summary>
    /// Same mechanism as <see cref="OnEventsListKeyDown"/>, for the Hints
    /// list instead - its own selection and clipboard content, unrelated to
    /// the Events list's. Each copied line reproduces what's shown for that
    /// hint (item, finder/receiver, location) as a single line of text.
    /// </summary>
    private async void OnHintsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsCopyShortcut(e) || sender is not ListBox listBox || !HasSelection(listBox))
        {
            return;
        }

        await CopySelectedLinesAsync<HintEntry>(listBox,
            hint => $"{hint.ItemName}: {hint.FindingPlayerName} -> {hint.ReceivingPlayerName} : {hint.LocationName}");
        e.Handled = true;
    }

    private static bool IsCopyShortcut(KeyEventArgs e) =>
        e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));

    private static bool HasSelection(ListBox listBox) => listBox.SelectedItems is { Count: > 0 };

    /// <summary>
    /// Builds one line of text per selected item (via <paramref name="toText"/>)
    /// and copies them all, newline-separated, to the clipboard - in the
    /// order the items appear in the list, not selection order, so a
    /// ctrl-clicked-out-of-order selection still copies chronologically.
    /// </summary>
    private async Task CopySelectedLinesAsync<T>(ListBox listBox, Func<T, string> toText) where T : class
    {
        var selected = new HashSet<object>(listBox.SelectedItems!.Cast<object>());
        var displayOrder = (listBox.ItemsSource as IEnumerable)?.Cast<object>() ?? Enumerable.Empty<object>();
        var text = string.Join(Environment.NewLine,
            displayOrder.OfType<T>().Where(item => selected.Contains(item)).Select(toText));

        if (text.Length == 0)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>
    /// Up/Down-arrow recall for the message TextBox, mirroring a terminal's
    /// command history - Up steps back through <see cref="GroupViewModel"/>'s
    /// recently sent messages, Down steps forward again (and eventually
    /// restores whatever was being typed before the first Up-press). Mainly
    /// useful for resending the same !hint text for an item several times in
    /// a row without retyping it. A single-line TextBox has no native use for
    /// Up/Down, so intercepting them here doesn't take anything away.
    /// </summary>
    private void OnMessageTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: GroupViewModel group } textBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                group.RecallPreviousMessage();
                break;
            case Key.Down:
                group.RecallNextMessage();
                break;
            default:
                return;
        }

        textBox.CaretIndex = textBox.Text?.Length ?? 0;
        e.Handled = true;
    }

    /// <summary>
    /// Forces the "Chat as:" ComboBox to display the view model's actual
    /// current <see cref="GroupViewModel.SelectedChatSlot"/> once this
    /// particular ComboBox instance has finished loading.
    ///
    /// A tab that was never the active one when the app started has this
    /// whole content template - this ComboBox included - materialized for
    /// the very first time only when the user actually clicks that tab,
    /// long after <see cref="GroupViewModel.SelectedChatSlot"/> was already
    /// set correctly (the leader connected back at startup). A brand-new
    /// ComboBox reconciling its initial SelectedItem against its ItemsSource
    /// can come up with no visible selection despite the bound value being
    /// perfectly fine, the same kind of rebinding artifact already handled
    /// at the view-model level in <c>GroupViewModel.OnSelectedChatSlotChanged</c>
    /// - this just also re-asserts it on the view once, so the dropdown
    /// itself shows the right thing without the user having to reselect it
    /// by hand.
    /// </summary>
    private void OnChatSlotComboBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox { DataContext: GroupViewModel group } comboBox)
        {
            comboBox.SelectedItem = group.SelectedChatSlot;
        }
    }

    /// <summary>
    /// Keeps the events list scrolled to the bottom as new events arrive, so
    /// the most recent message/event is always visible without the user
    /// having to scroll manually - and wires up the floating "Jump to
    /// newest" button (see <see cref="Views.MainWindow"/>'s XAML) as the way
    /// back once the user has deliberately scrolled away (see
    /// <see cref="OnEventsScrollViewerScrollChanged"/> for how "should we be
    /// following the bottom right now" is tracked and enforced).
    ///
    /// Wires a genuine user gesture - a mouse-wheel scroll over the list -
    /// to drop out of auto-follow, since that is the one unambiguous signal
    /// that the user (not Avalonia's own scroll-anchoring, see below) wants
    /// to look at something else. Tunnel routing sees it before the
    /// ScrollViewer consumes it.
    /// </summary>
    private void OnEventsListLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        // The button lives as the ListBox's sibling in the wrapping overlay
        // Grid (see MainWindow.axaml) - found by name rather than by type
        // alone since each tab gets its own instance of this whole subtree
        // (a per-tab DataTemplate), so there's no single compile-time-named
        // field to reference directly the way a top-level control would have.
        var jumpButton = (listBox.Parent as Panel)?.Children
            .OfType<Button>()
            .FirstOrDefault(b => b.Name == "JumpToBottomButton");

        ScrollViewer? attachedScrollViewer = null;
        EventHandler<ScrollChangedEventArgs>? scrollChangedHandler = null;

        // "Should the next layout settle back at the bottom" - see
        // OnEventsScrollViewerScrollChanged's doc comment for why this has
        // to be a persistent intent rather than something re-derived from
        // scroll geometry on every call.
        var stickToBottom = true;

        void Attach()
        {
            if (attachedScrollViewer is not null)
            {
                return;
            }

            attachedScrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (attachedScrollViewer is null)
            {
                return;
            }

            scrollChangedHandler = (s, args) => OnEventsScrollViewerScrollChanged(s, args, jumpButton, listBox, ref stickToBottom);
            attachedScrollViewer.ScrollChanged += scrollChangedHandler;
            attachedScrollViewer.ScrollToEnd();

            attachedScrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) =>
            {
                stickToBottom = false;
            }, RoutingStrategies.Tunnel);
        }

        // The control template (and with it, the inner ScrollViewer) might
        // not be applied yet at this exact point; TemplateApplied covers
        // that case, Attach() itself covers the common case where it's
        // already available.
        Attach();
        listBox.TemplateApplied += (_, _) => Attach();

        if (jumpButton is not null)
        {
            jumpButton.Click += (_, _) =>
            {
                listBox.SelectedIndex = -1;
                stickToBottom = true;
                attachedScrollViewer?.ScrollToEnd();
            };
        }

        listBox.Unloaded += (_, _) =>
        {
            if (attachedScrollViewer is not null && scrollChangedHandler is not null)
            {
                attachedScrollViewer.ScrollChanged -= scrollChangedHandler;
            }
        };
    }

    /// <summary>
    /// Enforces <paramref name="stickToBottom"/>: while true, every single
    /// <see cref="ScrollViewer.ScrollChanged"/> - not just ones caused by new
    /// content - re-clears any selection and re-scrolls to the end, so
    /// <paramref name="jumpButton"/> stays hidden; once false (the user
    /// scrolled away, see <see cref="OnEventsListLoaded"/>), it instead just
    /// tracks distance from the bottom to show/hide the button, and never
    /// scrolls on its own.
    ///
    /// Re-asserting on *every* call, not once per content change, is load-
    /// bearing, not redundant - built and shipped as "just clear the
    /// selection and ScrollToEnd() once" first, then disproven with a
    /// standalone repro harness (ScrollRepro, see scratchpad) that isolated
    /// the exact same ListBox/button/scroll code from the live app: logging
    /// <see cref="ScrollViewer.CurrentAnchor"/> on every call showed
    /// Avalonia's *own* scroll-anchoring - unrelated to selection or focus,
    /// confirmed by clearing both and seeing no difference - re-picking a
    /// new anchor candidate and nudging <see cref="ScrollViewer.Offset"/>
    /// away from the end on *several separate, later* ScrollChanged calls
    /// after <see cref="MessageHistoryService"/>'s per-tab cap starts
    /// RemoveAt(0)-ing items above the viewport (the anchor walked backward
    /// one realized row at a time - e.g. event #14 -> #13 -> #12 -> #11 -
    /// each one pulling Offset down by roughly a row height, all with
    /// ExtentDelta=0 so nothing about our own "did content change" check
    /// would ever see them coming). A single ScrollToEnd() only ever won the
    /// *first* round of that fight; the later rounds arrived with nothing
    /// left in our code to notice or correct them, leaving the view - and
    /// the button - stuck short of the end. The anchor walk is empirically
    /// bounded (a handful of steps, not unbounded), so simply re-asserting
    /// every time instead of once is enough to always win it: our correction
    /// fires at least as often as anchoring's own adjustment, so it can
    /// never end up more than one step behind.
    /// </summary>
    private static void OnEventsScrollViewerScrollChanged(
        object? sender, ScrollChangedEventArgs e, Button? jumpButton, ListBox listBox, ref bool stickToBottom)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (stickToBottom)
        {
            if (listBox.SelectedIndex != -1)
            {
                listBox.SelectedIndex = -1;
            }
            scrollViewer.ScrollToEnd();

            if (jumpButton is not null)
            {
                jumpButton.IsVisible = false;
            }

            return;
        }

        if (jumpButton is not null)
        {
            const double AtBottomTolerance = 2.0;
            var distanceFromBottom = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
            jumpButton.IsVisible = distanceFromBottom > AtBottomTolerance;
        }
    }
}
