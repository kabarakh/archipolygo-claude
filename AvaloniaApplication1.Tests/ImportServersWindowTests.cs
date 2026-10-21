using System.Linq;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

public class ImportServersWindowTests
{
    private static readonly ServerConnectionGroup LocalGroup = new() { Name = "Local", Host = "room.example", Port = 1 };

    private static ConfigExportContent Content(AppSettings? settings) => new(
        new[]
        {
            new ServerConnectionGroup { Name = "Existing", Host = "ROOM.example", Port = 1 },
            new ServerConnectionGroup { Name = "Brand new", Host = "other.example", Port = 2 },
        },
        settings);

    private static ImportServersWindow ShowWindow(ImportServersViewModel viewModel)
    {
        var window = new ImportServersWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void OnlyTheServerWithTheSameHostAndPort_IsMarkedAsReplacing()
    {
        var window = ShowWindow(new ImportServersViewModel(Content(null), new[] { LocalGroup }));

        var visibleBadges = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("overwrite-badge") && b.IsEffectivelyVisible)
            .ToList();
        var badge = Assert.Single(visibleBadges);
        Assert.Equal("Existing", ((ImportServerRowViewModel)badge.DataContext!).Group.Name);
    }

    [AvaloniaFact]
    public void SettingsCheckbox_OnlyShownWhenTheFileHasSettings_AndOffByDefault()
    {
        var withoutSettings = ShowWindow(new ImportServersViewModel(Content(null), new[] { LocalGroup }));
        Assert.False(withoutSettings.FindControl<CheckBox>("ImportSettingsCheckBox")!.IsEffectivelyVisible);

        var withSettings = ShowWindow(new ImportServersViewModel(Content(new AppSettings()), new[] { LocalGroup }));
        var checkBox = withSettings.FindControl<CheckBox>("ImportSettingsCheckBox")!;
        Assert.True(checkBox.IsEffectivelyVisible);
        Assert.False(checkBox.IsChecked);
    }

    [AvaloniaFact]
    public void DeselectingEveryServer_DisablesImport_AndSelectionOnlyHoldsCheckedOnes()
    {
        var viewModel = new ImportServersViewModel(Content(new AppSettings()), new[] { LocalGroup });
        var window = ShowWindow(viewModel);
        var importButton = window.FindControl<Button>("ImportButton")!;
        Assert.True(importButton.IsEffectivelyEnabled);

        viewModel.Rows[0].IsSelected = false;
        Assert.Equal(new[] { "Brand new" }, viewModel.CreateSelection().Groups.Select(g => g.Name));
        Assert.Null(viewModel.CreateSelection().Settings);

        viewModel.Rows[1].IsSelected = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(importButton.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void ErrorState_ShowsOnlyTheMessageAndClose()
    {
        var window = ShowWindow(ImportServersViewModel.ForError("The file is not an Archipolygo export."));

        Assert.True(window.FindControl<SelectableTextBlock>("ErrorText")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<Button>("CloseButton")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("ImportButton")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<ItemsControl>("ServerList")!.IsEffectivelyVisible);
    }
}
