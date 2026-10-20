using System;
using System.IO;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

public sealed class DataDirectoryResolverTests : IDisposable
{
    private readonly string _executableDirectory = Path.Combine(Path.GetTempPath(), $"archipolygo-exe-{Guid.NewGuid():N}");
    private readonly string _appDataRoot = Path.Combine(Path.GetTempPath(), $"archipolygo-appdata-{Guid.NewGuid():N}");

    public DataDirectoryResolverTests()
    {
        Directory.CreateDirectory(_executableDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(_executableDirectory, recursive: true);
    }

    private void CreateMarker() =>
        File.WriteAllText(Path.Combine(_executableDirectory, DataDirectoryResolver.PortableMarkerFileName), "");

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void WithoutMarker_UsesAppDataAndReportsNothing(bool isManagedInstall, bool isMacOS)
    {
        var result = DataDirectoryResolver.Resolve(_executableDirectory, _appDataRoot, isManagedInstall, isMacOS);

        Assert.Equal(new DataDirectory(Path.Combine(_appDataRoot, "Archipolygo"), false, null), result);
    }

    [Fact]
    public void WithMarker_UnmanagedNonMac_UsesDataFolderNextToExecutable()
    {
        CreateMarker();

        var result = DataDirectoryResolver.Resolve(_executableDirectory, _appDataRoot, isManagedInstall: false, isMacOS: false);

        Assert.Equal(new DataDirectory(Path.Combine(_executableDirectory, "data"), true, null), result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithMarker_ManagedInstall_IgnoresMarker(bool isMacOS)
    {
        CreateMarker();

        var result = DataDirectoryResolver.Resolve(_executableDirectory, _appDataRoot, isManagedInstall: true, isMacOS);

        Assert.False(result.IsPortable);
        Assert.Equal(Path.Combine(_appDataRoot, "Archipolygo"), result.Path);
        Assert.Equal(PortableMarkerIgnoredReason.ManagedInstall, result.IgnoredMarkerReason);
    }

    [Fact]
    public void WithMarker_UnmanagedOnMac_IgnoresMarker()
    {
        CreateMarker();

        var result = DataDirectoryResolver.Resolve(_executableDirectory, _appDataRoot, isManagedInstall: false, isMacOS: true);

        Assert.False(result.IsPortable);
        Assert.Equal(Path.Combine(_appDataRoot, "Archipolygo"), result.Path);
        Assert.Equal(PortableMarkerIgnoredReason.UnsupportedOperatingSystem, result.IgnoredMarkerReason);
    }
}
