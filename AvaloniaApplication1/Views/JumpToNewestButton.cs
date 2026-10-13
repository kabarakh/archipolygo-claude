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
/// Floating "Jump to newest" button that also owns an event list's stick-to-bottom auto-scroll.
/// Place it after the target ListBox in the same Grid cell and bind <c>TargetListBox="{Binding #SomeListBox}"</c>.
/// </summary>
public class JumpToNewestButton : Button
{
    // Without this, theme lookup keys off this subclass (no ControlTheme) and it gets Button's bare template.
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

    // Persistent intent rather than re-derived from scroll geometry; see OnScrollViewerScrollChanged.
    private bool _stickToBottom = true;

    public JumpToNewestButton()
    {
        Content = "↓ Jump to newest";
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 16, 12);
        IsVisible = false;
        Classes.Add("floating-overlay");

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

        // TargetListBox is resolved by the time this control's own Loaded fires, even in lazily materialized tabs.
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

    // A mouse-wheel scroll is the one unambiguous "user scrolled away" signal; tunnel routing sees it before the ScrollViewer consumes it.
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

        // The inner ScrollViewer may not exist until the template is applied.
        Attach();
        listBox.TemplateApplied += (_, _) => Attach();
    }

    /// <summary>
    /// While sticking, re-asserts ScrollToEnd on <em>every</em> ScrollChanged, not once per content change:
    /// Avalonia's own scroll anchoring nudges the offset off the end over several later calls (ExtentDelta=0)
    /// once the item cap trims rows above the viewport, and a single correction only wins the first round.
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
