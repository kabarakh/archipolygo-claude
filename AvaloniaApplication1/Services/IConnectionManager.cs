using System.Threading.Tasks;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Opens/closes Archipelago connections for connection tabs and forwards
/// incoming data to the <see cref="IMessageHistoryService"/>.
/// </summary>
public interface IConnectionManager
{
    Task ConnectAsync(TabViewModel tab);

    Task DisconnectAsync(TabViewModel tab);

    /// <summary>
    /// Sends a chat message on the tab's active session via <c>session.Say</c>.
    /// Does nothing if the tab is not currently connected.
    /// </summary>
    Task SendMessageAsync(TabViewModel tab, string text);
}
