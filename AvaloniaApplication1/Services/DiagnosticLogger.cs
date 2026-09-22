using System;
using System.IO;

namespace Archipolygo.Services;

/// <summary>
/// Real <see cref="IDiagnosticLogger"/> - appends plain-text lines to
/// <c>%AppData%/Archipolygo/diagnostic.log</c>, alongside
/// <see cref="PersistenceService"/>'s own files. Registered as a singleton
/// (see App.axaml.cs) so every service instrumented with one writes to the
/// same file through the same lock - the design-time/test constructors of
/// those services fall back to <see cref="NullDiagnosticLogger"/> instead of
/// each creating their own instance here, precisely to avoid several
/// uncoordinated instances appending to the same file at once.
/// </summary>
public sealed class DiagnosticLogger : IDiagnosticLogger
{
    /// <summary>
    /// Once the file passes this size, the older half of its lines is
    /// dropped (see <see cref="TrimIfTooLarge"/>) - keeps a long-running
    /// session's log bounded without ever losing all history at once.
    /// </summary>
    private const long MaxFileSizeBytes = 2 * 1024 * 1024;

    private readonly string _logFilePath;
    private readonly object _lock = new();

    public DiagnosticLogger()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Archipolygo"))
    {
    }

    /// <summary>Test-only entry point, same reasoning as <see cref="PersistenceService"/>'s internal string constructor.</summary>
    internal DiagnosticLogger(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _logFilePath = Path.Combine(appDataDirectory, "diagnostic.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception}");

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";

        lock (_lock)
        {
            try
            {
                TrimIfTooLarge();
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // The diagnostic logger must never itself be the reason
                // something else throws - a failed write here is simply lost.
            }
        }
    }

    private void TrimIfTooLarge()
    {
        var info = new FileInfo(_logFilePath);
        if (!info.Exists || info.Length <= MaxFileSizeBytes)
        {
            return;
        }

        var lines = File.ReadAllLines(_logFilePath);
        File.WriteAllLines(_logFilePath, lines[(lines.Length / 2)..]);
    }

    public string ReadAll()
    {
        lock (_lock)
        {
            try
            {
                return File.Exists(_logFilePath) ? File.ReadAllText(_logFilePath) : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
