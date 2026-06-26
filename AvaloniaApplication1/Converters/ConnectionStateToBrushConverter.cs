using System;
using System.Globalization;
using Archipolygo.Models;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Archipolygo.Converters;

/// <summary>
/// Maps a <see cref="ConnectionState"/> to a small status-dot color shown in
/// the tab header, so a slot's connection status - especially "not
/// connected" and "connecting/waiting for its turn" - is visible at a
/// glance without having to select the tab first.
/// </summary>
public class ConnectionStateToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ConnectionState.Connected => Brushes.LimeGreen,
            ConnectionState.Connecting => Brushes.Orange,
            ConnectionState.Reconnecting => Brushes.Orange,
            ConnectionState.Queued => Brushes.Goldenrod,
            ConnectionState.Error => Brushes.Red,
            ConnectionState.Disconnected => Brushes.Gray,
            _ => Brushes.Gray
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
