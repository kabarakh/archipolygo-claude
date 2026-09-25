using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using Archipolygo.Services;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Archipolygo;

public partial class App : Application
{
    /// <summary>
    /// Composition root: every service below is registered explicitly as a
    /// singleton, so there is exactly one instance per service for the whole
    /// application lifetime (no implicit "this happens to be shared because
    /// it was passed around" instances, unlike before this was introduced).
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDiagnosticLogger, DiagnosticLogger>();
        services.AddSingleton<IPersistenceService, PersistenceService>();
        services.AddSingleton<IProfileSyncStateStore, ProfileSyncStateStore>();
        services.AddSingleton<IMessageHistoryService, MessageHistoryService>();
        services.AddSingleton<IHintService, HintService>();
        services.AddSingleton<ISessionFactory, ArchipelagoSessionFactoryAdapter>();
        // Feature-Plaene/Tab-Eigenes-Fenster.md: shared foundation for both
        // detached-window support and the window flash - registered before
        // IConnectionManager, which consumes IWindowAttentionService (via
        // HintService/its own DeathLink handling/its internal
        // SessionEventTranslator) as an optional constructor parameter.
        services.AddSingleton<IGroupWindowLocator, GroupWindowLocator>();
        services.AddSingleton<IWindowAttentionService, WindowAttentionService>();
        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddSingleton<IMultiworldTrackerService, MultiworldTrackerService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        // Applied before the main window is created, so it never briefly
        // flashes the wrong variant (or re-lays out from the wrong density)
        // at startup - see Feature-Plaene/Theme-Umschalter-und-Server-Farbwaehler.md
        // and Kompakteres-Layout.md.
        var persistedSettings = Services.GetRequiredService<IPersistenceService>().LoadSettings();
        ThemeService.Apply(persistedSettings.ThemePreference);
        DensityService.Apply(persistedSettings.UiDensity);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
            };

            // Feature-Plaene/Tab-Eigenes-Fenster.md: every group defaults to
            // the main window until/unless it's detached - see
            // IGroupWindowLocator's own doc comment for why this
            // registration happens here rather than inside MainWindow or
            // MainWindowViewModel themselves.
            var groupWindowLocator = Services.GetRequiredService<IGroupWindowLocator>();
            groupWindowLocator.RegisterMainWindow(mainWindow);
            mainWindow.GroupWindowLocator = groupWindowLocator;

            // Same wiring style as ShowPasswordPromptDialogAsync/
            // ShowConfirmationDialogAsync above - MainWindow is the one
            // place with an actual Window to open/close a
            // DetachedGroupWindow from.
            mainWindowViewModel.OpenDetachedWindow = mainWindow.OpenDetachedGroupWindow;
            mainWindowViewModel.CloseDetachedWindow = mainWindow.CloseDetachedGroupWindowIfOpen;

            // Feature-Plaene/Passwort-Speicherung.md: the only place with an
            // actual Window to own a PasswordPromptWindow - see
            // MainWindowViewModel.ShowPasswordPromptDialogAsync's own doc
            // comment for why this isn't wired inside MainWindowViewModel or
            // MainWindow.axaml.cs's own constructor instead (DataContext
            // isn't assigned yet at either of those points).
            mainWindowViewModel.ShowPasswordPromptDialogAsync =
                (viewModel, cancellationToken) => PasswordPromptWindow.ShowDialogAsync(mainWindow, viewModel, cancellationToken);

            // Same reasoning as ShowPasswordPromptDialogAsync above - only
            // this class has an actual Window to own the confirmation dialog.
            mainWindowViewModel.ShowConfirmationDialogAsync =
                viewModel => ConfirmationWindow.ShowDialogAsync(mainWindow, viewModel);

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}