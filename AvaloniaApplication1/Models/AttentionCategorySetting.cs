using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>
/// Per-<see cref="AttentionCategory"/> toggles, stored in
/// <see cref="AppSettings.AttentionCategories"/>. "Count" and "Blink" are
/// deliberately independent (see the feature-plan archive's
/// <c>Benachrichtigungen.md</c>): e.g. counting every item quietly without
/// blinking for any of them.
/// </summary>
public class AttentionCategorySetting
{
    public bool Count { get; set; }

    public bool Blink { get; set; }

    public AttentionCategorySetting Clone() => new() { Count = Count, Blink = Blink };

    /// <summary>
    /// Out-of-the-box behavior - chosen so an existing install behaves
    /// almost exactly as before this setting existed (the same three
    /// triggers that already blinked, plus chat mentions), with everything
    /// noisier opt-in.
    /// </summary>
    public static AttentionCategorySetting DefaultFor(AttentionCategory category) => category switch
    {
        AttentionCategory.OwnHint or AttentionCategory.DeathLink or AttentionCategory.ProgressionItem or AttentionCategory.ChatMention
            => new AttentionCategorySetting { Count = true, Blink = true },
        _ => new AttentionCategorySetting { Count = false, Blink = false }
    };

    public static Dictionary<string, AttentionCategorySetting> CreateDefaults()
    {
        var defaults = new Dictionary<string, AttentionCategorySetting>();
        foreach (var category in System.Enum.GetValues<AttentionCategory>())
        {
            defaults[category.ToString()] = DefaultFor(category);
        }

        return defaults;
    }
}
