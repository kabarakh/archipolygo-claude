using System.Linq;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): the horizontal-StackPanel vertical-
/// alignment gotcha documented in CLAUDE.md - "A horizontal StackPanel's
/// children default to VerticalAlignment='Stretch'. A plain TextBlock next to
/// a taller Button looks top-aligned unless it gets an explicit
/// VerticalAlignment='Center' of its own." - exercised against the real
/// instance in MainWindow.axaml's tab content header row (the host:port /
/// connection-state text next to the "Disconnect" button), which carries the
/// fix (each TextBlock has its own explicit VerticalAlignment="Center") and a
/// doc comment explaining exactly this. Verified with real Bounds after a
/// real layout pass, not a screenshot.
/// </summary>
public class HorizontalStackPanelAlignmentTests
{
    [AvaloniaFact]
    public void StatusRow_HostPortTextBlock_IsVerticallyCenteredWithDisconnectButton()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager);
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var hostPortText = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == group.Group.HostPort);
        var disconnectButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Disconnect"));

        // Both are direct children of the same horizontal StackPanel (see
        // MainWindow.axaml), so their Bounds already share one coordinate
        // space - no TranslatePoint needed to compare them.
        //
        // This is the actual regression signal: without the TextBlock's own
        // explicit VerticalAlignment="Center", the horizontal StackPanel
        // stretches it to the row's full (Button-driven) height instead of
        // leaving it at its natural text height - verified by temporarily
        // deleting that one attribute and rerunning this test, which then
        // fails right here instead of at the center-comparison below.
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
