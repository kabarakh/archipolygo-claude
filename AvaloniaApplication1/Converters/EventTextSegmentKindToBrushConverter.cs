using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Archipolygo.Models;

namespace Archipolygo.Converters;

/// <summary>
/// Maps <see cref="EventTextSegmentKind"/> to a foreground brush for the
/// events log. The item-related colors (trap/progression/useful/other) match
/// the official Archipelago client's palette (Salmon/Plum/SlateBlue/Cyan) so
/// they look familiar to players coming from other AP trackers; the
/// player-name colors follow the same Magenta-for-yourself/Yellow-for-others
/// convention, with an extra color for players tracked by another tab of
/// this app on the same server instance.
/// </summary>
public class EventTextSegmentKindToBrushConverter : IValueConverter
{
    private static readonly IBrush OwnSlotName = Brushes.Magenta;
    private static readonly IBrush ConnectedSlotName = Brushes.Orange;
    private static readonly IBrush OtherSlotName = Brushes.Gold;
    private static readonly IBrush ItemTrap = Brushes.Salmon;
    private static readonly IBrush ItemProgression = Brushes.Plum;
    private static readonly IBrush ItemUseful = Brushes.SlateBlue;
    private static readonly IBrush ItemOther = Brushes.Cyan;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            EventTextSegmentKind.OwnSlotName => OwnSlotName,
            EventTextSegmentKind.ConnectedSlotName => ConnectedSlotName,
            EventTextSegmentKind.OtherSlotName => OtherSlotName,
            EventTextSegmentKind.ItemTrap => ItemTrap,
            EventTextSegmentKind.ItemProgression => ItemProgression,
            EventTextSegmentKind.ItemUseful => ItemUseful,
            EventTextSegmentKind.ItemOther => ItemOther,
            // PlainText: don't touch Foreground at all, so the TextBlock keeps
            // whatever the theme/inherited default would otherwise be.
            _ => BindingOperations.DoNothing
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
