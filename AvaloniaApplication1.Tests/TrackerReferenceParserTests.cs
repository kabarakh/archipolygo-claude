using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="TrackerReferenceParser.TryParseTrackerReference"/> -
/// pure string logic, no network/Avalonia involved. See
/// Feature-Plaene/Fortschrittsanzeigen.md, "Konsequenzen für das
/// Datenmodell", for the three accepted forms.
/// </summary>
public class TrackerReferenceParserTests
{
    [Theory]
    [InlineData("2gVkMQgISGScA8wsvDZg5A")]
    [InlineData("  2gVkMQgISGScA8wsvDZg5A  ")]
    public void BareId_NoSlash_IsTrackerId(string input)
    {
        var ok = TrackerReferenceParser.TryParseTrackerReference(input, out var kind, out var value);

        Assert.True(ok);
        Assert.Equal(TrackerReferenceKind.TrackerId, kind);
        Assert.Equal("2gVkMQgISGScA8wsvDZg5A", value);
    }

    [Theory]
    [InlineData("https://archipelago.gg/tracker/2gVkMQgISGScA8wsvDZg5A")]
    [InlineData("http://archipelago.gg/tracker/2gVkMQgISGScA8wsvDZg5A")]
    [InlineData("archipelago.gg/tracker/2gVkMQgISGScA8wsvDZg5A")]
    [InlineData("archipelago.gg/tracker/2gVkMQgISGScA8wsvDZg5A/")]
    [InlineData("/tracker/2gVkMQgISGScA8wsvDZg5A")]
    [InlineData("https://archipelago.gg/tracker/2gVkMQgISGScA8wsvDZg5A?foo=bar")]
    [InlineData("https://archipelago.gg/tracker/2gVkMQgISGScA8wsvDZg5A#frag")]
    [InlineData("https://my-self-hosted-webhost.example/tracker/2gVkMQgISGScA8wsvDZg5A")]
    public void TrackerUrl_AnyScheme_TrailingSlash_QueryOrFragment_ExtractsTrackerId(string input)
    {
        var ok = TrackerReferenceParser.TryParseTrackerReference(input, out var kind, out var value);

        Assert.True(ok);
        Assert.Equal(TrackerReferenceKind.TrackerId, kind);
        Assert.Equal("2gVkMQgISGScA8wsvDZg5A", value);
    }

    [Theory]
    [InlineData("https://archipelago.gg/room/kK5fmxd8TfisU5Yp_eg")]
    [InlineData("archipelago.gg/room/kK5fmxd8TfisU5Yp_eg/")]
    [InlineData("/room/kK5fmxd8TfisU5Yp_eg")]
    [InlineData("https://archipelago.gg/room/kK5fmxd8TfisU5Yp_eg?foo=bar")]
    public void RoomUrl_ExtractsRoomId(string input)
    {
        var ok = TrackerReferenceParser.TryParseTrackerReference(input, out var kind, out var value);

        Assert.True(ok);
        Assert.Equal(TrackerReferenceKind.RoomId, kind);
        Assert.Equal("kK5fmxd8TfisU5Yp_eg", value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wirres Freitext das niemand eintippen sollte")]
    [InlineData("https://archipelago.gg/sphere_tracker/2gVkMQgISGScA8wsvDZg5A")]
    [InlineData("foo/bar")]
    public void InvalidInput_ReturnsFalse(string? input)
    {
        var ok = TrackerReferenceParser.TryParseTrackerReference(input, out _, out _);

        Assert.False(ok);
    }
}
