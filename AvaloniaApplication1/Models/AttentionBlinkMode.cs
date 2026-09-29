namespace Archipolygo.Models;

/// <summary>
/// How loudly a window asks for attention - see
/// <see cref="AppSettings.AttentionBlinkMode"/> for how it's persisted (as
/// a plain string) and <see cref="Services.WindowAttentionService"/> for what
/// each value maps to per platform.
/// </summary>
public enum AttentionBlinkMode
{
    /// <summary>Never blink - counting (if enabled per category) still happens.</summary>
    Off,

    /// <summary>Blink a limited number of times (Windows), bounce the Dock icon once (macOS).</summary>
    Count,

    /// <summary>Keep blinking/bouncing until the window is activated - the original pre-2026-09-26 behavior.</summary>
    UntilFocus
}
