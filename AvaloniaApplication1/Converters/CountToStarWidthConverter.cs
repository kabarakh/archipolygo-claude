using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Archipolygo.Converters;

/// <summary>
/// Turns a plain count (own/other done/open - see <see cref="ViewModels.GroupViewModel.OwnChecksDone"/>
/// and friends) into a Star-weighted <see cref="GridLength"/>, so four
/// <c>ColumnDefinition</c>s inside one <c>Grid</c> end up sized proportionally
/// to those four counts - the actual mechanism behind Feature-Plaene/Archiv/Fortschrittsanzeigen.md's
/// single combined "stacked" progress bar (own/other done/open, all in one
/// bar, not one bar per player) instead of Avalonia's plain <c>ProgressBar</c>,
/// which only ever has one fill color/value.
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
