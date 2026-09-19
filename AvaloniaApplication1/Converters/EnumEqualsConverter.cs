using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Archipolygo.Converters;

/// <summary>
/// Reports whether a bound enum value equals a given <c>ConverterParameter</c>
/// (the enum member's name). Used with Avalonia's "Classes.xyz" style-class
/// binding syntax to highlight whichever filter button currently represents
/// the active value of an enum-typed view model property, e.g.:
/// <c>Classes.active="{Binding SelectedHintFilter, Converter={StaticResource EnumEqualsConverter}, ConverterParameter=Unfound}"</c>.
/// One-way only (target is a style class, applied/removed automatically as
/// the source enum changes) - the actual filter switching happens via a
/// separate <c>Command</c> on the same button, so <see cref="ConvertBack"/>
/// is never actually invoked.
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
