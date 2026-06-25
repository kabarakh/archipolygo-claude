using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Archipolygo.Converters;

/// <summary>
/// Reports whether a bound enum value equals a given <c>ConverterParameter</c>
/// (the enum member's name). Used with Avalonia's "Classes.xyz" style-class
/// binding syntax to highlight whichever filter button currently represents
/// the active value of an enum-typed view model property, e.g.:
/// <c>Classes.active="{Binding SelectedHintFilter, Converter={StaticResource EnumEqualsConverter}, ConverterParameter=Unfound}"</c>.
/// Only used one-way (target is a style class, applied/removed automatically
/// as the source enum changes); the actual filter switching still happens via
/// a separate <c>Command</c> on the same button, so <see cref="ConvertBack"/>
/// is unused but implemented for completeness/potential reuse with a
/// two-way-bindable target (e.g. a real <see cref="Avalonia.Controls.Primitives.ToggleButton.IsChecked"/>).
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string memberName)
        {
            return Enum.Parse(targetType, memberName);
        }

        return BindingOperations.DoNothing;
    }
}
