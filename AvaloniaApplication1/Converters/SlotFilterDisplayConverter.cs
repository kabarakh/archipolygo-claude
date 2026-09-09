using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Archipolygo.Models;

namespace Archipolygo.Converters;

/// <summary>
/// Displays a <see cref="SlotProfile"/>'s name (plus alias, see
/// <see cref="SlotProfile.DisplayName"/>), or "All slots" for the null entry
/// that represents "no filter" in <see cref="ViewModels.GroupViewModel.SlotFilterOptions"/>.
/// </summary>
public class SlotFilterDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SlotProfile slot ? slot.DisplayName : "All slots";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
