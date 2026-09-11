using System;
using System.Globalization;
using Archipolygo.ViewModels;
using Avalonia.Data.Converters;

namespace Archipolygo.Converters;

/// <summary>
/// Displays a <see cref="GroupViewModel"/>'s <see cref="GroupViewModel.HeaderText"/>,
/// or "All servers" for the null entry that represents "no filter" in
/// <see cref="DashboardViewModel.HintServerFilterOptions"/> - the Dashboard's
/// shared Hints overview equivalent of <see cref="SlotFilterDisplayConverter"/>.
/// </summary>
public class DashboardServerFilterDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is GroupViewModel group ? group.HeaderText : "All servers";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
