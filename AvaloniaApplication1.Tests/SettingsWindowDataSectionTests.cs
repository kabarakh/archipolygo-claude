using System;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AvaloniaApplication1.Tests;

public class SettingsWindowDataSectionTests
{
    private static SettingsWindow ShowWindow(DataDirectory? dataDirectory)
    {
        var dataManagement = dataDirectory is null
            ? null
            : new DataManagementContext(dataDirectory, new ConfigExportService(), Array.Empty<ServerConnectionGroup>, () => new AppSettings(), _ => { });
        var window = new SettingsWindow { DataContext = SettingsViewModel.FromSettings(new AppSettings(), dataManagement: dataManagement) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static bool IsVisible(Window window, string name) => window.FindControl<Control>(name)!.IsEffectivelyVisible;

    [AvaloniaFact]
    public void WithoutDataManagement_TheWholeSectionIsHidden()
    {
        var window = ShowWindow(null);

        Assert.False(IsVisible(window, "DataSection"));
    }

    [AvaloniaFact]
    public void NormalInstall_ShowsButtons_ButNoPortableLineOrHint()
    {
        var window = ShowWindow(new DataDirectory("/appdata/Archipolygo", IsPortable: false, IgnoredMarkerReason: null));

        Assert.True(IsVisible(window, "ExportServersButton"));
        Assert.True(IsVisible(window, "ImportServersButton"));
        Assert.True(IsVisible(window, "OpenDataFolderButton"));
        Assert.False(IsVisible(window, "PortableModeLine"));
        Assert.False(IsVisible(window, "PortableMarkerIgnoredHint"));
    }

    [AvaloniaFact]
    public void PortableMode_ShowsTheDataPath()
    {
        var window = ShowWindow(new DataDirectory("/apps/archipolygo/data", IsPortable: true, IgnoredMarkerReason: null));

        var line = window.FindControl<SelectableTextBlock>("PortableModeLine")!;
        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal("Portable mode: data stored in /apps/archipolygo/data", line.Text);
    }

    [AvaloniaFact]
    public void IgnoredMarker_ExplainsWhy()
    {
        var window = ShowWindow(new DataDirectory("/appdata/Archipolygo", IsPortable: false, PortableMarkerIgnoredReason.ManagedInstall));

        var hint = window.FindControl<TextBlock>("PortableMarkerIgnoredHint")!;
        Assert.True(hint.IsEffectivelyVisible);
        Assert.Contains("updates itself", hint.Text);
    }
}
