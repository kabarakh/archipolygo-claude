using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using Archipolygo.Models;

namespace Archipolygo.Converters;

/// <summary>
/// Maps <see cref="EventTextSegmentKind"/> to an event-log brush. Dark item colors are the official
/// Archipelago client's values; Light variants are darker hue-preserving derivations (WCAG >= 4.5:1 on white).
/// Known limitation: already-rendered rows keep their color after a live theme switch, because Avalonia only
/// re-runs a converter when its source value changes.
/// </summary>
public class EventTextSegmentKindToBrushConverter : IValueConverter
{
    private static readonly IBrush OwnSlotNameDark = Brush.Parse("#EE00EE");
    private static readonly IBrush OwnSlotNameLight = Brush.Parse("#D100D1");

    // App-specific: a player tracked by another tab of this app (AP only knows "you" vs. "others").
    private static readonly IBrush ConnectedSlotNameDark = Brushes.Orange;
    private static readonly IBrush ConnectedSlotNameLight = Brush.Parse("#A46A00");

    // Deliberately Gold rather than AP's very pale "yellow" (#FAFAD2), which is hard to tell from plain text.
    private static readonly IBrush OtherSlotNameDark = Brushes.Gold;
    private static readonly IBrush OtherSlotNameLight = Brush.Parse("#8B7500");

    private static readonly IBrush ItemTrapDark = Brush.Parse("#FA8072");
    private static readonly IBrush ItemTrapLight = Brush.Parse("#E81F08");

    private static readonly IBrush ItemProgressionDark = Brush.Parse("#AF99EF");
    private static readonly IBrush ItemProgressionLight = Brush.Parse("#805EE6");

    private static readonly IBrush ItemUsefulDark = Brush.Parse("#6D8BE8");
    private static readonly IBrush ItemUsefulLight = Brush.Parse("#496FE2");

    private static readonly IBrush ItemOtherDark = Brush.Parse("#00EEEE");
    private static readonly IBrush ItemOtherLight = Brush.Parse("#008484");

    // App-specific, distinct from every item/player color; Firebrick already has enough contrast on light backgrounds.
    private static readonly IBrush DeathLinkDark = Brush.Parse("#E05B5B");
    private static readonly IBrush DeathLinkLight = Brushes.Firebrick;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isLightTheme = Application.Current?.ActualThemeVariant == ThemeVariant.Light;

        return value switch
        {
            EventTextSegmentKind.OwnSlotName => isLightTheme ? OwnSlotNameLight : OwnSlotNameDark,
            EventTextSegmentKind.ConnectedSlotName => isLightTheme ? ConnectedSlotNameLight : ConnectedSlotNameDark,
            EventTextSegmentKind.OtherSlotName => isLightTheme ? OtherSlotNameLight : OtherSlotNameDark,
            EventTextSegmentKind.ItemTrap => isLightTheme ? ItemTrapLight : ItemTrapDark,
            EventTextSegmentKind.ItemProgression => isLightTheme ? ItemProgressionLight : ItemProgressionDark,
            EventTextSegmentKind.ItemUseful => isLightTheme ? ItemUsefulLight : ItemUsefulDark,
            EventTextSegmentKind.ItemOther => isLightTheme ? ItemOtherLight : ItemOtherDark,
            EventTextSegmentKind.DeathLink => isLightTheme ? DeathLinkLight : DeathLinkDark,
            // PlainText: leave Foreground untouched so the inherited default applies.
            _ => BindingOperations.DoNothing
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
