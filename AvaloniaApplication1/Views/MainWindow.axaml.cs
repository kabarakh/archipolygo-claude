using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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

        // Feature-Plaene/Archiv/Dashboard-Tab.md's per-row icons: DashboardView
        // itself has no Window to anchor a dialog on and no reference to
        // MainWindowViewModel (its own DataContext is the narrower
        // DashboardViewModel) - it just raises which group was clicked, and
        // this class runs the exact same dialog flow as the toolbar buttons
        // below, just parameterized by that group instead of always
        // ViewModel.SelectedGroup.
        DashboardViewControl.AddSlotRequested += (_, group) => _ = AddSlotToGroupAsync(group);
        DashboardViewControl.EditServerRequested += (_, group) => _ = EditServerAsync(group);
        DashboardViewControl.RemoveServerRequested += (_, group) => _ = ViewModel.RemoveGroupAsync(group);
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private async void OnAddServerClick(object? sender, RoutedEventArgs e)
    {
        var defaultAutoConnect = ViewModel.LoadSettings().DefaultAutoConnect;
        var editorViewModel = ConnectionEditorViewModel.ForNewGroup(defaultAutoConnect, ViewModel.GetAllGroups(), ViewModel.ResolveTrackerIdAsync, ViewModel.ResolveRoomConnectionInfoAsync);
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            ViewModel.AddNewGroup(result.Name, result.Host, result.Port, result.Password, result.SlotName, result.AutoConnect, result.TrackerReferenceInput, result.TrackerId);
        }
    }

    private async void OnAddSlotClick(object? sender, RoutedEventArgs e)
    {
        var selectedGroup = ViewModel.SelectedGroup;
        if (selectedGroup is not null)
        {
            await AddSlotToGroupAsync(selectedGroup);
        }
    }

    /// <summary>Shared by the toolbar's "Add slot..." (always <see cref="MainWindowViewModel.SelectedGroup"/>) and the Dashboard's per-row icon (whichever group's row was clicked).</summary>
    private async Task AddSlotToGroupAsync(GroupViewModel group)
    {
        // May briefly connect/disconnect under the hood if this server has
        // no live session right now - see MainWindowViewModel.GetAvailableSlotsToAddAsync.
        var availablePlayers = await ViewModel.GetAvailableSlotsToAddAsync(group);

        var editorViewModel = ConnectionEditorViewModel.ForAddSlot(group.Group, availablePlayers);
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            ViewModel.AddSlotsToGroup(group, result.SlotsToAdd);
        }
    }

    private async void OnEditServerClick(object? sender, RoutedEventArgs e)
    {
        var selectedGroup = ViewModel.SelectedGroup;
        if (selectedGroup is not null)
        {
            await EditServerAsync(selectedGroup);
        }
    }

    /// <summary>Shared by the toolbar's "Edit server..." and the Dashboard's per-row icon - see <see cref="AddSlotToGroupAsync"/>'s doc comment.</summary>
    private async Task EditServerAsync(GroupViewModel group)
    {
        var editorViewModel = ConnectionEditorViewModel.ForEditGroup(
            group.Group,
            ViewModel.GetAllGroups(),
            ViewModel.ResolveTrackerIdAsync,
            ViewModel.ResolveRoomConnectionInfoAsync);
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            await ViewModel.UpdateGroup(group, result.Name, result.Host, result.Port, result.Password, result.AutoConnect, result.PreferredLeaderSlotId, result.SlotsToRemove, result.TrackerReferenceInput, result.TrackerId);
        }
    }

    /// <summary>
    /// Feature-Plaene/Archiv/Auto-Update.md's update-available button (a
    /// small barely-visible dot originally - now a real labeled Button, see
    /// MainWindow.axaml) - opens its attached Flyout with the version and
    /// the actual "Update now" button.
    /// </summary>
    private void OnUpdateBadgeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            FlyoutBase.ShowAttachedFlyout(control);
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settingsViewModel = SettingsViewModel.FromSettings(ViewModel.LoadSettings(), ViewModel.CheckForUpdatesAsync, ViewModel.ShowUnmanagedInstallHint, ViewModel.ReadDiagnosticLog);
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
    /// Finds a same-named descendant control starting from another element in
    /// the same, already-instantiated template - the Events/Hints/Items UI
    /// lives inside <c>TabControl.ContentTemplate</c> (see CLAUDE.md's
    /// lazy-materialization gotcha for that same template), so each open tab
    /// gets its own instance of e.g. "EventsListBox"; a plain compiled x:Name
    /// field would be ambiguous across tabs, and Avalonia doesn't generate one
    /// for elements inside a template for exactly that reason. Walking up from
    /// any element that's definitely in the same instance (e.g. the button
    /// that was just clicked) and searching each ancestor's descendants finds
    /// the right one without needing a global/static lookup.
    /// </summary>
    private static T? FindInSameTemplateInstance<T>(Visual anchor, string name) where T : Control
    {
        for (var ancestor = anchor.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            var match = ancestor.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// "Copy from here" for the Events list - copies the earliest selected
    /// event and everything after it in the current filtered view (see
    /// <see cref="ClipboardCopyHelper.CopyFromSelectedOnwardsAsync{T}"/>),
    /// same per-line text as <see cref="OnEventsListKeyDown"/>.
    /// </summary>
    private async void OnCopyEventsFromHereClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Visual anchor || FindInSameTemplateInstance<ListBox>(anchor, "EventsListBox") is not { } eventsListBox)
        {
            return;
        }

        await ClipboardCopyHelper.CopyFromSelectedOnwardsAsync<EventEntry>(this, eventsListBox, entry => entry.Text);
    }

    /// <summary>
    /// Same mechanism as <see cref="OnCopyEventsFromHereClick"/>, for the
    /// Hints list - same per-line text as <see cref="OnHintsListKeyDown"/>.
    /// </summary>
    private async void OnCopyHintsFromHereClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Visual anchor || FindInSameTemplateInstance<ListBox>(anchor, "HintsListBox") is not { } hintsListBox)
        {
            return;
        }

        await ClipboardCopyHelper.CopyFromSelectedOnwardsAsync<HintEntry>(this, hintsListBox,
            hint => $"{hint.ItemName}: {hint.FindingPlayerName} -> {hint.ReceivingPlayerName} : {hint.LocationName}");
    }

    /// <summary>
    /// Toggles the Events column's "Copy from here" button's enabled state
    /// with the Events list's selection - the button needs an anchor row, so
    /// it stays disabled (rather than silently no-op on click) until one is
    /// selected. Code-behind-driven rather than a bound view-model property,
    /// matching this list's existing selection handling (see
    /// <see cref="OnEventsListKeyDown"/>).
    /// </summary>
    private void OnEventsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || FindInSameTemplateInstance<Button>(listBox, "CopyEventsFromHereButton") is not { } button)
        {
            return;
        }

        button.IsEnabled = listBox.SelectedItems is { Count: > 0 };
    }

    /// <summary>Same mechanism as <see cref="OnEventsListSelectionChanged"/>, for the Hints list.</summary>
    private void OnHintsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || FindInSameTemplateInstance<Button>(listBox, "CopyHintsFromHereButton") is not { } button)
        {
            return;
        }

        button.IsEnabled = listBox.SelectedItems is { Count: > 0 };
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
    /// <see cref="HintPickerWindow"/>) - the button's own DataContext (from
    /// this tab's DataTemplate) is the <see cref="GroupViewModel"/>, same
    /// situation as <see cref="OnAddSlotClick"/>/<see cref="OnEditServerClick"/>
    /// elsewhere in this file.
    /// </summary>
    private void OnHintButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GroupViewModel group })
        {
            HintPickerWindow.Show(this, group.HintPicker);
        }
    }

    // ── Manual tab-reordering (Feature-Plaene/Tab-Reihenfolge.md) ──
    //
    // A plain tab click must keep working exactly as before (TabControl
    // already selects a tab on PointerPressed) - unlike DashboardView's
    // Overview rows (see DashboardView.axaml.cs), nothing here is ever
    // marked Handled on press, so this only ever adds drag detection on
    // top of the existing click behavior, never replaces it.

    private PointerPressedEventArgs? _groupTabPressedArgs;
    private GroupViewModel? _groupTabDragCandidate;
    private Point _groupTabDragStartPosition;
    private ulong _groupTabPressedTimestamp;
    private bool _groupTabDragStarted;

    private void OnGroupTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not GroupViewModel group)
        {
            return;
        }

        _groupTabPressedArgs = e;
        _groupTabDragCandidate = group;
        _groupTabDragStartPosition = e.GetPosition(control);
        _groupTabPressedTimestamp = e.Timestamp;
        _groupTabDragStarted = false;
    }

    private async void OnGroupTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_groupTabDragCandidate is not { } group || _groupTabDragStarted ||
            _groupTabPressedArgs is not { } pressedArgs || sender is not Control control)
        {
            return;
        }

        if (!GroupReorderDragDrop.ShouldStartDrag(_groupTabDragStartPosition, _groupTabPressedTimestamp, e.GetPosition(control), e.Timestamp))
        {
            return;
        }

        // Consume immediately - one DoDragDropAsync per press, and this also
        // means a PointerMoved that fires again while the drag is already
        // in flight (or after it ended) is a harmless no-op above.
        _groupTabDragStarted = true;
        await GroupReorderDragDrop.StartDragAsync(pressedArgs, group);
        group.DropIndicator = DropIndicatorPosition.None;
    }

    /// <summary>
    /// Disarms the gesture-tracking fields once the pointer comes back up -
    /// without this, a plain click (press then release, no real drag)
    /// left <see cref="_groupTabDragCandidate"/> set, and
    /// <see cref="OnGroupTabPointerMoved"/> fires for plain hover movement
    /// too, not just movement while a button is held. Once enough real time
    /// had simply passed and the cursor had moved anywhere at all - true
    /// for basically any later hover, unrelated to the original click - a
    /// phantom drag started despite no button being pressed anymore (dev
    /// feedback: clicking a tab then just moving the mouse afterward showed
    /// a drag cursor). <see cref="OnGroupTabPointerPressed"/> re-arms it
    /// fresh on the next real press.
    /// </summary>
    private void OnGroupTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _groupTabPressedArgs = null;
        _groupTabDragCandidate = null;
        _groupTabDragStarted = false;
    }

    private void OnGroupTabDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: GroupViewModel target } control)
        {
            return;
        }

        var isValidTarget = GroupReorderDragDrop.TryGetSource(e.DataTransfer) is { } source && !ReferenceEquals(source, target);
        e.DragEffects = isValidTarget ? DragDropEffects.Move : DragDropEffects.None;
        // Tab headers lay out left-to-right - left half of the hovered
        // header means "insert before this tab", right half means "after".
        target.DropIndicator = !isValidTarget
            ? DropIndicatorPosition.None
            : e.GetPosition(control).X < control.Bounds.Width / 2
                ? DropIndicatorPosition.Before
                : DropIndicatorPosition.After;
    }

    private void OnGroupTabDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control { DataContext: GroupViewModel target })
        {
            target.DropIndicator = DropIndicatorPosition.None;
        }
    }

    private void OnGroupTabDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: GroupViewModel target })
        {
            return;
        }

        var insertAfter = target.DropIndicator == DropIndicatorPosition.After;
        target.DropIndicator = DropIndicatorPosition.None;
        if (GroupReorderDragDrop.TryGetSource(e.DataTransfer) is { } source)
        {
            ViewModel.ReorderGroup(source, target, insertAfter);
        }
    }
}
