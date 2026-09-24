using System;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Newtonsoft.Json;

namespace Archipolygo.Services;

/// <summary>
/// Closes an <see cref="IArchipelagoSession"/>'s socket and works around two
/// Archipelago.MultiClient.Net memory-leak bugs that DisconnectAsync() alone
/// doesn't fix. See Umsetzungsplan.md, section "Socket-Cleanup: der
/// Speicherleck-Workaround" for the full analysis (verified against
/// Archipelago.MultiClient.Net 6.7.1's source and a live GC-root trace).
/// Split out of <see cref="ConnectionManager"/> since it needs almost none
/// of that class's connection/leader state - just logging.
/// </summary>
internal sealed class SocketCleanup
{
    private readonly IMessageHistoryService _messageHistoryService;
    private readonly IDiagnosticLogger _diagnosticLogger;

    public SocketCleanup(IMessageHistoryService messageHistoryService, IDiagnosticLogger diagnosticLogger)
    {
        _messageHistoryService = messageHistoryService;
        _diagnosticLogger = diagnosticLogger;
    }

    /// <summary>
    /// Closes <paramref name="session"/>'s socket and actually waits for it,
    /// then applies the reflection-based cleanup below. See this class's own
    /// doc comment.
    /// </summary>
    public async Task CloseAsync(GroupViewModel group, SlotProfile? slot, IArchipelagoSession session)
    {
        try
        {
            await session.Socket.DisconnectAsync();
        }
        catch
        {
            // Best-effort cleanup; nothing more to do.
        }

        // Two independent library bugs neither of which DisconnectAsync
        // actually fixes - see Umsetzungsplan.md, section "Socket-Cleanup:
        // der Speicherleck-Workaround" for the full analysis.
        try
        {
            if (FindClientWebSocket(session.Socket) is { } rawSocket)
            {
                rawSocket.Abort();
            }
        }
        catch (Exception ex)
        {
            // Best-effort; logged visibly since silently failing here would
            // silently reopen the memory leak this exists to fix.
            _messageHistoryService.HandleError(
                group, $"[{slot?.DisplayName ?? "unknown slot"}] internal cleanup workaround (socket abort) failed: {ex.Message}");
            _diagnosticLogger.Error($"[{group.Group.Name}] [{slot?.DisplayName ?? "unknown slot"}] Socket abort cleanup workaround failed - may leak the session", ex);
        }

        try
        {
            CompleteSendQueue(session.Socket);
        }
        catch (Exception ex)
        {
            // See above - same reasoning, other half of the workaround.
            _messageHistoryService.HandleError(
                group, $"[{slot?.DisplayName ?? "unknown slot"}] internal cleanup workaround (send queue) failed: {ex.Message}");
            _diagnosticLogger.Error($"[{group.Group.Name}] [{slot?.DisplayName ?? "unknown slot"}] Send queue cleanup workaround failed - may leak the session", ex);
        }
    }

    /// <summary>
    /// Reaches through <see cref="IArchipelagoSocketHelper"/> to the
    /// internal <c>ClientWebSocket</c> field so <see cref="CloseAsync"/> can
    /// abort it directly. See Umsetzungsplan.md, section "Socket-Cleanup:
    /// der Speicherleck-Workaround".
    /// </summary>
    private static WebSocket? FindClientWebSocket(IArchipelagoSocketHelper socketHelper)
    {
        for (var type = socketHelper.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("Socket", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (field is not null)
            {
                return field.GetValue(socketHelper) as WebSocket;
            }
        }

        return null;
    }

    /// <summary>
    /// Reaches through <see cref="IArchipelagoSocketHelper"/> to the
    /// internal <c>sendQueue</c> and calls <c>CompleteAdding()</c> to
    /// unstick a blocked <c>SendLoop</c>. See Umsetzungsplan.md, section
    /// "Socket-Cleanup: der Speicherleck-Workaround".
    /// </summary>
    private static void CompleteSendQueue(IArchipelagoSocketHelper socketHelper)
    {
        for (var type = socketHelper.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("sendQueue", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (field is null)
            {
                continue;
            }

            var queue = field.GetValue(socketHelper);
            queue?.GetType().GetMethod("CompleteAdding", BindingFlags.Public | BindingFlags.Instance)?.Invoke(queue, null);
            return;
        }
    }

    /// <summary>
    /// True for the exact exception <see cref="CompleteSendQueue"/>
    /// deliberately provokes - the intended result of that workaround, not a
    /// real failure. See Umsetzungsplan.md, section "Erwartete Exceptions
    /// durch die eigenen Workarounds".
    /// </summary>
    public static bool IsExpectedSendQueueCompletionError(Exception ex) =>
        ex is InvalidOperationException &&
        ex.Message.Contains("marked as complete with regards to additions", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for a malformed incoming <c>Bounced</c> packet from another
    /// client/game in the room, not a bug here. See Umsetzungsplan.md,
    /// section "Erwartete Exceptions durch die eigenen Workarounds".
    /// </summary>
    public static bool IsMalformedBounceDataError(Exception ex) =>
        ex is JsonSerializationException &&
        ex.Message.Contains("Path 'data'", StringComparison.OrdinalIgnoreCase) &&
        ex.Message.Contains("Dictionary", StringComparison.OrdinalIgnoreCase) &&
        ex.Message.Contains("JToken", StringComparison.OrdinalIgnoreCase);
}
