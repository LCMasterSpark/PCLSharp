using System.Security.Cryptography;
using System.Text;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LegacyLoginService : ILegacyLoginService
{
    public LoginSession CreateSession(string userName)
    {
        return CreateSession(userName, skinType: 0, skinSlim: false, skinName: "");
    }

    public LoginSession CreateSession(string userName, int skinType, bool skinSlim, string skinName, string skinUuid = "")
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("玩家名不能为空！", nameof(userName));
        }

        var normalized = userName.Trim();
        if (normalized.Contains('"'))
        {
            throw new ArgumentException("玩家名不能包含英文引号！", nameof(userName));
        }

        if (normalized.Length < 3 || normalized.Length > 16)
        {
            throw new ArgumentException("玩家名长度必须为 3 到 16 个字符！", nameof(userName));
        }

        return new LoginSession(
            LoginType.Legacy,
            normalized,
            CreateOfflineUuidWithSkin(normalized, skinType, skinSlim, skinName, skinUuid),
            "0",
            "00000000-0000-0000-0000-000000000000");
    }

    public void SaveHistory(string userName, IAppSettingsService settings)
    {
        var normalized = userName.Trim().Replace("¨", "", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var names = settings.Get(AppSettingKeys.LoginLegacyName, "")
            .Split('¨', StringSplitOptions.RemoveEmptyEntries)
            .Where(name => !string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
            .Prepend(normalized)
            .Take(20);
        settings.Set(AppSettingKeys.LoginLegacyName, string.Join('¨', names));
    }

    private static string CreateOfflineUuid(string userName)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{userName}"));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString("N");
    }

    private static string CreateOfflineUuidWithSkin(string userName, int skinType, bool skinSlim, string skinName, string skinUuid)
    {
        var uuid = CreateOfflineUuid(userName);
        return skinType switch
        {
            1 => AdjustUuidForSkinModel(uuid, "Steve"),
            2 => AdjustUuidForSkinModel(uuid, "Alex"),
            3 => ResolveSkinUuid(skinName, skinUuid, uuid),
            4 => AdjustUuidForSkinModel(uuid, skinSlim ? "Alex" : "Steve"),
            _ => uuid
        };
    }

    private static string ResolveSkinUuid(string skinName, string skinUuid, string fallbackUuid)
    {
        if (IsValidUuid(skinUuid))
        {
            return skinUuid.ToLowerInvariant();
        }

        var normalizedSkinName = skinName.Trim();
        return string.IsNullOrWhiteSpace(normalizedSkinName) ? fallbackUuid : CreateOfflineUuid(normalizedSkinName);
    }

    private static bool IsValidUuid(string uuid)
    {
        return uuid.Length == 32 && uuid.All(Uri.IsHexDigit);
    }

    private static string AdjustUuidForSkinModel(string uuid, string targetModel)
    {
        var current = uuid.ToUpperInvariant();
        for (var i = 0; i <= 0xfffff; i++)
        {
            if (string.Equals(GetSkinModel(current), targetModel, StringComparison.OrdinalIgnoreCase))
            {
                return current.ToLowerInvariant();
            }

            var prefix = current[..27];
            var suffix = current[27..];
            if (string.Equals(suffix, "FFFFF", StringComparison.OrdinalIgnoreCase))
            {
                current = prefix + "00000";
                continue;
            }

            current = prefix + (Convert.ToInt64(suffix, 16) + 1).ToString("X5");
        }

        return uuid;
    }

    private static string GetSkinModel(string uuid)
    {
        if (uuid.Length != 32)
        {
            return "Steve";
        }

        var a = Convert.ToInt32(uuid[7].ToString(), 16);
        var b = Convert.ToInt32(uuid[15].ToString(), 16);
        var c = Convert.ToInt32(uuid[23].ToString(), 16);
        var d = Convert.ToInt32(uuid[31].ToString(), 16);
        return ((a ^ b ^ c ^ d) % 2) == 1 ? "Alex" : "Steve";
    }
}
