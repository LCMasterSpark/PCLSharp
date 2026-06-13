using PCLrmkBYCSharp.Services.Link;

namespace PCLrmkBYCSharp.Tests;

public sealed class PclLinkServiceTests
{
    [Fact]
    public void CreateHostInviteBuildsParseablePclCode()
    {
        var service = new PclLinkService();

        var invite = service.CreateHostInvite(25565);
        var code = service.BuildInviteCode(invite);
        var parsed = service.ParseInviteCode("【" + code + "】");

        Assert.True(parsed.Success);
        Assert.Equal(25565, parsed.Invite?.ServerPort);
        Assert.Equal(2, parsed.Invite?.Version);
        Assert.Contains(code, service.BuildShareText(invite), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseInviteCodeAcceptsLegacyV1Code()
    {
        var service = new PclLinkService();

        var result = service.ParseInviteCode("P04D2-ABCDE-12345");

        Assert.True(result.Success);
        Assert.Equal(1234, result.Invite?.ServerPort);
        Assert.Equal(1, result.Invite?.Version);
        Assert.Equal(0x05E, result.Invite?.DiscoverNodeId);
    }

    [Fact]
    public void ParseInviteCodeNormalizesLookalikeCharacters()
    {
        var service = new PclLinkService();

        var result = service.ParseInviteCode("[P04D2-ABCOI-1OIO1-02000]");

        Assert.True(result.Success);
        Assert.Equal("P04D2-ABC01", result.Invite?.NetworkName);
        Assert.Equal("10101", result.Invite?.NetworkSecret);
    }

    [Fact]
    public void ParseInviteCodeRejectsOtherLauncherRoomCode()
    {
        var service = new PclLinkService();

        var result = service.ParseInviteCode("U/example-room");

        Assert.False(result.Success);
        Assert.Contains("PCL", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateHostInviteRejectsInvalidServerPort()
    {
        var service = new PclLinkService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateHostInvite(80));
    }
}
