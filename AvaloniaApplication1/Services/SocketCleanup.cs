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
/// Closes a session's socket and works around two Archipelago.MultiClient.Net memory leaks DisconnectAsync() doesn't fix.
/// See Umsetzungsplan.md, section "Socket-Cleanup: der Speicherleck-Workaround".
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

    public async Task CloseAsync(GroupViewModel group, SlotProfile? slot, IArchipelagoSession session)
    {
        try
        {
            await session.Socket.DisconnectAsync();
        }
        catch
        {
            // Best-effort.
        }

        try
        {
            if (FindClientWebSocket(session.Socket) is { } rawSocket)
            {
                rawSocket.Abort();
            }
        }
        catch (Exception ex)
        {
            // Logged visibly: failing silently would reopen the memory leak.
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
            _messageHistoryService.HandleError(
                group, $"[{slot?.DisplayName ?? "unknown slot"}] internal cleanup workaround (send queue) failed: {ex.Message}");
            _diagnosticLogger.Error($"[{group.Group.Name}] [{slot?.DisplayName ?? "unknown slot"}] Send queue cleanup workaround failed - may leak the session", ex);
        }
    }

    /// <summary>Reflects into the internal <c>ClientWebSocket</c>. See Umsetzungsplan.md, section "Socket-Cleanup: der Speicherleck-Workaround".</summary>
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
    /// Completes the internal <c>sendQueue</c> to unstick a blocked <c>SendLoop</c>.
    /// See Umsetzungsplan.md, section "Socket-Cleanup: der Speicherleck-Workaround".
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
    /// The exception <see cref="CompleteSendQueue"/> deliberately provokes.
    /// See Umsetzungsplan.md, section "Erwartete Exceptions durch die eigenen Workarounds".
    /// </summary>
    public static bool IsExpectedSendQueueCompletionError(Exception ex) =>
        ex is InvalidOperationException &&
        ex.Message.Contains("marked as complete with regards to additions", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A malformed <c>Bounced</c> packet from another client in the room.
    /// See Umsetzungsplan.md, section "Erwartete Exceptions durch die eigenen Workarounds".
    /// </summary>
    public static bool IsMalformedBounceDataError(Exception ex) =>
        ex is JsonSerializationException &&
        ex.Message.Contains("Path 'data'", StringComparison.OrdinalIgnoreCase) &&
        ex.Message.Contains("Dictionary", StringComparison.OrdinalIgnoreCase) &&
        ex.Message.Contains("JToken", StringComparison.OrdinalIgnoreCase);
}
