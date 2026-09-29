namespace Archipolygo.Models;

/// <summary>
/// Which panel is shown on the right side of a tab: the hint list, the
/// full received-items list, or (only for a server marked "I'm the admin",
/// see <see cref="ServerConnectionGroup.IsAdmin"/>) the Admin view.
/// </summary>
public enum RightPanelView
{
    Hints,
    ReceivedItems,
    Admin,
}
