using System;
using System.Linq;
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
