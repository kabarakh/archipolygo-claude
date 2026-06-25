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

        services.AddSingleton<IPersistenceService, PersistenceService>();
        services.AddSingleton<IMessageHistoryService, MessageHistoryService>();
        services.AddSingleton<IHintService, HintService>();
        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddSingleton<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}