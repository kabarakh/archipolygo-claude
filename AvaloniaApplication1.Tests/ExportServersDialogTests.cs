using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

public class ExportServersDialogTests
{
    private sealed class RecordingConfigExportService : IConfigExportService
    {
        public Exception? ExportFailure { get; init; }

        public List<(string FilePath, List<ServerConnectionGroup> Groups, AppSettings? Settings)> Exports { get; } = new();

        public void Export(string filePath, IEnumerable<ServerConnectionGroup> groups, AppSettings? settings)
        {
            if (ExportFailure is not null)
            {
                throw ExportFailure;
            }

            Exports.Add((filePath, groups.ToList(), settings));
        }

        public ConfigExportContent Import(string filePath) => throw new NotSupportedException();
    }

    private static readonly ServerConnectionGroup Alpha = new() { Name = "Alpha", Host = "alpha.example", Port = 1111, Color = "#E57373" };
    private static readonly ServerConnectionGroup Beta = new() { Name = "Beta", Host = "beta.example", Port = 2222 };
    private static readonly ServerConnectionGroup Gamma = new() { Name = "Gamma", Host = "gamma.example", Port = 3333 };

    private static ExportServersViewModel NewViewModel(IConfigExportService? exportService = null, AppSettings? settings = null) =>
        new([Alpha, Beta, Gamma], settings ?? new AppSettings(), exportService ?? new RecordingConfigExportService());

    private static ExportServersWindow ShowWindow(ExportServersViewModel viewModel)
    {
        var window = new ExportServersWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static List<CheckBox> ServerCheckBoxes(Window window) =>
        window.FindControl<ItemsControl>("ServerList")!.GetVisualDescendants().OfType<CheckBox>().ToList();

    [AvaloniaFact]
    public void ListsEveryServerWithNameAndHostPort_AllSelected()
    {
        var window = ShowWindow(NewViewModel());

        var checkBoxes = ServerCheckBoxes(window);
        Assert.Equal(3, checkBoxes.Count);
        Assert.All(checkBoxes, checkBox => Assert.True(checkBox.IsChecked));

        var texts = window.FindControl<ItemsControl>("ServerList")!.GetVisualDescendants().OfType<TextBlock>()
            .Select(textBlock => textBlock.Text).ToList();
        Assert.Contains("Alpha", texts);
        Assert.Contains("alpha.example:1111", texts);
        Assert.Contains("gamma.example:3333", texts);
    }

    [AvaloniaFact]
    public void IncludeAppSettings_StartsUnchecked()
    {
        var window = ShowWindow(NewViewModel());

        Assert.False(window.FindControl<CheckBox>("IncludeAppSettingsCheckBox")!.IsChecked);
        Assert.True(window.FindControl<Button>("ExportButton")!.IsEnabled);
        Assert.False(window.FindControl<TextBlock>("ErrorTextBlock")!.IsVisible);
    }

    [AvaloniaFact]
    public void UncheckingEveryServer_DisablesExport_AndRecheckingEnablesIt()
    {
        var window = ShowWindow(NewViewModel());
        var exportButton = window.FindControl<Button>("ExportButton")!;

        foreach (var checkBox in ServerCheckBoxes(window))
        {
            checkBox.IsChecked = false;
        }
        Dispatcher.UIThread.RunJobs();
        Assert.False(exportButton.IsEnabled);

        ServerCheckBoxes(window)[1].IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(exportButton.IsEnabled);
    }

    [AvaloniaFact]
    public void TryExport_PassesOnlySelectedGroups_AndNoSettingsByDefault()
    {
        var exportService = new RecordingConfigExportService();
        var viewModel = NewViewModel(exportService);
        viewModel.Servers[1].IsSelected = false;

        Assert.True(viewModel.TryExport("/tmp/export.json"));

        var export = Assert.Single(exportService.Exports);
        Assert.Equal("/tmp/export.json", export.FilePath);
        Assert.Equal(new[] { Alpha, Gamma }, export.Groups);
        Assert.Null(export.Settings);
    }

    [AvaloniaFact]
    public void TryExport_WithIncludeAppSettingsChecked_PassesTheSettings()
    {
        var exportService = new RecordingConfigExportService();
        var settings = new AppSettings();
        var window = ShowWindow(NewViewModel(exportService, settings));
        var viewModel = (ExportServersViewModel)window.DataContext!;

        window.FindControl<CheckBox>("IncludeAppSettingsCheckBox")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        viewModel.TryExport("/tmp/export.json");

        Assert.Same(settings, Assert.Single(exportService.Exports).Settings);
    }

    [AvaloniaFact]
    public void TryExport_IoFailure_ShowsTheErrorInsteadOfThrowing()
    {
        var exportService = new RecordingConfigExportService { ExportFailure = new IOException("Disk full") };
        var window = ShowWindow(NewViewModel(exportService));
        var viewModel = (ExportServersViewModel)window.DataContext!;

        Assert.False(viewModel.TryExport("/tmp/export.json"));
        Dispatcher.UIThread.RunJobs();

        var errorText = window.FindControl<TextBlock>("ErrorTextBlock")!;
        Assert.True(errorText.IsVisible);
        Assert.Contains("Disk full", errorText.Text);
    }
}
