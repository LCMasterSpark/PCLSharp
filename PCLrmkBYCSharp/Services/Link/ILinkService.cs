using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public interface ILinkService
{
    int InviteCodeVersion { get; }

    LinkInviteInfo CreateHostInvite(int serverPort);

    LinkInviteParseResult ParseInviteCode(string? code);

    string BuildInviteCode(LinkInviteInfo invite);

    string BuildShareText(LinkInviteInfo invite);
}

public sealed record LinkInviteParseResult(bool Success, LinkInviteInfo? Invite, string Message);
