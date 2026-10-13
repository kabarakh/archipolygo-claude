using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Archipolygo.Converters;

/// <summary>
/// True when the bound enum value's name equals <c>ConverterParameter</c>; used for
/// <c>Classes.active</c> bindings on filter buttons. One-way only.
/// </summary>
public class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return false;
        }

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(EnumEqualsConverter)} is one-way only.");
}
