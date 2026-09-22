using System;
using System.IO;
using System.Linq;

namespace Archipolygo.Services;

/// <summary>
/// Real <see cref="IDiagnosticLogger"/> - appends plain-text lines to one
/// file per app run under <c>%AppData%/Archipolygo/logs</c>, alongside
/// <see cref="PersistenceService"/>'s own files. Registered as a singleton
/// (see App.axaml.cs) so every service instrumented with one writes to the
/// same file through the same lock - the design-time/test constructors of
/// those services fall back to <see cref="NullDiagnosticLogger"/> instead of
/// each creating their own instance here, precisely to avoid several
/// uncoordinated instances appending to the same file at once.
/// </summary>
public sealed class DiagnosticLogger : IDiagnosticLogger
{
    /// <summary>How many of the most recent runs' log files are kept - see <see cref="PruneOldLogFiles"/>.</summary>
    private const int MaxLogFiles = 10;

    private const string FilePrefix = "diagnostic-";
    private const string FileExtension = ".log";

    private readonly string _logDirectory;

    /// <summary>
    /// One file for this whole app run, named after the moment this
    /// singleton was constructed (i.e. app start) - not one file per day or
    /// per size threshold. Sortable lexicographically the same as
    /// chronologically, which <see cref="PruneOldLogFiles"/> relies on.
    /// </summary>
    private readonly string _logFilePath;

    private readonly object _lock = new();

    public DiagnosticLogger()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Archipolygo"))
    {
    }

    /// <summary>Test-only entry point, same reasoning as <see cref="PersistenceService"/>'s internal string constructor.</summary>
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
                // The diagnostic logger must never itself be the reason
                // something else throws - a failed write here is simply lost.
            }
        }
    }

    /// <summary>
    /// Keeps at most <see cref="MaxLogFiles"/> of this run's own file plus
    /// however many earlier runs' files are still on disk, deleting the
    /// oldest ones first - so a long history of app starts doesn't grow
    /// <c>logs/</c> forever. Runs once, right after this run's own filename
    /// is decided, leaving room for that not-yet-created file rather than
    /// pruning down to the full cap and then going one over.
    /// </summary>
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
            // Best-effort - a leftover old file is harmless clutter, not worth failing startup over.
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
