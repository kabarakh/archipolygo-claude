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
    /// <summary>Composition root; every service is registered as a singleton.</summary>
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
        services.AddSingleton<IGroupWindowLocator, GroupWindowLocator>();
        services.AddSingleton<IWindowAttentionService, WindowAttentionService>();
        services.AddSingleton<IAttentionTracker, AttentionTracker>();
        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddSingleton<IMultiworldTrackerService, MultiworldTrackerService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        // Applied before the main window exists so it never flashes the wrong theme/density at startup.
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

            // Registered here rather than in MainWindow/MainWindowViewModel - see IGroupWindowLocator.
            var groupWindowLocator = Services.GetRequiredService<IGroupWindowLocator>();
            groupWindowLocator.RegisterMainWindow(mainWindow);
            mainWindow.GroupWindowLocator = groupWindowLocator;

            // Set only here so headless tests never touch the real Dock/taskbar.
            mainWindow.UnreadBadgeService = new UnreadBadgeService();

            mainWindowViewModel.OpenDetachedWindow = mainWindow.OpenDetachedGroupWindow;
            mainWindowViewModel.CloseDetachedWindow = mainWindow.CloseDetachedGroupWindowIfOpen;

            // Wired here because DataContext isn't assigned yet in the MainWindow/view-model constructors.
            mainWindowViewModel.ShowPasswordPromptDialogAsync =
                (viewModel, cancellationToken) => PasswordPromptWindow.ShowDialogAsync(mainWindow, viewModel, cancellationToken);

            mainWindowViewModel.ShowConfirmationDialogAsync =
                viewModel => ConfirmationWindow.ShowDialogAsync(mainWindow, viewModel);

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}