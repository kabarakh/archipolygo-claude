using System;

namespace Archipolygo.Services;

/// <summary>
/// Durable diagnostic log, separate from the in-memory per-group Events list
/// (<see cref="MessageHistoryService"/>) that only lives as long as a group's
/// tab is open and gets trimmed to <see cref="Models.AppSettings.EventHistoryLimit"/>.
/// Deliberately terse - connection lifecycle milestones and anything that was
/// previously a silently swallowed <c>catch (Exception)</c>, never per-message/
/// per-item/per-hint content - so a long-running session's log stays small
/// enough to actually read, while still being able to answer "why didn't a
/// reconnect happen" or "why did loading groups.json fail" after the fact.
/// Exported to a user-chosen file from <see cref="ViewModels.SettingsViewModel"/>.
/// </summary>
public interface IDiagnosticLogger
{
    /// <summary>A connection lifecycle milestone - connected, disconnected, leader switched, a catch-up sweep started/finished.</summary>
    void Info(string message);

    /// <summary>Something recoverable that's still worth knowing about if a user reports odd behavior - a transient retry, a rejected malformed message, a rejected login.</summary>
    void Warning(string message);

    /// <summary>A failure that was previously swallowed silently - a corrupted file, a failed save, an unexpected exception.</summary>
    void Error(string message, Exception? exception = null);

    /// <summary>Full contents of the on-disk diagnostic log, for <see cref="ViewModels.SettingsViewModel"/>'s export button.</summary>
    string ReadAll();
}
