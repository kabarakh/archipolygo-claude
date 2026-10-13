using System;
using System.IO;
using System.Linq;

namespace Archipolygo.Services;

/// <summary>
/// One log file per app run. Must be a singleton so all writers share one lock; design-time/test
/// constructors use <see cref="NullDiagnosticLogger"/> instead of creating extra instances.
/// </summary>
public sealed class DiagnosticLogger : IDiagnosticLogger
{
    private const int MaxLogFiles = 10;

    private const string FilePrefix = "diagnostic-";
    private const string FileExtension = ".log";

    private readonly string _logDirectory;

    /// <summary>One file per app run; the timestamped name sorts chronologically, which pruning relies on.</summary>
    private readonly string _logFilePath;

    private readonly object _lock = new();

    public DiagnosticLogger()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Archipolygo"))
    {
    }

    /// <summary>Test-only entry point.</summary>
    internal DiagnosticLogger(string appDataDirectory)
    {
        _logDirectory = Path.Combine(appDataDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);

        _logFilePath = Path.Combine(_logDirectory, $"{FilePrefix}{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}{FileExtension}");
        PruneOldLogFiles();
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
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // The logger must never make anything else throw; a failed write is simply lost.
            }
        }
    }

    /// <summary>Leaves room for this run's not-yet-created file within <see cref="MaxLogFiles"/>.</summary>
    private void PruneOldLogFiles()
    {
        try
        {
            var existingFiles = Directory.GetFiles(_logDirectory, $"{FilePrefix}*{FileExtension}")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .ToList();

            foreach (var oldFile in existingFiles.Skip(MaxLogFiles - 1))
            {
                File.Delete(oldFile);
            }
        }
        catch (Exception)
        {
            // Best-effort; a leftover old file is harmless.
        }
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
