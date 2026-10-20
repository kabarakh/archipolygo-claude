using System;
using System.IO;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

public sealed class PortableDataSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"archipolygo-portable-{Guid.NewGuid():N}");
    private readonly string _appDataDirectory;
    private readonly string _portableDirectory;

    public PortableDataSetupTests()
    {
        _appDataDirectory = Path.Combine(_root, "appdata", "Archipolygo");
        _portableDirectory = Path.Combine(_root, "app", "data");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void CreateAppData()
    {
        Directory.CreateDirectory(Path.Combine(_appDataDirectory, "sync-state"));
        File.WriteAllText(Path.Combine(_appDataDirectory, "groups.json"), "[]");
        File.WriteAllText(Path.Combine(_appDataDirectory, "sync-state", "slot.json"), "{}");
    }

    [Fact]
    public void CanWriteTo_CreatesTheDirectoryAndLeavesItEmpty()
    {
        Assert.True(PortableDataSetup.CanWriteTo(_portableDirectory));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_portableDirectory));
    }

    [Fact]
    public void ShouldOfferAppDataTransfer_OnlyWhenPortableIsEmptyAndAppDataHasData()
    {
        Assert.False(PortableDataSetup.ShouldOfferAppDataTransfer(_portableDirectory, _appDataDirectory));

        CreateAppData();
        Assert.True(PortableDataSetup.ShouldOfferAppDataTransfer(_portableDirectory, _appDataDirectory));

        Directory.CreateDirectory(_portableDirectory);
        Assert.True(PortableDataSetup.ShouldOfferAppDataTransfer(_portableDirectory, _appDataDirectory));

        File.WriteAllText(Path.Combine(_portableDirectory, "settings.json"), "{}");
        Assert.False(PortableDataSetup.ShouldOfferAppDataTransfer(_portableDirectory, _appDataDirectory));
    }

    [Fact]
    public void Copy_CopiesEverythingAndKeepsAppData()
    {
        CreateAppData();

        PortableDataSetup.CopyFromAppData(_appDataDirectory, _portableDirectory);

        Assert.True(File.Exists(Path.Combine(_portableDirectory, "groups.json")));
        Assert.True(File.Exists(Path.Combine(_portableDirectory, "sync-state", "slot.json")));
        Assert.True(File.Exists(Path.Combine(_appDataDirectory, "groups.json")));
    }

    [Fact]
    public void Move_CopiesEverythingAndDeletesAppData()
    {
        CreateAppData();

        PortableDataSetup.MoveFromAppData(_appDataDirectory, _portableDirectory);

        Assert.True(File.Exists(Path.Combine(_portableDirectory, "sync-state", "slot.json")));
        Assert.False(Directory.Exists(_appDataDirectory));
    }

    [Fact]
    public void FailedMove_LeavesAppDataUntouchedAndRemovesThePartialCopy()
    {
        CreateAppData();
        // A file where the copy needs a directory makes the copy fail midway.
        Directory.CreateDirectory(_portableDirectory);
        File.WriteAllText(Path.Combine(_portableDirectory, "sync-state"), "blocks the sub-directory");

        Assert.ThrowsAny<IOException>(() => PortableDataSetup.MoveFromAppData(_appDataDirectory, _portableDirectory));

        Assert.True(File.Exists(Path.Combine(_appDataDirectory, "groups.json")));
        Assert.True(File.Exists(Path.Combine(_appDataDirectory, "sync-state", "slot.json")));
        Assert.False(Directory.Exists(_portableDirectory));
    }
}
