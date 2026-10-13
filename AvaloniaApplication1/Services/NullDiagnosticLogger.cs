using System;

namespace Archipolygo.Services;

/// <summary>Default for design-time/test constructors so they never write log files.</summary>
public sealed class NullDiagnosticLogger : IDiagnosticLogger
{
    public static readonly NullDiagnosticLogger Instance = new();

    private NullDiagnosticLogger()
    {
    }

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }

    public string ReadAll() => string.Empty;
}
