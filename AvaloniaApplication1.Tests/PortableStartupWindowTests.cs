using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AvaloniaApplication1.Tests;

public class PortableStartupWindowTests
{
    private static PortableStartupWindow ShowWindow(PortableStartupViewModel viewModel)
    {
        var window = new PortableStartupWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void TransferOffer_ShowsTheThreeChoicesAndNoQuit()
    {
        var window = ShowWindow(PortableStartupViewModel.OfferTransfer("/appdata", "/app/data"));

        Assert.True(window.FindControl<Button>("CopyButton")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<Button>("MoveButton")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<Button>("StartEmptyButton")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("QuitButton")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void NotWritable_ShowsOnlyQuit()
    {
        var window = ShowWindow(PortableStartupViewModel.NotWritable("/app/data"));

        Assert.True(window.FindControl<Button>("QuitButton")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("CopyButton")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void ClickingMove_ReportsMove()
    {
        var window = new PortableStartupWindow { DataContext = PortableStartupViewModel.OfferTransfer("/appdata", "/app/data") };
        var choice = window.ShowAndWaitForChoiceAsync();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>("MoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(choice.IsCompleted, "Closed did not complete the task");
        Assert.Equal(PortableStartupChoice.MoveFromAppData, choice.Result);
    }

    [AvaloniaFact]
    public void ClosingWithoutAButton_ReportsQuit()
    {
        var window = new PortableStartupWindow { DataContext = PortableStartupViewModel.OfferTransfer("/appdata", "/app/data") };
        var choice = window.ShowAndWaitForChoiceAsync();
        Dispatcher.UIThread.RunJobs();

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(choice.IsCompleted, "Closed did not complete the task");
        Assert.Equal(PortableStartupChoice.Quit, choice.Result);
    }
}
