using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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
        var editorViewModel = ConnectionEditorViewModel.ForNewGroup(defaultAutoConnect, ViewModel.GetAllGroups());
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            ViewModel.AddNewGroup(result.Name, result.Host, result.Port, result.Password, result.SlotName, result.AutoConnect);
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
            slot => ViewModel.RemoveSlotFromGroup(selectedGroup, slot),
            ViewModel.GetAllGroups());
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            ViewModel.UpdateGroup(selectedGroup, result.Name, result.Host, result.Port, result.Password, result.AutoConnect, result.PreferredLeaderSlotId);
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settingsViewModel = SettingsViewModel.FromSettings(ViewModel.LoadSettings());
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
    /// having to scroll manually.
    ///
    /// This reacts to the <see cref="ScrollViewer.ScrollChanged"/> event
    /// rather than the events collection or a fixed delay: while a tab is
    /// not the selected one, Avalonia skips layout for its (hidden) content,
    /// so its <see cref="ScrollViewer.Extent"/> does not grow as new events
    /// arrive - only <see cref="ScrollViewer.Offset"/> would need to follow
    /// it, and there is nothing to follow yet. The moment the tab becomes
    /// visible again, a layout pass finally catches the content up to its
    /// real size, which is exactly when <see cref="ScrollViewer.ExtentDelta"/>
    /// becomes positive - so reacting to that, instead of guessing how long
    /// any particular layout pass takes, is what actually fixes "wasn't
    /// scrolled to the end after switching back", regardless of how much
    /// content arrived or how long the log is.
    /// </summary>
    private void OnEventsListLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        ScrollViewer? attachedScrollViewer = null;

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

            attachedScrollViewer.ScrollChanged += OnEventsScrollViewerScrollChanged;
            attachedScrollViewer.ScrollToEnd();
        }

        // The control template (and with it, the inner ScrollViewer) might
        // not be applied yet at this exact point; TemplateApplied covers
        // that case, Attach() itself covers the common case where it's
        // already available.
        Attach();
        listBox.TemplateApplied += (_, _) => Attach();

        listBox.Unloaded += (_, _) =>
        {
            if (attachedScrollViewer is not null)
            {
                attachedScrollViewer.ScrollChanged -= OnEventsScrollViewerScrollChanged;
            }
        };
    }

    private static void OnEventsScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer && e.ExtentDelta.Y > 0)
        {
            scrollViewer.ScrollToEnd();
        }
    }
}
