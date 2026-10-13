using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Archipolygo.Models;

namespace Archipolygo.Converters;

/// <summary>Shows a slot's display name, or "All slots" for the null "no filter" entry.</summary>
public class SlotFilterDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SlotProfile slot ? slot.DisplayName : "All slots";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
