using System.Net.Http;
using System.Text.Json;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class MicrosoftLoginService(
    ILaunchHttpClient http,
    IAppSettingsService settings,
    IMicrosoftDeviceCodePresenter? deviceCodePresenter = null) : IMicrosoftLoginService
{
    public async Task<LoginSession> LoginAsync(CancellationToken cancellationToken = default, bool forceNewLogin = false)
    {
        var refreshToken = settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, "");
        var accessToken = settings.Get(AppSettingKeys.CacheMsV2Access, "");
        var expires = settings.Get<long>(AppSettingKeys.CacheMsV2Expires, 0);
        var cachedProfile = GetCachedProfile();
        if (!forceNewLogin
            && !string.IsNullOrWhiteSpace(accessToken)
            && expires > DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
            && !string.IsNullOrWhiteSpace(cachedProfile.Uuid))
        {
            NormalizeCachedProfile(cachedProfile);
            UpsertCachedAccount(new MicrosoftAccountCacheEntry(
                cachedProfile.Uuid,
                cachedProfile.Name,
                refreshToken,
                accessToken,
                expires,
                cachedProfile.ProfileJson,
                DateTimeOffset.UtcNow));
            await settings.SaveAsync(cancellationToken).ConfigureAwait(false);
            return new LoginSession(
                LoginType.Ms,
                cachedProfile.Name,
                cachedProfile.Uuid,
                accessToken,
                cachedProfile.Uuid,
                cachedProfile.ProfileJson);
        }

        var clientId = GetClientId();
        var oauth = forceNewLogin
            ? await LoginWithDeviceCodeAsync(clientId, cancellationToken).ConfigureAwait(false)
            : await GetOAuthAsync(clientId, refreshToken, cancellationToken).ConfigureAwait(false);
        var xblToken = await AuthenticateXboxLiveAsync(oauth.AccessToken, cancellationToken).ConfigureAwait(false);
        var xsts = await AuthorizeXstsAsync(xblToken, cancellationToken).ConfigureAwait(false);
        var minecraft = await LoginMinecraftAsync(xsts, cancellationToken).ConfigureAwait(false);
        await CheckEntitlementAsync(minecraft.AccessToken, cancellationToken).ConfigureAwait(false);
        var profile = await GetProfileAsync(minecraft.AccessToken, cancellationToken).ConfigureAwait(false);

        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, oauth.RefreshToken);
        settings.Set(AppSettingKeys.CacheMsV2Access, minecraft.AccessToken);
        settings.Set(AppSettingKeys.CacheMsV2Expires, minecraft.ExpiresAt);
        settings.Set(AppSettingKeys.CacheMsV2Uuid, profile.Uuid);
        settings.Set(AppSettingKeys.CacheMsV2Name, profile.Name);
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, profile.ProfileJson);
        UpsertCachedAccount(new MicrosoftAccountCacheEntry(
            profile.Uuid,
            profile.Name,
            oauth.RefreshToken,
            minecraft.AccessToken,
            minecraft.ExpiresAt,
            profile.ProfileJson,
            DateTimeOffset.UtcNow));
        await settings.SaveAsync(cancellationToken).ConfigureAwait(false);

        return new LoginSession(LoginType.Ms, profile.Name, profile.Uuid, minecraft.AccessToken, profile.Uuid, profile.ProfileJson);
    }

    private (string Uuid, string Name, string ProfileJson) GetCachedProfile()
    {
        var profileJson = settings.Get(AppSettingKeys.CacheMsV2ProfileJson, "");
        var uuid = settings.Get(AppSettingKeys.CacheMsV2Uuid, "");
        var name = settings.Get(AppSettingKeys.CacheMsV2Name, "");
        if ((string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
            && !string.IsNullOrWhiteSpace(profileJson))
        {
            try
            {
                using var json = JsonDocument.Parse(profileJson);
                if (string.IsNullOrWhiteSpace(uuid)
                    && json.RootElement.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String)
                {
                    uuid = id.GetString() ?? "";
                }

                if (string.IsNullOrWhiteSpace(name)
                    && json.RootElement.TryGetProperty("name", out var profileName)
                    && profileName.ValueKind == JsonValueKind.String)
                {
                    name = profileName.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
                // 损坏的缓存不应阻断后续刷新或设备码登录。
            }
        }

        return (uuid, name, profileJson);
    }

    private void NormalizeCachedProfile((string Uuid, string Name, string ProfileJson) profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Uuid))
        {
            settings.Set(AppSettingKeys.CacheMsV2Uuid, profile.Uuid);
        }

        if (!string.IsNullOrWhiteSpace(profile.Name))
        {
            settings.Set(AppSettingKeys.CacheMsV2Name, profile.Name);
        }
    }

    private void UpsertCachedAccount(MicrosoftAccountCacheEntry account)
    {
        if (string.IsNullOrWhiteSpace(account.Uuid) && string.IsNullOrWhiteSpace(account.Name))
        {
            return;
        }

        var accounts = ReadCachedAccounts()
            .Where(item => !IsSameAccount(item, account))
            .Prepend(account)
            .OrderByDescending(item => item.LastUsedAt)
            .Take(10)
            .ToArray();
        settings.Set(AppSettingKeys.CacheMsV2AccountsJson, JsonSerializer.Serialize(accounts));
        UpsertLegacyLoginMsJson(account);
    }

    private IReadOnlyList<MicrosoftAccountCacheEntry> ReadCachedAccounts()
    {
        var accounts = new List<MicrosoftAccountCacheEntry>();
        var json = settings.Get(AppSettingKeys.CacheMsV2AccountsJson, "");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                accounts.AddRange(JsonSerializer.Deserialize<MicrosoftAccountCacheEntry[]>(json) ?? []);
            }
            catch (JsonException)
            {
            }
        }

        accounts.AddRange(ReadLegacyLoginMsJson());
        return accounts
            .Where(HasAccountData)
            .GroupBy(account => string.IsNullOrWhiteSpace(account.Uuid) ? account.Name : account.Uuid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(account => account.LastUsedAt).First())
            .OrderByDescending(account => account.LastUsedAt)
            .Take(10)
            .ToArray();
    }

    private IReadOnlyList<MicrosoftAccountCacheEntry> ReadLegacyLoginMsJson()
    {
        var json = settings.Get(AppSettingKeys.LoginMsJson, "{}");
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var accounts = new List<MicrosoftAccountCacheEntry>();
            var index = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = property.Name.Trim();
                var refreshToken = property.Value.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(refreshToken))
                {
                    continue;
                }

                accounts.Add(new MicrosoftAccountCacheEntry(
                    "",
                    name,
                    refreshToken,
                    "",
                    0,
                    "",
                    DateTimeOffset.UtcNow.AddSeconds(-index++)));
            }

            return accounts;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void UpsertLegacyLoginMsJson(MicrosoftAccountCacheEntry account)
    {
        if (string.IsNullOrWhiteSpace(account.Name) || string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            return;
        }

        var legacy = ReadLegacyLoginMsJson()
            .Where(item => !string.Equals(item.Name, account.Name, StringComparison.OrdinalIgnoreCase))
            .Select(item => new KeyValuePair<string, string>(item.Name, item.RefreshToken))
            .Prepend(new KeyValuePair<string, string>(account.Name, account.RefreshToken))
            .Take(10);
        settings.Set(AppSettingKeys.LoginMsJson, JsonSerializer.Serialize(legacy.ToDictionary(item => item.Key, item => item.Value)));
    }

    private static bool IsSameAccount(MicrosoftAccountCacheEntry left, MicrosoftAccountCacheEntry right)
    {
        return (!string.IsNullOrWhiteSpace(left.Uuid)
                && !string.IsNullOrWhiteSpace(right.Uuid)
                && string.Equals(left.Uuid, right.Uuid, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(left.Name)
                && !string.IsNullOrWhiteSpace(right.Name)
                && string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAccountData(MicrosoftAccountCacheEntry account)
    {
        return !string.IsNullOrWhiteSpace(account.Uuid)
            || !string.IsNullOrWhiteSpace(account.Name);
    }

    private async Task<(string AccessToken, string RefreshToken)> GetOAuthAsync(string clientId, string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            var cachedAccountOAuth = await TryRefreshCachedAccountsAsync(clientId, "", cancellationToken).ConfigureAwait(false);
            return cachedAccountOAuth
                ?? await LoginWithDeviceCodeAsync(clientId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await RefreshOAuthAsync(clientId, refreshToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            var cachedAccountOAuth = await TryRefreshCachedAccountsAsync(clientId, refreshToken, cancellationToken).ConfigureAwait(false);
            if (cachedAccountOAuth is not null)
            {
                return cachedAccountOAuth.Value;
            }

            ClearCachedMicrosoftAccount();
            await settings.SaveAsync(cancellationToken).ConfigureAwait(false);
            return await LoginWithDeviceCodeAsync(clientId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(string AccessToken, string RefreshToken)?> TryRefreshCachedAccountsAsync(string clientId, string failedRefreshToken, CancellationToken cancellationToken)
    {
        var triedTokens = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(failedRefreshToken))
        {
            triedTokens.Add(failedRefreshToken);
        }

        foreach (var account in ReadCachedAccounts().OrderByDescending(account => account.LastUsedAt))
        {
            if (string.IsNullOrWhiteSpace(account.RefreshToken) || !triedTokens.Add(account.RefreshToken))
            {
                continue;
            }

            try
            {
                return await RefreshOAuthAsync(clientId, account.RefreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
            {
                // Continue trying older Microsoft accounts before asking the user to sign in again.
            }
        }

        return null;
    }

    private void ClearCachedMicrosoftAccount()
    {
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "");
        settings.Set(AppSettingKeys.CacheMsV2Access, "");
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, "");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "");
        settings.Set(AppSettingKeys.CacheMsV2Name, "");
        settings.Set(AppSettingKeys.CacheMsV2Expires, 0L);
    }

    private string GetClientId()
    {
        var clientId = settings.Get(AppSettingKeys.MicrosoftClientId, "").Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            clientId = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID")?.Trim() ?? "";
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("尚未配置微软登录 Client ID。请在启动页的正版登录区域填写 Microsoft Client ID，或设置环境变量 PCL_MS_CLIENT_ID。");
        }

        return clientId;
    }

    private async Task<(string AccessToken, string RefreshToken)> LoginWithDeviceCodeAsync(string clientId, CancellationToken cancellationToken)
    {
        var info = await RequestDeviceCodeAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (deviceCodePresenter is null)
        {
            throw new InvalidOperationException($"微软账号尚未登录，请打开 {info.VerificationUri} 并输入代码 {info.UserCode}。");
        }

        await deviceCodePresenter.ShowAsync(info, cancellationToken).ConfigureAwait(false);
        try
        {
            return await PollDeviceCodeAsync(clientId, info, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (deviceCodePresenter is IMicrosoftDeviceCodeStatusService status)
            {
                status.Clear();
            }
        }
    }

    private async Task<MicrosoftDeviceCodeInfo> RequestDeviceCodeAsync(string clientId, CancellationToken cancellationToken)
    {
        var text = await http.SendAsync(new LaunchHttpRequest(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
            HttpMethod.Post,
            $"client_id={Uri.EscapeDataString(clientId)}&tenant=/consumers&scope=XboxLive.signin%20offline_access",
            "application/x-www-form-urlencoded"), cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        return new MicrosoftDeviceCodeInfo(
            GetRequired(root, "user_code"),
            GetRequired(root, "device_code"),
            GetRequired(root, "verification_uri"),
            root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 900,
            root.TryGetProperty("interval", out var interval) ? Math.Max(1, interval.GetInt32()) : 5,
            root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() ?? "" : "");
    }

    private async Task<(string AccessToken, string RefreshToken)> PollDeviceCodeAsync(string clientId, MicrosoftDeviceCodeInfo info, CancellationToken cancellationToken)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, info.ExpiresInSeconds));
        var intervalSeconds = Math.Max(1, info.IntervalSeconds);
        var pollCount = 0;
        var status = deviceCodePresenter as IMicrosoftDeviceCodeStatusService;
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pollCount++;
            status?.UpdateStatus($"正在等待微软网页登录授权，第 {pollCount} 次检查中...");
            var text = await SendDeviceCodeTokenAsync(new LaunchHttpRequest(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                HttpMethod.Post,
                "grant_type=urn:ietf:params:oauth:grant-type:device_code&" +
                $"client_id={Uri.EscapeDataString(clientId)}&" +
                $"device_code={Uri.EscapeDataString(info.DeviceCode)}&" +
                "scope=XboxLive.signin%20offline_access",
                "application/x-www-form-urlencoded"), cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(text);
            if (!json.RootElement.TryGetProperty("error", out var error))
            {
                status?.UpdateStatus("微软网页登录授权完成，正在获取 Minecraft 档案...");
                return (GetRequired(json.RootElement, "access_token"), GetRequired(json.RootElement, "refresh_token"));
            }

            var errorCode = error.GetString() ?? "";
            var errorDescription = json.RootElement.TryGetProperty("error_description", out var description)
                ? description.GetString() ?? ""
                : "";
            if (string.Equals(errorCode, "authorization_pending", StringComparison.OrdinalIgnoreCase))
            {
                status?.UpdateStatus($"尚未完成网页登录授权，{intervalSeconds} 秒后继续检查。");
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (string.Equals(errorCode, "slow_down", StringComparison.OrdinalIgnoreCase))
            {
                status?.UpdateStatus($"微软要求放慢检查频率，{intervalSeconds} 秒后继续等待。");
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken).ConfigureAwait(false);
                intervalSeconds += 5;
                continue;
            }

            throw new InvalidOperationException(MapDeviceCodeError(errorCode, errorDescription));
        }

        throw new InvalidOperationException("登录用时太长，请重新尝试。");
    }

    private async Task<string> SendDeviceCodeTokenAsync(LaunchHttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (LooksLikeJsonObject(GetHttpErrorText(ex)))
        {
            return GetHttpErrorText(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("微软设备码登录失败：" + GetHttpErrorText(ex), ex);
        }
    }

    private static string MapDeviceCodeError(string errorCode, string errorDescription)
    {
        var text = string.IsNullOrWhiteSpace(errorDescription) ? errorCode : errorCode + " " + errorDescription;
        if (text.Contains("Account security interrupt", StringComparison.OrdinalIgnoreCase))
        {
            return "该账号因安全问题无法登录，请前往微软账号页面查看并处理。";
        }

        if (text.Contains("service abuse", StringComparison.OrdinalIgnoreCase))
        {
            return "该账号可能被微软封禁或限制，暂时无法登录。";
        }

        return errorCode switch
        {
            "authorization_declined" => "你拒绝了 PCL Sharp 请求的登录权限。",
            "expired_token" => "登录用时太长，请重新尝试。",
            "bad_verification_code" => "网页登录代码无效，请重新登录。",
            _ when text.Contains("AADSTS70000", StringComparison.OrdinalIgnoreCase) => "微软登录状态已失效，请重新登录。",
            _ => "微软设备码登录失败：" + text
        };
    }

    private async Task<(string AccessToken, string RefreshToken)> RefreshOAuthAsync(string clientId, string refreshToken, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await http.SendAsync(new LaunchHttpRequest(
                "https://login.live.com/oauth20_token.srf",
                HttpMethod.Post,
                $"client_id={Uri.EscapeDataString(clientId)}&refresh_token={Uri.EscapeDataString(refreshToken)}&grant_type=refresh_token&scope=XboxLive.signin%20offline_access",
                "application/x-www-form-urlencoded",
                Headers: new Dictionary<string, string>
                {
                    ["Accept-Language"] = "en-US,en;q=0.5",
                    ["X-Requested-With"] = "XMLHttpRequest"
                }), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(MapOAuthRefreshError(GetHttpErrorText(ex)), ex);
        }

        using var json = JsonDocument.Parse(text);
        if (json.RootElement.TryGetProperty("error", out var error))
        {
            var description = json.RootElement.TryGetProperty("error_description", out var descriptionElement)
                ? descriptionElement.GetString() ?? ""
                : "";
            throw new InvalidOperationException(MapOAuthRefreshError(error.GetString() + " " + description));
        }

        return (GetRequired(json.RootElement, "access_token"), GetRequired(json.RootElement, "refresh_token"));
    }

    private static string MapOAuthRefreshError(string text)
    {
        if (text.Contains("must sign in again", StringComparison.OrdinalIgnoreCase)
            || text.Contains("password expired", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("refresh_token", StringComparison.OrdinalIgnoreCase)
                && text.Contains("is not valid", StringComparison.OrdinalIgnoreCase))
            || text.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || text.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AADSTS70000", StringComparison.OrdinalIgnoreCase))
        {
            return "微软登录状态已失效，需要重新打开网页登录。";
        }

        if (text.Contains("Account security interrupt", StringComparison.OrdinalIgnoreCase))
        {
            return "该账号因安全问题无法登录，请前往微软账号页面查看并处理。";
        }

        if (text.Contains("service abuse", StringComparison.OrdinalIgnoreCase))
        {
            return "该账号可能被微软封禁或限制，暂时无法登录。";
        }

        return "微软 OAuth 刷新登录失败：" + text;
    }

    private async Task<string> AuthenticateXboxLiveAsync(string accessToken, CancellationToken cancellationToken)
    {
        var rpsTicket = accessToken.StartsWith("d=", StringComparison.OrdinalIgnoreCase)
            ? accessToken
            : "d=" + accessToken;
        var body = $$"""
        {
          "Properties": {
            "AuthMethod": "RPS",
            "SiteName": "user.auth.xboxlive.com",
            "RpsTicket": "{{rpsTicket}}"
          },
          "RelyingParty": "http://auth.xboxlive.com",
          "TokenType": "JWT"
        }
        """;
        var text = await http.SendAsync(new LaunchHttpRequest("https://user.auth.xboxlive.com/user/authenticate", HttpMethod.Post, body), cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(text);
        return GetRequired(json.RootElement, "Token");
    }

    private async Task<(string Token, string UserHash)> AuthorizeXstsAsync(string xblToken, CancellationToken cancellationToken)
    {
        var body = $$"""
        {
          "Properties": {
            "SandboxId": "RETAIL",
            "UserTokens": ["{{xblToken}}"]
          },
          "RelyingParty": "rp://api.minecraftservices.com/",
          "TokenType": "JWT"
        }
        """;
        string text;
        try
        {
            text = await http.SendAsync(new LaunchHttpRequest("https://xsts.auth.xboxlive.com/xsts/authorize", HttpMethod.Post, body), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(MapXstsError(GetHttpErrorText(ex)), ex);
        }

        using var json = JsonDocument.Parse(text);
        if (json.RootElement.TryGetProperty("XErr", out var xerr))
        {
            throw new InvalidOperationException(MapXstsError(xerr.ToString()));
        }

        var userHash = json.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString() ?? "";
        return (GetRequired(json.RootElement, "Token"), userHash);
    }

    private static string MapXstsError(string text)
    {
        return text switch
        {
            var value when value.Contains("2148916227", StringComparison.OrdinalIgnoreCase) => "该微软账号似乎已被封禁，无法登录。",
            var value when value.Contains("2148916233", StringComparison.OrdinalIgnoreCase) => "该微软账号尚未创建 Xbox 档案，请先在 Xbox 官网完成档案初始化后再登录。",
            var value when value.Contains("2148916235", StringComparison.OrdinalIgnoreCase) => "该微软账号所在地区暂不支持 Xbox Live，无法完成正版登录。",
            var value when value.Contains("2148916236", StringComparison.OrdinalIgnoreCase) => "该微软账号需要完成年龄或身份验证后才能登录 Xbox Live。",
            var value when value.Contains("2148916237", StringComparison.OrdinalIgnoreCase) => "该微软账号需要完成年龄或身份验证后才能登录 Xbox Live。",
            var value when value.Contains("2148916238", StringComparison.OrdinalIgnoreCase) => "该微软账号是儿童账号，请由家庭组管理员允许其使用 Xbox Live 后再登录。",
            _ => "Xbox Live 身份验证失败：" + text
        };
    }

    private async Task<(string AccessToken, long ExpiresAt)> LoginMinecraftAsync((string Token, string UserHash) xsts, CancellationToken cancellationToken)
    {
        var body = $$"""
        {
          "identityToken": "XBL3.0 x={{xsts.UserHash}};{{xsts.Token}}"
        }
        """;
        string text;
        try
        {
            text = await http.SendAsync(new LaunchHttpRequest("https://api.minecraftservices.com/authentication/login_with_xbox", HttpMethod.Post, body), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            if (GetHttpErrorText(ex).Contains("ACCOUNT_SUSPENDED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("该微软账号可能已被封禁，无法登录。", ex);
            }

            if (GetStatusCode(ex) == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new InvalidOperationException("登录尝试太过频繁，请等待几分钟后再试。", ex);
            }

            if (GetStatusCode(ex) == System.Net.HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException("当前 IP 的登录尝试异常。如果你使用了 VPN 或加速器，请关闭或更换节点后再试。", ex);
            }

            throw new InvalidOperationException(MapMinecraftServicesError("Minecraft Services 登录失败", ex), ex);
        }

        using var json = JsonDocument.Parse(text);
        var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expiresElement) ? expiresElement.GetInt64() : 86400;
        return (GetRequired(json.RootElement, "access_token"), DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn - 1200)).ToUnixTimeSeconds());
    }

    private async Task CheckEntitlementAsync(string accessToken, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await http.SendAsync(new LaunchHttpRequest(
                "https://api.minecraftservices.com/entitlements/mcstore",
                HttpMethod.Get,
                Headers: new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken }), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(MapMinecraftServicesError("检查 Minecraft Java Edition 资格失败", ex), ex);
        }

        using var json = JsonDocument.Parse(text);
        if (!json.RootElement.TryGetProperty("items", out var items) || !HasMinecraftJavaEntitlement(items))
        {
            throw new InvalidOperationException("该微软账号没有 Minecraft Java Edition。");
        }
    }

    private static bool HasMinecraftJavaEntitlement(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("name", out var nameElement)
                || nameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = nameElement.GetString();
            if (string.Equals(name, "game_minecraft", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "product_minecraft", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<(string Uuid, string Name, string ProfileJson)> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await http.SendAsync(new LaunchHttpRequest(
                "https://api.minecraftservices.com/minecraft/profile",
                HttpMethod.Get,
                Headers: new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken }), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            if (GetStatusCode(ex) == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new InvalidOperationException("登录尝试太过频繁，请等待几分钟后再试。", ex);
            }

            if (GetStatusCode(ex) == System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException("请先创建 Minecraft 玩家档案，然后再重新登录。", ex);
            }

            throw new InvalidOperationException(MapMinecraftServicesError("获取 Minecraft 档案失败", ex), ex);
        }

        using var json = JsonDocument.Parse(text);
        return (GetRequired(json.RootElement, "id"), GetRequired(json.RootElement, "name"), text);
    }

    private static string MapMinecraftServicesError(string prefix, HttpRequestException ex)
    {
        var text = GetHttpErrorText(ex);
        if (text.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
        {
            return "微软登录 Client ID 无效或未启用公共客户端流程，请检查启动页中的 Microsoft Client ID。";
        }

        if (text.Contains("ForbiddenOperationException", StringComparison.OrdinalIgnoreCase)
            || text.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase)
            || text.Contains("NOT_FOUND_PROFILE", StringComparison.OrdinalIgnoreCase))
        {
            return "该微软账号没有可用的 Minecraft Java Edition 档案。";
        }

        if (text.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Invalid token", StringComparison.OrdinalIgnoreCase)
            || text.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return "Minecraft Services 登录凭据已失效，请重新登录正版账号。";
        }

        return prefix + "：" + text;
    }

    private static string GetHttpErrorText(HttpRequestException ex)
    {
        return ex is LaunchHttpException launchHttp && !string.IsNullOrWhiteSpace(launchHttp.ResponseBody)
            ? launchHttp.ResponseBody
            : ex.Message;
    }

    private static System.Net.HttpStatusCode? GetStatusCode(HttpRequestException ex)
    {
        return ex is LaunchHttpException launchHttp
            ? launchHttp.StatusCodeValue
            : ex.StatusCode;
    }

    private static bool LooksLikeJsonObject(string text)
    {
        return text.TrimStart().StartsWith('{');
    }

    private static string GetRequired(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : throw new InvalidOperationException($"登录响应缺少 {propertyName}");
    }
}
