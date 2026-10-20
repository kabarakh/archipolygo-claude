using System;
using System.IO;
using System.Linq;

namespace Archipolygo.Services;

/// <summary>File-system steps for the portable mode's first start; see <see cref="DataDirectoryResolver"/>.</summary>
public static class PortableDataSetup
{
    private const string WriteProbeFileName = ".write-probe";

    public static bool CanWriteTo(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, WriteProbeFileName);
            File.WriteAllText(probePath, "");
            File.Delete(probePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool ShouldOfferAppDataTransfer(string portableDirectory, string appDataDirectory) =>
        IsMissingOrEmpty(portableDirectory) && !IsMissingOrEmpty(appDataDirectory);

    public static void CopyFromAppData(string appDataDirectory, string portableDirectory) =>
        CopyOrRollBack(appDataDirectory, portableDirectory);

    /// <summary>AppData is only deleted once the copy fully succeeded.</summary>
    public static void MoveFromAppData(string appDataDirectory, string portableDirectory)
    {
        CopyOrRollBack(appDataDirectory, portableDirectory);
        Directory.Delete(appDataDirectory, recursive: true);
    }

    private static bool IsMissingOrEmpty(string directory) =>
        !Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any();

    // A half-copied data folder is no longer empty, so the next start would silently skip the offer.
    private static void CopyOrRollBack(string sourceDirectory, string targetDirectory)
    {
        try
        {
            CopyDirectory(sourceDirectory, targetDirectory);
        }
        catch
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            throw;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)));
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(subdirectory, Path.Combine(targetDirectory, Path.GetFileName(subdirectory)));
        }
    }
}
