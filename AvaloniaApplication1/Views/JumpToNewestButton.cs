using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Archipolygo.Views;

/// <summary>
/// The floating "↓ Jump to newest" button that overlays the bottom-right
/// corner of an auto-scrolling event list, and owns the auto-scroll/
/// stick-to-bottom behavior that goes with it. Originally hand-built once
/// for MainWindow's own per-server Events list; when DashboardView's
/// shared, server-spanning Events list needed the exact same behavior, this
/// was pulled out into one real control instead of a second hand-copied
/// implementation - same idea as <see cref="ClipboardCopyHelper"/> for the
/// Ctrl+C-to-copy behavior, just packaged as a control (not a static
/// helper) since this one has a visible, stateful UI element of its own
/// rather than being pure logic.
///
/// Usage: place as a sibling of the target <see cref="ListBox"/> inside the
/// same overlay <see cref="Grid"/> cell (no <c>Grid.Row</c>/<c>Grid.Column</c>
/// of its own - it floats over whatever cell the ListBox occupies),
/// declared *after* the ListBox in the XAML so it paints on top, and point
/// <see cref="TargetListBox"/> at it via Avalonia's compiled-binding
/// element-name syntax (<c>#Name</c>, not a real property-path binding -
/// this deliberately doesn't go through either control's <c>DataContext</c>,
/// so it works the same regardless of what view model hosts the list):
/// <code>
/// &lt;Grid&gt;
///     &lt;ListBox Name="SomeListBox" .../&gt;
///     &lt;views:JumpToNewestButton TargetListBox="{Binding #SomeListBox}"/&gt;
/// &lt;/Grid&gt;
/// </code>
/// </summary>
public class JumpToNewestButton : Button
{
    /// <summary>
    /// Without this, Avalonia's implicit theme lookup keys off this
    /// control's own concrete type (see <c>StyledElement.StyleKeyOverride</c>),
    /// which has no registered <c>ControlTheme</c> of its own - the button
    /// would fall back to <see cref="Button"/>'s bare pre-theme default
    /// template (a plain <c>ContentPresenter</c>, no background/border/hover
    /// chrome at all) instead of FluentTheme's real Button look. This makes
    /// Avalonia style it exactly as if it *were* a <see cref="Button"/>.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(Button);

    public static readonly StyledProperty<ListBox?> TargetListBoxProperty =
        AvaloniaProperty.Register<JumpToNewestButton, ListBox?>(nameof(TargetListBox));

    public ListBox? TargetListBox
    {
        get => GetValue(TargetListBoxProperty);
        set => SetValue(TargetListBoxProperty, value);
    }

    private ScrollViewer? _attachedScrollViewer;
    private EventHandler<ScrollChangedEventArgs>? _scrollChangedHandler;

    /// <summary>
    /// "Should the next layout settle back at the bottom" - has to be a
    /// persistent intent rather than something re-derived from scroll
    /// geometry on every call; see <see cref="OnScrollViewerScrollChanged"/>'s
    /// doc comment for why.
    /// </summary>
    private bool _stickToBottom = true;

    public JumpToNewestButton()
    {
        Content = "↓ Jump to newest";
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 16, 12);
        IsVisible = false;

        Click += (_, _) =>
        {
            if (TargetListBox is not { } listBox)
            {
                return;
            }

            listBox.SelectedIndex = -1;
            _stickToBottom = true;
            _attachedScrollViewer?.ScrollToEnd();
        };

        // By the time this button itself has loaded, TargetListBox (set via
        // x:Reference in XAML, evaluated at parse time) is already
        // available - same tab-materialization timing as the ComboBox
        // resync workaround elsewhere in this app (see CLAUDE.md), just
        // triggered off this control's own Loaded instead of the sibling
        // ListBox's.
        Loaded += (_, _) =>
        {
            if (TargetListBox is { } listBox)
            {
                AttachTo(listBox);
            }
        };

        Unloaded += (_, _) => Detach();
    }

    private void Detach()
    {
        if (_attachedScrollViewer is not null && _scrollChangedHandler is not null)
        {
            _attachedScrollViewer.ScrollChanged -= _scrollChangedHandler;
        }

        _attachedScrollViewer = null;
        _scrollChangedHandler = null;
    }

    /// <summary>
    /// Wires a genuine user gesture - a mouse-wheel scroll over the list -
    /// to drop out of auto-follow, since that is the one unambiguous signal
    /// that the user (not Avalonia's own scroll-anchoring, see
    /// <see cref="OnScrollViewerScrollChanged"/>) wants to look at something
    /// else. Tunnel routing sees it before the ScrollViewer consumes it.
    /// </summary>
    private void AttachTo(ListBox listBox)
    {
        void Attach()
        {
            if (_attachedScrollViewer is not null)
            {
                return;
            }

            _attachedScrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_attachedScrollViewer is null)
            {
                return;
            }

            _scrollChangedHandler = (s, _) => OnScrollViewerScrollChanged(s, listBox);
            _attachedScrollViewer.ScrollChanged += _scrollChangedHandler;
            _attachedScrollViewer.ScrollToEnd();

            _attachedScrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) =>
            {
                _stickToBottom = false;
            }, RoutingStrategies.Tunnel);
        }

        // The control template (and with it, the inner ScrollViewer) might
        // not be applied yet at this exact point; TemplateApplied covers
        // that case, Attach() itself covers the common case where it's
        // already available.
        Attach();
        listBox.TemplateApplied += (_, _) => Attach();
    }

    /// <summary>
    /// Enforces <see cref="_stickToBottom"/>: while true, every single
    /// <see cref="ScrollViewer.ScrollChanged"/> - not just ones caused by
    /// new content - re-clears any selection and re-scrolls to the end, so
    /// this button stays hidden; once false (the user scrolled away, see
    /// <see cref="AttachTo"/>), it instead just tracks distance from the
    /// bottom to show/hide itself, and never scrolls on its own.
    ///
    /// Re-asserting on *every* call, not once per content change, is load-
    /// bearing, not redundant - built and shipped as "just clear the
    /// selection and ScrollToEnd() once" first, then disproven with a
    /// standalone repro harness that isolated the exact same
    /// ListBox/button/scroll code from the live app: logging
    /// <see cref="ScrollViewer.CurrentAnchor"/> on every call showed
    /// Avalonia's *own* scroll-anchoring - unrelated to selection or focus,
    /// confirmed by clearing both and seeing no difference - re-picking a
    /// new anchor candidate and nudging <see cref="ScrollViewer.Offset"/>
    /// away from the end on *several separate, later* ScrollChanged calls
    /// after a per-tab item cap starts trimming items above the viewport
    /// (the anchor walked backward one realized row at a time, all with
    /// ExtentDelta=0 so nothing about a plain "did content change" check
    /// would ever see them coming). A single ScrollToEnd() only ever won
    /// the *first* round of that fight; the later rounds arrived with
    /// nothing left to notice or correct them, leaving the view - and the
    /// button - stuck short of the end. The anchor walk is empirically
    /// bounded (a handful of steps, not unbounded), so simply re-asserting
    /// every time instead of once is enough to always win it: our
    /// correction fires at least as often as anchoring's own adjustment, so
    /// it can never end up more than one step behind.
    /// </summary>
    private void OnScrollViewerScrollChanged(object? sender, ListBox listBox)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (_stickToBottom)
        {
            if (listBox.SelectedIndex != -1)
            {
                listBox.SelectedIndex = -1;
            }
            scrollViewer.ScrollToEnd();
            IsVisible = false;
            return;
        }

        const double AtBottomTolerance = 2.0;
        var distanceFromBottom = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
        IsVisible = distanceFromBottom > AtBottomTolerance;
    }
}
