using System;
using System.Collections.Specialized;
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

    private async void OnAddProfileClick(object? sender, RoutedEventArgs e)
    {
        var defaultAutoConnect = ViewModel.LoadSettings().DefaultAutoConnect;
        var editorViewModel = ConnectionEditorViewModel.ForNewProfile(defaultAutoConnect, ViewModel.GetAllProfiles());
        var profile = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (profile is not null)
        {
            ViewModel.AddOrUpdateProfile(profile);
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

    private async void OnEditProfileClick(object? sender, RoutedEventArgs e)
    {
        var selectedTab = ViewModel.SelectedTab;
        if (selectedTab is null)
        {
            return;
        }

        var editorViewModel = ConnectionEditorViewModel.ForExistingProfile(selectedTab.ServerProfile, ViewModel.GetAllProfiles());
        var profile = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (profile is not null)
        {
            ViewModel.AddOrUpdateProfile(profile);
        }
    }

    /// <summary>
    /// Opens the editor pre-filled with the selected connection's data so it
    /// can be saved as a new, separate profile. The editor's duplicate check
    /// (same Host+Port+SlotName) forces the user to change something before
    /// the copy can actually be saved.
    /// </summary>
    private async void OnDuplicateProfileClick(object? sender, RoutedEventArgs e)
    {
        var selectedTab = ViewModel.SelectedTab;
        if (selectedTab is null)
        {
            return;
        }

        var editorViewModel = ConnectionEditorViewModel.ForDuplicateProfile(selectedTab.ServerProfile, ViewModel.GetAllProfiles());
        var profile = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (profile is not null)
        {
            ViewModel.AddOrUpdateProfile(profile);
        }
    }

    /// <summary>
    /// Copies the selected event's text (without the timestamp) to the
    /// clipboard on Ctrl+C (Windows/Linux) or Cmd+C (macOS, where the
    /// physical key reports as <see cref="KeyModifiers.Meta"/>).
    /// </summary>
    private async void OnEventsListKeyDown(object? sender, KeyEventArgs e)
    {
        var isCopyShortcut = e.Key == Key.C &&
            (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));

        if (!isCopyShortcut || sender is not ListBox { SelectedItem: EventEntry entry })
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(entry.Text);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Keeps the events list scrolled to the bottom as new events arrive, so
    /// the most recent message/event is always visible without the user
    /// having to scroll manually. Each <see cref="TabViewModel"/>'s events
    /// list gets its own ListBox instance (one per tab's content template),
    /// so the subscription is attached/detached per instance via Loaded/Unloaded.
    ///
    /// Scrolls the inner <see cref="ScrollViewer"/> directly to its end
    /// (rather than ListBox.ScrollIntoView, which needs the new item's
    /// container already realized). A previous version deferred this via the
    /// LayoutUpdated event, but that turned out unreliable: server-originated
    /// events (e.g. the !hint reply) often arrive together with other
    /// changes on the same tab (hint list updates, the tab header's unfound-
    /// hint count), which can trigger their own, unrelated layout passes; our
    /// one-shot LayoutUpdated handler could fire (and unsubscribe) on one of
    /// those instead of the pass that actually realizes the new event, so the
    /// scroll never happened. A short fixed delay sidesteps that race
    /// entirely - it doesn't matter which layout pass did the work, only
    /// that enough time passed for all of them to finish before scrolling.
    /// </summary>
    private void OnEventsListLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.DataContext is not TabViewModel tab)
        {
            return;
        }

        async void OnEventsChanged(object? collectionSender, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action != NotifyCollectionChangedAction.Add)
            {
                return;
            }

            await Task.Delay(75);
            listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
        }

        tab.Events.CollectionChanged += OnEventsChanged;
        listBox.Unloaded += (_, _) => tab.Events.CollectionChanged -= OnEventsChanged;
    }
}
