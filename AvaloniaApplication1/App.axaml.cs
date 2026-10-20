using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        // A logger-less probe: the real UpdateService needs the logger, which itself needs the data directory.
        var dataDirectory = DataDirectoryResolver.ResolveForCurrentProcess(new UpdateService().IsManagedInstall);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && dataDirectory.IsPortable)
        {
            // The setup dialogs may close before the main window exists; that must not end the app.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = PreparePortableDataThenStartOrQuitAsync(desktop, dataDirectory);
        }
        else
        {
            BuildServices(dataDirectory);
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                desktopLifetime.MainWindow = CreateMainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Fire-and-forget would otherwise swallow a crash and leave a windowless process running forever.
    private static async Task PreparePortableDataThenStartOrQuitAsync(IClassicDesktopStyleApplicationLifetime desktop, DataDirectory dataDirectory)
    {
        try
        {
            await PreparePortableDataThenStartAsync(desktop, dataDirectory);
        }
        catch
        {
            desktop.Shutdown(1);
            throw;
        }
    }

    /// <summary>Must finish before any service is built: the logger and persistence create their folders on construction.</summary>
    private static async Task PreparePortableDataThenStartAsync(IClassicDesktopStyleApplicationLifetime desktop, DataDirectory dataDirectory)
    {
        if (!PortableDataSetup.CanWriteTo(dataDirectory.Path))
        {
            await PortableStartupWindow.ShowAndWaitAsync(PortableStartupViewModel.NotWritable(dataDirectory.Path));
            desktop.Shutdown(1);
            return;
        }

        var appDataDirectory = DataDirectoryResolver.AppDataDirectoryForCurrentUser();
        if (PortableDataSetup.ShouldOfferAppDataTransfer(dataDirectory.Path, appDataDirectory))
        {
            var choice = await PortableStartupWindow.ShowAndWaitAsync(
                PortableStartupViewModel.OfferTransfer(appDataDirectory, dataDirectory.Path));

            if (choice == PortableStartupChoice.Quit || !await TryTransferAppDataAsync(choice, appDataDirectory, dataDirectory.Path))
            {
                desktop.Shutdown();
                return;
            }
        }

        BuildServices(dataDirectory);
        var mainWindow = CreateMainWindow();
        desktop.MainWindow = mainWindow;
        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
        mainWindow.Show();
    }

    private static async Task<bool> TryTransferAppDataAsync(PortableStartupChoice choice, string appDataDirectory, string portableDirectory)
    {
        try
        {
            if (choice == PortableStartupChoice.CopyFromAppData)
            {
                PortableDataSetup.CopyFromAppData(appDataDirectory, portableDirectory);
            }
            else if (choice == PortableStartupChoice.MoveFromAppData)
            {
                PortableDataSetup.MoveFromAppData(appDataDirectory, portableDirectory);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await PortableStartupWindow.ShowAndWaitAsync(PortableStartupViewModel.TransferFailed(ex.Message));
            return false;
        }
    }

    private static void BuildServices(DataDirectory dataDirectory)
    {
        var services = new ServiceCollection();

        services.AddSingleton(dataDirectory);
        services.AddSingleton<IDiagnosticLogger>(_ => new DiagnosticLogger(dataDirectory.Path));
        services.AddSingleton<IPersistenceService>(serviceProvider =>
            new PersistenceService(dataDirectory.Path, serviceProvider.GetRequiredService<IDiagnosticLogger>()));
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
    }

    private static MainWindow CreateMainWindow()
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

        return mainWindow;
    }
}