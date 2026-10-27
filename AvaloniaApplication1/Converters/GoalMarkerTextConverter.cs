using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Archipolygo.Converters;

/// <summary>
/// Values: [name, hasGoal]. Prefixes 🏁 to a slot name that reached its goal. Inline in the one TextBlock
/// rather than a sibling, so the name keeps wrapping inside the segment WrapPanel.
/// </summary>
public class GoalMarkerTextConverter : IMultiValueConverter
{
    public const string Marker = "🏁 ";

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = values.Count > 0 ? values[0] as string ?? string.Empty : string.Empty;
        // hasGoal is unset when the binding source has no RoomSlotStatus.
        var hasGoal = values.Count > 1 && values[1] is true;
        return hasGoal ? Marker + text : text;
    }
}
