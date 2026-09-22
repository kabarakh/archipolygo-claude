using System;

namespace Archipolygo.Services;

/// <summary>
/// No-op <see cref="IDiagnosticLogger"/> - the default every instrumented
/// service (<see cref="PersistenceService"/>, <see cref="ConnectionManager"/>,
/// <see cref="UpdateService"/>) falls back to when nobody explicitly passes a
/// real <see cref="DiagnosticLogger"/> in, e.g. their design-time/test
/// constructors. Keeps those call sites (Avalonia's XAML previewer,
/// AvaloniaApplication1.Tests) from writing diagnostic files to disk at all,
/// without needing a real logger threaded through every one of them.
/// </summary>
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
