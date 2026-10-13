namespace Archipolygo.Models;

/// <summary>How loudly a window asks for attention; persisted as a string in <see cref="AppSettings.AttentionBlinkMode"/>.</summary>
public enum AttentionBlinkMode
{
    /// <summary>Never blink - counting (if enabled per category) still happens.</summary>
    Off,

    /// <summary>Blink a limited number of times (Windows), bounce the Dock icon once (macOS).</summary>
    Count,

    /// <summary>Keep blinking/bouncing until the window is activated.</summary>
    UntilFocus
}
