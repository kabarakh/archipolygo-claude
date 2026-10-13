using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

/// <summary>Hosted by MainWindow or a DetachedGroupWindow, so owner windows are resolved per call via TopLevel.</summary>
public partial class GroupDetailView : UserControl
{
    /// <summary>Hides <c>HostPortCopiedText</c> again shortly after a copy; restarted on every copy so repeated clicks keep it visible.</summary>
    private readonly DispatcherTimer _hostPortCopiedTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    public GroupDetailView()
    {
        InitializeComponent();
        _hostPortCopiedTimer.Tick += (_, _) =>
        {
            _hostPortCopiedTimer.Stop();
            HostPortCopiedText.IsVisible = false;
        };
    }

    // Copies on release, so a drag-select ending here doesn't count; left button only, right-click is for context menus.
    private async void OnHostPortTextPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        await CopyHostPortAsync();
    }

    private async void OnCopyHostPortClick(object? sender, RoutedEventArgs e) => await CopyHostPortAsync();

    private async Task CopyHostPortAsync()
    {
        if (DataContext is not GroupViewModel group
            || !await ClipboardCopyHelper.CopyTextAsync(this, group.Group.HostPort))
        {
            return;
        }

        HostPortCopiedText.IsVisible = true;
        _hostPortCopiedTimer.Stop();
        _hostPortCopiedTimer.Start();
    }

    private async void OnEventsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (!ClipboardCopyHelper.IsCopyShortcut(e) || sender is not ListBox listBox)
        {
            return;
        }

        if (await ClipboardCopyHelper.CopySelectedLinesAsync<EventEntry>(this, listBox, entry => entry.Text))
        {
            e.Handled = true;
        }
    }

    private async void OnHintsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (!ClipboardCopyHelper.IsCopyShortcut(e) || sender is not ListBox listBox)
        {
            return;
        }

        var copied = await ClipboardCopyHelper.CopySelectedLinesAsync<HintEntry>(this, listBox,
            hint => $"{hint.ItemName}: {hint.FindingPlayerName} -> {hint.ReceivingPlayerName} : {hint.LocationName}");
        if (copied)
        {
            e.Handled = true;
        }
    }

    private async void OnCopyEventsFromHereClick(object? sender, RoutedEventArgs e)
    {
        await ClipboardCopyHelper.CopyFromSelectedOnwardsAsync<EventEntry>(this, EventsListBox, entry => entry.Text);
    }

    private async void OnCopyHintsFromHereClick(object? sender, RoutedEventArgs e)
    {
        await ClipboardCopyHelper.CopyFromSelectedOnwardsAsync<HintEntry>(this, HintsListBox,
            hint => $"{hint.ItemName}: {hint.FindingPlayerName} -> {hint.ReceivingPlayerName} : {hint.LocationName}");
    }

    // Hidden without a selection: it needs an anchor row and would otherwise just cover the list.
    private void OnEventsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CopyEventsFromHereButton.IsVisible = EventsListBox.SelectedItems is { Count: > 0 };
    }

    private void OnHintsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CopyHintsFromHereButton.IsVisible = HintsListBox.SelectedItems is { Count: > 0 };
    }

    // Terminal-style history recall; a single-line TextBox has no native use for Up/Down.
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
    /// A never-selected tab materializes this ComboBox lazily, and a brand-new ComboBox can resolve its
    /// initial SelectedItem wrongly even though the bound value is correct - so re-assert it once on Loaded.
    /// </summary>
    private void OnChatSlotComboBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox { DataContext: GroupViewModel group } comboBox)
        {
            comboBox.SelectedItem = group.SelectedChatSlot;
        }
    }

    // Asks for the admin password first if not yet logged in.
    private async void OnAdminButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GroupViewModel group } || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (!group.Admin.IsLoggedIn && !await AdminLoginWindow.ShowDialogAsync(owner, group.Admin))
        {
            return;
        }

        group.SelectedRightPanel = RightPanelView.Admin;
    }

    private void OnHintButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GroupViewModel group } && TopLevel.GetTopLevel(this) is Window owner)
        {
            HintPickerWindow.Show(owner, group.HintPicker);
        }
    }
}
