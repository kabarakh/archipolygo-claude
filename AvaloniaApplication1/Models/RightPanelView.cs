namespace Archipolygo.Models;

/// <summary>The right-hand panel of a tab; <see cref="Admin"/> only exists when <see cref="ServerConnectionGroup.IsAdmin"/> is set.</summary>
public enum RightPanelView
{
    Hints,
    ReceivedItems,
    Admin,
}
