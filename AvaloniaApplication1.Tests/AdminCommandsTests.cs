using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): the exact "!admin" lines the Admin
/// view sends (Admin-Funktionen.md in the feature-plan archive). The quoting
/// rule differs per command - see <see cref="AdminCommands"/>'s doc comment:
/// shlex-parsed commands get quoted names, raw ones ("/release", "/collect")
/// must not, since MultiServer.py compares their raw text to the name exactly.
/// </summary>
public class AdminCommandsTests
{
    [Fact]
    public void SendItem_SingleAmount_UsesSendWithQuotedNames() =>
        Assert.Equal("!admin /send \"Player One\" \"Progressive Sword\"", AdminCommands.SendItem("Player One", "Progressive Sword", 1));

    [Fact]
    public void SendItem_SeveralAmount_UsesSendMultiple() =>
        Assert.Equal("!admin /send_multiple 3 \"Mira\" \"Missile\"", AdminCommands.SendItem("Mira", "Missile", 3));

    [Theory]
    [InlineData(0, "!admin /send \"A\" \"B\"")]
    [InlineData(250, "!admin /send_multiple 100 \"A\" \"B\"")]
    public void SendItem_AmountIsClampedToWhatTheServerAccepts(int amount, string expected) =>
        Assert.Equal(expected, AdminCommands.SendItem("A", "B", amount));

    [Fact]
    public void SendLocation_QuotesBothNames() =>
        Assert.Equal("!admin /send_location \"Player One\" \"Energy Tank, Brinstar Ceiling\"",
            AdminCommands.SendLocation("Player One", "Energy Tank, Brinstar Ceiling"));

    [Fact]
    public void ReleaseAndCollect_SendTheNameUnquoted()
    {
        Assert.Equal("!admin /release Player One", AdminCommands.Release("Player One"));
        Assert.Equal("!admin /collect Player One", AdminCommands.Collect("Player One"));
    }

    [Fact]
    public void Quote_EscapesOnlyWhatShlexTreatsAsAnEscapeInsideDoubleQuotes() =>
        Assert.Equal("\"Say \\\"hi\\\" C:\\\\ $5 `x`\"", AdminCommands.Quote("Say \"hi\" C:\\ $5 `x`"));

    [Fact]
    public void SetOption_QuotesTheValue() =>
        Assert.Equal("!admin /option release_mode \"goal\"", AdminCommands.SetOption("release_mode", "goal"));

    [Theory]
    [InlineData("!admin login secret", true)]
    [InlineData("  !ADMIN LOGIN secret", true)]
    [InlineData("!admin /option server_password new", true)]
    [InlineData("!admin /option password new", true)]
    [InlineData("!admin /send \"A\" \"B\"", false)]
    [InlineData("!hint Hookshot", false)]
    public void ContainsPassword_DetectsPasswordCarryingLines(string line, bool expected) =>
        Assert.Equal(expected, AdminCommands.ContainsPassword(line));
}
