using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

/// <summary>
/// Code-behind for <see cref="GroupDetailView"/> - see that XAML file's own
/// doc comment for why this content is a standalone UserControl rather than
/// living inline in MainWindow.axaml. Every handler below used to live in
/// MainWindow.axaml.cs and assumed "this" was the owning Window directly;
/// now resolved per-call via <see cref="TopLevel.GetTopLevel"/> instead,
/// since this content can be hosted by either MainWindow (inside its
/// TabControl.ContentTemplate) or a DetachedGroupWindow (see
/// Feature-Plaene/Tab-Eigenes-Fenster.md).
/// </summary>
public partial class GroupDetailView : UserControl
{
    public GroupDetailView()
    {
        InitializeComponent();
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
        if (!ClipboardCopyHelper.IsCopyShortcut(e) || sender is not ListBox listBox)
        {
            return;
        }

        if (await ClipboardCopyHelper.CopySelectedLinesAsync<EventEntry>(this, listBox, entry => entry.Text))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Same mechanism as <see cref="OnEventsListKeyDown"/>, for the Hints
    /// list instead - its own selection and clipboard content, unrelated to
    /// the Events list's. Each copied line reproduces what's shown for that
    /// hint (item, finder/receiver, location) as a single line of text.
    /// </summary>
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

    /// <summary>
    /// "Copy from here" for the Events list - copies the earliest selected
    /// event and everything after it in the current filtered view (see
    /// <see cref="ClipboardCopyHelper.CopyFromSelectedOnwardsAsync{T}"/>),
    /// same per-line text as <see cref="OnEventsListKeyDown"/>.
    /// </summary>
    private async void OnCopyEventsFromHereClick(object? sender, RoutedEventArgs e)
    {
        await ClipboardCopyHelper.CopyFromSelectedOnwardsAsync<EventEntry>(this, EventsListBox, entry => entry.Text);
    }

    /// <summary>
    /// Same mechanism as <see cref="OnCopyEventsFromHereClick"/>, for the
    /// Hints list - same per-line text as <see cref="OnHintsListKeyDown"/>.
    /// </summary>
    private async void OnCopyHintsFromHereClick(object? sender, RoutedEventArgs e)
    {
        await ClipboardCopyHelper.CopyFromSelectedOnwardsAsync<HintEntry>(this, HintsListBox,
            hint => $"{hint.ItemName}: {hint.FindingPlayerName} -> {hint.ReceivingPlayerName} : {hint.LocationName}");
    }

    /// <summary>
    /// Shows the Events list's floating "Copy from here" button only while a
    /// row is selected - it needs an anchor row, and hiding it otherwise keeps
    /// it from covering the list for nothing (Kompakteres-Layout.md Teil C;
    /// it used to be a permanently visible, mostly disabled button). Code-behind-driven rather than a bound view-model property,
    /// matching this list's existing selection handling (see
    /// <see cref="OnEventsListKeyDown"/>).
    /// </summary>
    private void OnEventsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CopyEventsFromHereButton.IsVisible = EventsListBox.SelectedItems is { Count: > 0 };
    }

    /// <summary>Same mechanism as <see cref="OnEventsListSelectionChanged"/>, for the Hints list.</summary>
    private void OnHintsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CopyHintsFromHereButton.IsVisible = HintsListBox.SelectedItems is { Count: > 0 };
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
    /// Opens the "Hint..." picker as a real dialog (see
    /// <see cref="HintPickerWindow"/>), owned by whichever window currently
    /// hosts this control - MainWindow for a docked tab, a
    /// <see cref="DetachedGroupWindow"/> for a detached one (see
    /// Feature-Plaene/Tab-Eigenes-Fenster.md).
    /// </summary>
    private void OnHintButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GroupViewModel group } && TopLevel.GetTopLevel(this) is Window owner)
        {
            HintPickerWindow.Show(owner, group.HintPicker);
        }
    }
}
