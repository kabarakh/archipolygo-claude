namespace Archipolygo.Models;

/// <summary>
/// The right-hand panel of a tab; <see cref="Admin"/> only exists when <see cref="ServerConnectionGroup.IsAdmin"/> is set,
/// <see cref="ItemLinks"/> only when the room has item-link groups.
/// </summary>
public enum RightPanelView
{
    Hints,
    ReceivedItems,
    Admin,
    ItemLinks,
}
