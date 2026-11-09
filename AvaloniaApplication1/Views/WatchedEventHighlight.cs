using System.ComponentModel;
using System.Runtime.CompilerServices;
using Archipolygo.Models;
using Avalonia;
using Avalonia.Controls;

namespace Archipolygo.Views;

/// <summary>
/// Puts the <c>watched</c> class on an event-log row whose progression item went to the watched slot, so a style
/// can highlight it. A style setter can't add classes, and a highlight inside the item template would sit inside
/// the row's padding instead of on its edge.
/// </summary>
public sealed class WatchedEventHighlight
{
    private WatchedEventHighlight()
    {
    }

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WatchedEventHighlight, ListBox, bool>("IsEnabled");

    private static readonly ConditionalWeakTable<Control, RowSubscription> Subscriptions = new();

    static WatchedEventHighlight()
    {
        IsEnabledProperty.Changed.AddClassHandler<ListBox>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ListBox listBox) => listBox.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ListBox listBox, bool value) => listBox.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            listBox.ContainerPrepared += OnContainerPrepared;
            listBox.ContainerClearing += OnContainerClearing;
        }
        else
        {
            listBox.ContainerPrepared -= OnContainerPrepared;
            listBox.ContainerClearing -= OnContainerClearing;
        }
    }

    private static void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        Unsubscribe(e.Container);

        var entry = e.Container.DataContext switch
        {
            EventEntry eventEntry => eventEntry,
            DashboardEventRow row => row.Event,
            _ => null,
        };

        if (entry?.Receiver is null || !entry.IsProgressionItem)
        {
            e.Container.Classes.Set("watched", false);
            return;
        }

        var subscription = new RowSubscription(e.Container, entry.Receiver);
        Subscriptions.Add(e.Container, subscription);
        subscription.Apply();
    }

    private static void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        Unsubscribe(e.Container);
        e.Container.Classes.Set("watched", false);
    }

    private static void Unsubscribe(Control container)
    {
        if (Subscriptions.TryGetValue(container, out var previous))
        {
            previous.Dispose();
            Subscriptions.Remove(container);
        }
    }

    /// <summary>Follows <see cref="RoomSlotStatus.IsWatched"/>, so picking a watched slot later marks older rows too.</summary>
    private sealed class RowSubscription
    {
        private readonly Control _container;
        private readonly RoomSlotStatus _receiver;

        public RowSubscription(Control container, RoomSlotStatus receiver)
        {
            _container = container;
            _receiver = receiver;
            _receiver.PropertyChanged += OnReceiverChanged;
        }

        public void Apply() => _container.Classes.Set("watched", _receiver.IsWatched);

        public void Dispose() => _receiver.PropertyChanged -= OnReceiverChanged;

        private void OnReceiverChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RoomSlotStatus.IsWatched))
            {
                Apply();
            }
        }
    }
}
