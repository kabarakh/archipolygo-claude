using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
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
            ViewModel.ResolveTrackerIdAsync);
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

}
