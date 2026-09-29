using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): which leader lines become
/// <see cref="EventType.Admin"/> entries, and how their plain server text is
/// colored (Admin-Funktionen.md in the feature-plan archive).
/// </summary>
public class AdminEventFormatterTests
{
    private static readonly (string Name, EventTextSegmentKind Kind)[] Players =
    {
        ("Null", EventTextSegmentKind.OwnSlotName),
        ("Sibling", EventTextSegmentKind.ConnectedSlotName),
        ("LinkMK8Dx", EventTextSegmentKind.OtherSlotName),
        ("Done", EventTextSegmentKind.OtherSlotName),
    };

    [Theory]
    [InlineData(AdminLineSource.Chat, "!admin login ********", true)]
    [InlineData(AdminLineSource.Chat, "hello", false)]
    [InlineData(AdminLineSource.CommandResult, "Login successful. You can now issue server side commands.", true)]
    [InlineData(AdminLineSource.CommandResult, "Password incorrect.", true)]
    [InlineData(AdminLineSource.CommandResult, "You must first login using !admin login [password]", true)]
    [InlineData(AdminLineSource.CommandResult, "Player Status on team 0:", false)]
    [InlineData(AdminLineSource.AdminCommandResult, "Set option hint_cost to 5", true)]
    [InlineData(AdminLineSource.Broadcast, "Cheat console: sending \"Bowser Jr.\" to Done (LinkMK8Dx)", true)]
    [InlineData(AdminLineSource.Broadcast, "Now that you are connected, ...", false)]
    public void IsAdminLine(AdminLineSource source, string text, bool expected) =>
        Assert.Equal(expected, AdminEventFormatter.IsAdminLine(source, text));

    [Fact]
    public void CheatConsoleLine_ColorsItemAndBothNames()
    {
        var segments = AdminEventFormatter.BuildSegments("Cheat console: sending \"Bowser Jr.\" to Done (LinkMK8Dx)", Players);

        Assert.Equal("Cheat console: sending \"Bowser Jr.\" to Done (LinkMK8Dx)", string.Concat(segments.Select(s => s.Text)));
        Assert.Contains(segments, s => s.Text == "Bowser Jr." && s.Kind == EventTextSegmentKind.ItemOther);
        Assert.Contains(segments, s => s.Text == "Done" && s.Kind == EventTextSegmentKind.OtherSlotName);
        Assert.Contains(segments, s => s.Text == "LinkMK8Dx" && s.Kind == EventTextSegmentKind.OtherSlotName);
    }

    [Fact]
    public void SendEcho_ColorsSenderAsOwnSlot_QuotedPlayer_AndItem()
    {
        var segments = AdminEventFormatter.BuildSegments("Null: !admin /send \"Sibling\" \"Hookshot\"", Players);

        Assert.Equal(EventTextSegmentKind.OwnSlotName, segments.Single(s => s.Text == "Null").Kind);
        Assert.Equal(EventTextSegmentKind.ConnectedSlotName, segments.Single(s => s.Text == "Sibling").Kind);
        Assert.Equal(EventTextSegmentKind.ItemOther, segments.Single(s => s.Text == "Hookshot").Kind);
    }

    [Fact]
    public void NonSendLine_NeverColorsQuotedTextAsAnItem()
    {
        var segments = AdminEventFormatter.BuildSegments("Null: !admin /option release_mode \"goal\"", Players);

        Assert.DoesNotContain(segments, s => s.Kind == EventTextSegmentKind.ItemOther);
    }

    [Theory]
    [InlineData("Set option server_password to hunter2", "Set option server_password to ********")]
    [InlineData("Set option password to room-pw", "Set option password to ********")]
    [InlineData("Set option hint_cost to 5", "Set option hint_cost to 5")]
    [InlineData("Null: !admin login hunter2", "Null: !admin login ********")]
    [InlineData("Null: !admin /option server_password \"hunter2\"", "Null: !admin /option server_password ********")]
    public void MaskPasswords_HidesPasswordValuesInOptionAnswers(string text, string expected) =>
        Assert.Equal(expected, AdminEventFormatter.MaskPasswords(text));

    [Fact]
    public void NameInsideAnotherWord_IsNotColored()
    {
        var segments = AdminEventFormatter.BuildSegments("Nullified by Doner", Players);

        Assert.All(segments, s => Assert.Equal(EventTextSegmentKind.PlainText, s.Kind));
    }
}
