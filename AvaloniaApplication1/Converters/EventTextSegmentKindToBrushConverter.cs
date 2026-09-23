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
/// Maps <see cref="EventTextSegmentKind"/> to a foreground brush for the
/// events log. The Dark-theme item-related colors (trap/progression/useful/
/// other) are the official Archipelago client's own exact values (from
/// ArchipelagoMW/Archipelago's <c>NetUtils.py</c>/<c>data/client.kv</c>,
/// checked directly rather than assumed - see CLAUDE.md), so they match
/// familiar to players coming from other AP trackers; the player-name colors
/// follow the same Magenta-for-yourself convention, with
/// <see cref="OtherSlotNameDark"/> deliberately kept as the existing `Gold`
/// rather than switched to AP's own very pale `yellow` (better distinguishable
/// from plain text - see Feature-Plaene/Theme-Umschalter-und-Server-Farbwaehler.md),
/// plus an extra color for players tracked by another tab of this app on the
/// same server instance. Every Light-theme color is a hand-derived,
/// hue-preserving darker variant of its Dark counterpart (WCAG contrast
/// against white checked to be at least 4.5:1, computed rather than guessed) -
/// Archipelago itself has no light-theme variant of this palette to draw on
/// (its own GUI client uses the exact same colors regardless of its
/// Light/Dark chrome setting), so these had to be derived here instead. See
/// that same plan document's "Konkrete Farbwerte" table for the values and
/// how they were computed. Known limitation: a row already rendered before a
/// live theme switch keeps its old color until something else re-triggers
/// its binding (a fresh event, or an app restart) - Avalonia only
/// re-invokes a converter when its *source* value changes, not on an
/// unrelated global theme-variant change, and reworking this into
/// theme-variant resource dictionaries instead (which would auto-update
/// every already-rendered row) was out of scope for this pass.
/// </summary>
public class EventTextSegmentKindToBrushConverter : IValueConverter
{
    private static readonly IBrush OwnSlotNameDark = Brush.Parse("#EE00EE");
    private static readonly IBrush OwnSlotNameLight = Brush.Parse("#D100D1");

    // App-specific - no Archipelago equivalent (AP only distinguishes
    // "yourself" from "everyone else"; this app additionally distinguishes a
    // player tracked passively via another one of this app's own tabs).
    private static readonly IBrush ConnectedSlotNameDark = Brushes.Orange;
    private static readonly IBrush ConnectedSlotNameLight = Brush.Parse("#A46A00");

    // Kept as Gold rather than AP's own very pale "yellow" (#FAFAD2) - see
    // this class's own doc comment.
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

    // App-specific (no Archipelago equivalent) - deliberately distinct from
    // every item/player color above so an incoming DeathLink
    // (Feature-Plaene/Archiv/DeathLink.md) stands out at a glance in the
    // event log, evoking "death" the way other AP trackers do. Firebrick
    // itself already tests well against a light background (6.7:1), so only
    // Dark gets a brightened variant.
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
            // PlainText: don't touch Foreground at all, so the TextBlock keeps
            // whatever the theme/inherited default would otherwise be.
            _ => BindingOperations.DoNothing
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
