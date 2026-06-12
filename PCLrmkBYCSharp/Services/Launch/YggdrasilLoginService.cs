using System.Text.Json;
using System.Net.Http;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class YggdrasilLoginService(
    ILaunchHttpClient http,
    IAppSettingsService settings,
    IYggdrasilProfileSelector? profileSelector = null) : IYggdrasilLoginService
{
    public async Task<LoginSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Type is not (LoginType.Nide or LoginType.Auth))
        {
            throw new ArgumentException("Yggdrasil 登录仅支持 Nide 与 Auth。", nameof(request));
        }

        ValidateInput(request);
        var token = request.Type == LoginType.Nide ? "Nide" : "Auth";
        var baseUrl = BuildBaseUrl(request);
        var cachedAccess = settings.Get("Cache" + token + "Access", "");
        var cachedClient = settings.Get("Cache" + token + "Client", "");
        if (!string.IsNullOrWhiteSpace(cachedAccess) && !string.IsNullOrWhiteSpace(cachedClient))
        {
            if (await ValidateAsync(baseUrl, cachedAccess, cachedClient, cancellationToken).ConfigureAwait(false))
            {
                return await AttachAuthlibPrefetchAsync(request, CachedSession(token, request.Type), cancellationToken).ConfigureAwait(false);
            }

            var refreshed = await TryRefreshAsync(baseUrl, token, cachedAccess, cachedClient, cancellationToken).ConfigureAwait(false);
            if (refreshed is not null)
            {
                return await AttachAuthlibPrefetchAsync(request, refreshed, cancellationToken).ConfigureAwait(false);
            }
        }

        var session = await AuthenticateAsync(baseUrl, token, request, cancellationToken).ConfigureAwait(false);
        SaveAccountHistory(token, request);
        await settings.SaveAsync(cancellationToken).ConfigureAwait(false);
        return await AttachAuthlibPrefetchAsync(request, session, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateInput(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            throw new ArgumentException("账号不能为空！");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("密码不能为空！");
        }

        if (request.Type == LoginType.Auth && string.IsNullOrWhiteSpace(request.Server))
        {
            throw new ArgumentException("Authlib-Injector 服务器不能为空！");
        }
    }

    private static string BuildBaseUrl(LoginRequest request)
    {
        if (request.Type == LoginType.Nide)
        {
            var server = string.IsNullOrWhiteSpace(request.Server) ? "" : request.Server.Trim().Trim('/');
            return $"https://auth.mc-user.com:233/{server}/authserver";
        }

        return request.Server.Trim().TrimEnd('/') + "/authserver";
    }

    private async Task<bool> ValidateAsync(string baseUrl, string accessToken, string clientToken, CancellationToken cancellationToken)
    {
        try
        {
            await http.SendAsync(new LaunchHttpRequest(
                baseUrl + "/validate",
                HttpMethod.Post,
                $$"""{"accessToken":"{{accessToken}}","clientToken":"{{clientToken}}"}"""), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<LoginSession?> TryRefreshAsync(string baseUrl, string token, string accessToken, string clientToken, CancellationToken cancellationToken)
    {
        try
        {
            var text = await http.SendAsync(new LaunchHttpRequest(
                baseUrl + "/refresh",
                HttpMethod.Post,
                $$"""{"accessToken":"{{accessToken}}","clientToken":"{{clientToken}}","requestUser":true}"""), cancellationToken).ConfigureAwait(false);
            return await SaveYggdrasilResponseAsync(token, token == "Nide" ? LoginType.Nide : LoginType.Auth, text, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<LoginSession> AuthenticateAsync(string baseUrl, string token, LoginRequest request, CancellationToken cancellationToken)
    {
        var body = $$"""
        {
          "agent": { "name": "Minecraft", "version": 1 },
          "username": "{{EscapeJson(request.UserName)}}",
          "password": "{{EscapeJson(request.Password)}}",
          "requestUser": true
        }
        """;
        var text = await http.SendAsync(new LaunchHttpRequest(baseUrl + "/authenticate", HttpMethod.Post, body), cancellationToken).ConfigureAwait(false);
        return await SaveYggdrasilResponseAsync(token, request.Type, text, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LoginSession> AttachAuthlibPrefetchAsync(LoginRequest request, LoginSession session, CancellationToken cancellationToken)
    {
        if (request.Type != LoginType.Auth)
        {
            return session;
        }

        var server = request.Server.Trim().TrimEnd('/');
        try
        {
            var metadata = await http.SendAsync(new LaunchHttpRequest(server, HttpMethod.Get), cancellationToken).ConfigureAwait(false);
            return session with { AuthlibInjectorMetadata = metadata };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"无法连接到第三方登录服务器（{server}）", ex);
        }
    }

    private async Task<LoginSession> SaveYggdrasilResponseAsync(string token, LoginType type, string text, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var selected = await ResolveSelectedProfileAsync(token, type, root, cancellationToken).ConfigureAwait(false);
        var accessToken = GetRequired(root, "accessToken");
        var clientToken = GetRequired(root, "clientToken");
        var uuid = selected.Uuid;
        var name = selected.Name;
        settings.Set("Cache" + token + "Access", accessToken);
        settings.Set("Cache" + token + "Client", clientToken);
        settings.Set("Cache" + token + "Uuid", uuid);
        settings.Set("Cache" + token + "Name", name);
        return new LoginSession(type, name, uuid, accessToken, clientToken);
    }

    private async Task<YggdrasilProfileOption> ResolveSelectedProfileAsync(string token, LoginType type, JsonElement root, CancellationToken cancellationToken)
    {
        if (root.TryGetProperty("selectedProfile", out var selected)
            && selected.ValueKind == JsonValueKind.Object)
        {
            return new YggdrasilProfileOption(GetRequired(selected, "id"), GetRequired(selected, "name"));
        }

        if (!root.TryGetProperty("availableProfiles", out var profiles)
            || profiles.ValueKind != JsonValueKind.Array
            || profiles.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("你还没有创建角色，请在创建角色后再试！");
        }

        if (profiles.GetArrayLength() == 1)
        {
            var profile = profiles.EnumerateArray().First();
            return new YggdrasilProfileOption(GetRequired(profile, "id"), GetRequired(profile, "name"));
        }

        var cachedName = settings.Get("Cache" + token + "Name", "");
        var options = profiles.EnumerateArray()
            .Select(profile => new YggdrasilProfileOption(GetRequired(profile, "id"), GetRequired(profile, "name")))
            .ToList();
        if (!string.IsNullOrWhiteSpace(cachedName))
        {
            foreach (var profile in options)
            {
                if (string.Equals(profile.Name, cachedName, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }
        }

        if (profileSelector is not null)
        {
            var title = type == LoginType.Nide ? "选择统一通行证角色" : "选择 Authlib-Injector 角色";
            var chosen = await profileSelector.SelectAsync(title, options, cachedName, cancellationToken).ConfigureAwait(false);
            if (chosen is not null)
            {
                return chosen;
            }
        }

        throw new InvalidOperationException("该账号包含多个角色，请在登录页选择要使用的角色。");
    }

    private LoginSession CachedSession(string token, LoginType type)
    {
        return new LoginSession(
            type,
            settings.Get("Cache" + token + "Name", ""),
            settings.Get("Cache" + token + "Uuid", ""),
            settings.Get("Cache" + token + "Access", ""),
            settings.Get("Cache" + token + "Client", ""));
    }

    private void SaveAccountHistory(string token, LoginRequest request)
    {
        settings.Set("Cache" + token + "Username", request.UserName);
        settings.Set("Cache" + token + "Pass", request.Remember ? request.Password : "");
        if (token == "Nide")
        {
            settings.Set(AppSettingKeys.LoginNideEmail, MergeHistory(settings.Get(AppSettingKeys.LoginNideEmail, ""), request.UserName));
            settings.Set(AppSettingKeys.LoginNidePass, request.Remember ? MergeHistory(settings.Get(AppSettingKeys.LoginNidePass, ""), request.Password) : "");
            settings.Set(AppSettingKeys.CacheNideServer, request.Server);
        }
        else
        {
            settings.Set(AppSettingKeys.LoginAuthEmail, MergeHistory(settings.Get(AppSettingKeys.LoginAuthEmail, ""), request.UserName));
            settings.Set(AppSettingKeys.LoginAuthPass, request.Remember ? MergeHistory(settings.Get(AppSettingKeys.LoginAuthPass, ""), request.Password) : "");
            settings.Set(AppSettingKeys.CacheAuthServerServer, request.Server);
        }
    }

    private static string MergeHistory(string current, string value)
    {
        return string.Join('¨', current.Split('¨', StringSplitOptions.RemoveEmptyEntries)
            .Where(item => !string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            .Prepend(value)
            .Take(20));
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string GetRequired(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : throw new InvalidOperationException($"登录响应缺少 {propertyName}");
    }

    private static string GetOptional(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }
}
