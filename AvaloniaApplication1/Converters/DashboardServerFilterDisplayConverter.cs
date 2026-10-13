using System;
using System.Globalization;
using Archipolygo.ViewModels;
using Avalonia.Data.Converters;

namespace Archipolygo.Converters;

/// <summary>Shows a group's header text, or "All servers" for the null "no filter" entry.</summary>
public class DashboardServerFilterDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is GroupViewModel group ? group.HeaderText : "All servers";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
