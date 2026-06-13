using System.Security.Cryptography;
using System.Text;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public sealed class PclLinkService : ILinkService
{
    public int InviteCodeVersion => 2;

    public LinkInviteInfo CreateHostInvite(int serverPort)
    {
        if (serverPort is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(serverPort), "端口号必须在 1024 到 65535 之间。");
        }

        var networkName = "P" + serverPort.ToString("X4") + "-" + GenerateToken(5);
        var networkSecret = GenerateToken(5);
        return new LinkInviteInfo(serverPort, networkName, networkSecret, InviteCodeVersion, 0);
    }

    public LinkInviteParseResult ParseInviteCode(string? code)
    {
        if (code is null)
        {
            return Fail("邀请码为空！");
        }

        var fixedCode = FixCodeFormat(code);
        if (!(fixedCode.Length >= 14 && fixedCode[0] == 'P' && fixedCode[5] == '-' && fixedCode[11] == '-'))
        {
            if (fixedCode.StartsWith("U/", StringComparison.OrdinalIgnoreCase))
            {
                return Fail("请让房主使用 PCL 或 PCL Sharp 创建房间！");
            }

            if (fixedCode.Length == 10)
            {
                return Fail("请让房主使用非社区版的 PCL 创建房间！");
            }

            return Fail("邀请码有误，请让房主使用 PCL 或 PCL Sharp 创建房间！");
        }

        if (!TryFromBase(fixedCode.Substring(1, 4), 16, out var port))
        {
            return Fail("邀请码端口段无效！");
        }

        var version = 1;
        var discoverNodeId = 0;
        if (fixedCode.Length >= 23 && fixedCode[17] == '-')
        {
            if (!int.TryParse(fixedCode.Substring(18, 2), out version))
            {
                return Fail("邀请码版本段无效！");
            }

            if (version > InviteCodeVersion)
            {
                return Fail("你的 PCL Sharp 版本太老了，请更新之后再联机！");
            }

            if (!TryFromBase(fixedCode.Substring(20, 3), 16, out discoverNodeId))
            {
                return Fail("邀请码节点段无效！");
            }
        }

        return new LinkInviteParseResult(
            true,
            new LinkInviteInfo(port, fixedCode.Substring(0, 11), fixedCode.Substring(12, 5), version, discoverNodeId),
            "邀请码有效");
    }

    public string BuildInviteCode(LinkInviteInfo invite)
    {
        return $"{invite.NetworkName}-{invite.NetworkSecret}-{invite.Version.ToString().PadLeft(2, '0')}{Math.Max(0, invite.DiscoverNodeId).ToString("X3")}";
    }

    public string BuildShareText(LinkInviteInfo invite)
    {
        return $"在 PCL Sharp 启动器中输入邀请码【{BuildInviteCode(invite)}】，即可加入联机房间！";
    }

    private static LinkInviteParseResult Fail(string message)
    {
        return new LinkInviteParseResult(false, null, message);
    }

    private static string FixCodeFormat(string code)
    {
        var result = ExtractBetween(code.Trim(), '【', '】');
        result = ExtractBetween(result, '[', ']');
        result = result.ToUpperInvariant().Replace('O', '0').Replace('I', '1');
        if (result.Length >= 17 && (result.Length < 23 || result[17] != '-'))
        {
            result = result.Substring(0, 17) + "-0105E";
        }

        return result;
    }

    private static string ExtractBetween(string text, char left, char right)
    {
        var start = text.IndexOf(left);
        var end = text.LastIndexOf(right);
        return start >= 0 && end > start ? text.Substring(start + 1, end - start - 1) : text;
    }

    private static string GenerateToken(int length)
    {
        const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var builder = new StringBuilder(length);
        foreach (var value in bytes)
        {
            builder.Append(alphabet[value % alphabet.Length]);
        }

        return builder.ToString();
    }

    private static bool TryFromBase(string value, int fromBase, out int result)
    {
        try
        {
            result = Convert.ToInt32(value, fromBase);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            result = 0;
            return false;
        }
    }
}
