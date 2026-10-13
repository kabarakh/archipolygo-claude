using System;

namespace Archipolygo.Services;

/// <summary>
/// Durable on-disk diagnostic log. Deliberately terse: lifecycle milestones and otherwise-swallowed
/// failures only, never per-message/item/hint content, so it stays readable.
/// </summary>
public interface IDiagnosticLogger
{
    void Info(string message);

    /// <summary>Recoverable but noteworthy: a transient retry, a malformed message, a rejected login.</summary>
    void Warning(string message);

    /// <summary>A failure that is otherwise swallowed (corrupted file, failed save, unexpected exception).</summary>
    void Error(string message, Exception? exception = null);

    string ReadAll();
}
