namespace Archipolygo.Models;

/// <summary>
/// What kind of event is asking for the user's attention - see the
/// feature-plan archive's <c>Benachrichtigungen.md</c>. Each category has its
/// own independent "count it" / "blink for it" toggle
/// (<see cref="AttentionCategorySetting"/>), so the user decides per kind of
/// event how loud it should be. Every category is only ever reported for
/// live activity, never for backlog replayed by a catch-up sync.
/// </summary>
public enum AttentionCategory
{
    /// <summary>A new, still-unfound hint where one of this group's configured slots is the receiver or finder.</summary>
    OwnHint,

    /// <summary>An incoming DeathLink.</summary>
    DeathLink,

    /// <summary>A progression item arriving for one of this group's configured slots.</summary>
    ProgressionItem,

    /// <summary>Any other (useful/filler/trap) item arriving for one of this group's configured slots.</summary>
    OtherItem,

    /// <summary>A chat message from another player that mentions one of this group's configured slot names - takes precedence over <see cref="Chat"/>.</summary>
    ChatMention,

    /// <summary>Any other chat message from a player who isn't one of this group's configured slots.</summary>
    Chat
}
