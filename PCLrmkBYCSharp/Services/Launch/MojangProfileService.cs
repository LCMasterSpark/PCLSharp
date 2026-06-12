using System.Net.Http;
using System.Text.Json;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class MojangProfileService(
    ILaunchHttpClient http,
    IAppSettingsService settings,
    IAppLoggerService logger) : IMojangProfileService
{
    public async Task<string?> GetUuidAsync(string userName, CancellationToken cancellationToken = default)
    {
        var normalized = userName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var cacheKey = AppSettingKeys.CacheMojangUuidPrefix + normalized;
        var cached = settings.Get(cacheKey, "");
        if (IsValidUuid(cached))
        {
            return cached;
        }

        try
        {
            var json = await http.SendAsync(
                new LaunchHttpRequest(
                    "https://api.mojang.com/users/profiles/minecraft/" + Uri.EscapeDataString(normalized),
                    HttpMethod.Get),
                cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var uuid = document.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
            if (!IsValidUuid(uuid))
            {
                logger.Warn("Mojang 玩家档案返回了无效 UUID：" + normalized);
                return null;
            }

            settings.Set(cacheKey, uuid);
            return uuid;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase))
        {
            logger.Warn("Mojang 玩家档案不存在：" + normalized);
            return null;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "获取 Mojang 玩家 UUID 失败：" + normalized);
            return null;
        }
    }

    private static bool IsValidUuid(string uuid)
    {
        return uuid.Length == 32 && uuid.All(Uri.IsHexDigit);
    }
}
