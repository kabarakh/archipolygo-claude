using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Archipolygo.Converters;

/// <summary>"▾" while expanded, "▸" while collapsed.</summary>
public class ExpandArrowConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "▾" : "▸";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
