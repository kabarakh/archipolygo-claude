namespace Archipolygo.Models;

/// <summary>
/// What kind of event asks for attention; each has its own count/blink toggle (<see cref="AttentionCategorySetting"/>).
/// Only ever reported for live activity, never for catch-up backlog.
/// </summary>
public enum AttentionCategory
{
    /// <summary>A new, still-unfound hint where one of this group's configured slots is the receiver or finder.</summary>
    OwnHint,

    DeathLink,

    /// <summary>A progression item arriving for one of this group's configured slots.</summary>
    ProgressionItem,

    /// <summary>Any other (useful/filler/trap) item arriving for one of this group's configured slots.</summary>
    OtherItem,

    /// <summary>Chat from another player mentioning a configured slot name - takes precedence over <see cref="Chat"/>.</summary>
    ChatMention,

    /// <summary>Any other chat message from a player who isn't one of this group's configured slots.</summary>
    Chat
}
