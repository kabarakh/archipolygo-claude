using System.Linq;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>Horizontal StackPanel children default to Stretch; the header TextBlocks need explicit VerticalAlignment="Center".</summary>
public class HorizontalStackPanelAlignmentTests
{
    [AvaloniaFact]
    public void StatusRow_HostPortTextBlock_IsVerticallyCenteredWithDisconnectButton()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();

        // Tab content only materializes once the TabControl is visible; the app starts on the Dashboard.
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        var hostPortText = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == group.Group.HostPort);
        var disconnectButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Disconnect"));

        // The actual regression signal: without VerticalAlignment="Center" the TextBlock stretches to the Button's height.
        Assert.True(disconnectButton.Bounds.Height > hostPortText.Bounds.Height,
            $"expected the TextBlock to keep its own natural (shorter) height instead of " +
            $"stretching to the Button's - text height={hostPortText.Bounds.Height}, button height={disconnectButton.Bounds.Height}");

        var textCenterY = hostPortText.Bounds.Y + hostPortText.Bounds.Height / 2;
        var buttonCenterY = disconnectButton.Bounds.Y + disconnectButton.Bounds.Height / 2;
        Assert.True(System.Math.Abs(textCenterY - buttonCenterY) < 1.0,
            $"expected the HostPort TextBlock and the Disconnect button to be vertically centered on each other; " +
            $"text center Y={textCenterY}, button center Y={buttonCenterY}");
    }
}
