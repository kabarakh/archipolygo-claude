using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Archipolygo.Converters;

/// <summary>
/// Turns a count into a Star-weighted <see cref="GridLength"/>, so several columns form one
/// stacked multi-color progress bar (Avalonia's <c>ProgressBar</c> has only one fill).
/// </summary>
public class CountToStarWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is IConvertible convertible ? System.Convert.ToDouble(convertible, culture) : 0d;
        return new GridLength(Math.Max(count, 0), GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
