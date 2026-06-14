using System.IO;
using System.IO.Compression;
using System.Text.Json;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.Launch;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Tests;

public sealed class LaunchStage5BTests
{
    [Theory]
    [InlineData("ab")]
    [InlineData("this_name_is_way_too_long")]
    [InlineData("bad\"name")]
    public void LegacyLoginUsesOldPclValidation(string name)
    {
        var service = new LegacyLoginService();

        Assert.Throws<ArgumentException>(() => service.CreateSession(name));
    }

    [Fact]
    public void LegacyLoginSavesHistoryWithOldSeparator()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var service = new LegacyLoginService();

        service.SaveHistory("Alex", settings);
        service.SaveHistory("Steve", settings);
        service.SaveHistory("Alex", settings);

        Assert.Equal("Alex¨Steve", settings.Get(AppSettingKeys.LoginLegacyName, ""));
    }

    [Fact]
    public void LaunchPageViewModelLocalizesWpfBindingAndCollectionErrors()
    {
        var collectionMessage = LaunchPageViewModel.ToUserFacingExceptionMessage(
            new InvalidOperationException("该类型的 CollectionView 不支持从调度程序线程以外的线程对其 SourceCollection 进行的更改。"));
        var bindingMessage = LaunchPageViewModel.ToUserFacingExceptionMessage(
            new InvalidOperationException("无法对只读属性进行 TwoWay 或 OneWayToSource 绑定。"));

        Assert.Contains("界面刷新线程冲突", collectionMessage);
        Assert.DoesNotContain("SourceCollection", collectionMessage);
        Assert.Contains("界面绑定方向异常", bindingMessage);
        Assert.DoesNotContain("TwoWay", bindingMessage);
    }

    [Fact]
    public async Task MicrosoftLoginFailsClearlyWithoutClientId()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var old = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "");
        try
        {
            var service = new MicrosoftLoginService(new FakeLaunchHttpClient(), settings);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

            Assert.Contains("尚未配置微软登录 Client ID", ex.Message);
            Assert.Contains("PCL_MS_CLIENT_ID", ex.Message);
            Assert.DoesNotContain("\u704F\u6C2D\u6E6D", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", old);
        }
    }

    [Fact]
    public async Task MicrosoftLoginUsesValidCachedSessionWithoutNetwork()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2Access, "cached-access");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "cached-uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "CachedAlex");
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, """{"id":"cached-uuid","name":"CachedAlex"}""");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var http = new FakeLaunchHttpClient();
        var presenter = new CaptureMicrosoftDeviceCodePresenter();
        var service = new MicrosoftLoginService(http, settings, presenter);

        var session = await service.LoginAsync();

        Assert.Equal(LoginType.Ms, session.Type);
        Assert.Equal("CachedAlex", session.UserName);
        Assert.Equal("cached-uuid", session.Uuid);
        Assert.Equal("cached-access", session.AccessToken);
        Assert.Empty(http.Requests);
        Assert.Null(presenter.LastInfo);
    }

    [Fact]
    public async Task MicrosoftLoginRestoresValidCachedSessionFromProfileJson()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.CacheMsV2Access, "cached-access");
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, """{"id":"json-uuid","name":"JsonAlex"}""");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var http = new FakeLaunchHttpClient();
        var presenter = new CaptureMicrosoftDeviceCodePresenter();
        var service = new MicrosoftLoginService(http, settings, presenter);

        var session = await service.LoginAsync();

        Assert.Equal(LoginType.Ms, session.Type);
        Assert.Equal("JsonAlex", session.UserName);
        Assert.Equal("json-uuid", session.Uuid);
        Assert.Equal("cached-access", session.AccessToken);
        Assert.Equal("json-uuid", settings.Get(AppSettingKeys.CacheMsV2Uuid, ""));
        Assert.Equal("JsonAlex", settings.Get(AppSettingKeys.CacheMsV2Name, ""));
        Assert.Empty(http.Requests);
        Assert.Null(presenter.LastInfo);
    }

    [Fact]
    public async Task MicrosoftLoginStoresValidCachedSessionInAccountHistory()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "cached-refresh");
        settings.Set(AppSettingKeys.CacheMsV2Access, "cached-access");
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, """{"id":"cached-uuid","name":"CachedAlex"}""");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var service = new MicrosoftLoginService(new FakeLaunchHttpClient(), settings, new CaptureMicrosoftDeviceCodePresenter());

        var session = await service.LoginAsync();
        var accounts = JsonSerializer.Deserialize<MicrosoftAccountCacheEntry[]>(settings.Get(AppSettingKeys.CacheMsV2AccountsJson, ""))!;

        Assert.Equal("CachedAlex", session.UserName);
        var account = Assert.Single(accounts);
        Assert.Equal("cached-uuid", account.Uuid);
        Assert.Equal("CachedAlex", account.Name);
        Assert.Equal("cached-refresh", account.RefreshToken);
        Assert.Equal("cached-access", account.AccessToken);
    }

    [Fact]
    public async Task MicrosoftLoginUsesValidCachedSessionWithoutClientId()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.CacheMsV2Access, "cached-access");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "cached-uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "CachedAlex");
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, """{"id":"cached-uuid","name":"CachedAlex"}""");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var old = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "");
        var http = new FakeLaunchHttpClient();
        var presenter = new CaptureMicrosoftDeviceCodePresenter();
        try
        {
            var service = new MicrosoftLoginService(http, settings, presenter);

            var session = await service.LoginAsync();

            Assert.Equal(LoginType.Ms, session.Type);
            Assert.Equal("CachedAlex", session.UserName);
            Assert.Equal("cached-uuid", session.Uuid);
            Assert.Equal("cached-access", session.AccessToken);
            Assert.Empty(http.Requests);
            Assert.Null(presenter.LastInfo);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", old);
        }
    }

    [Fact]
    public async Task MicrosoftDeviceCodeStatusServicePublishesAndClearsState()
    {
        var service = new MicrosoftDeviceCodeStatusService();
        var changed = 0;
        service.Changed += (_, _) => changed++;
        var info = new MicrosoftDeviceCodeInfo("ABCD-EFGH", "device", "https://microsoft.com/link", 900, 1, "CODE ABCD-EFGH");

        await service.ShowAsync(info);

        Assert.True(service.IsActive);
        Assert.Equal(info, service.Current);
        Assert.True(service.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal("CODE ABCD-EFGH", service.StatusMessage);
        Assert.Equal(1, changed);

        service.UpdateStatus("WAITING");

        Assert.Equal("WAITING", service.StatusMessage);
        Assert.Equal(2, changed);

        service.Clear();

        Assert.False(service.IsActive);
        Assert.Null(service.Current);
        Assert.Null(service.ExpiresAt);
        Assert.Equal("", service.StatusMessage);
        Assert.Equal(3, changed);
    }

    [Fact]
    public async Task MicrosoftLoginForceNewLoginIgnoresValidCacheUntilLoginSucceeds()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "cached-refresh");
        settings.Set(AppSettingKeys.CacheMsV2Access, "cached-access");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "cached-uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "CachedAlex");
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, """{"id":"cached-uuid","name":"CachedAlex"}""");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var http = new FakeLaunchHttpClient();
        http.Enqueue("devicecode", """{"user_code":"ABCD-EFGH","device_code":"device","verification_uri":"https://microsoft.com/link","expires_in":900,"interval":1}""");
        http.Enqueue("oauth2/v2.0/token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"new-uuid","name":"NewAlex"}""");
        var presenter = new CaptureMicrosoftDeviceCodePresenter();
        var service = new MicrosoftLoginService(http, settings, presenter);

        var session = await service.LoginAsync(forceNewLogin: true);

        Assert.Equal("NewAlex", session.UserName);
        Assert.Equal("new-uuid", session.Uuid);
        Assert.Equal("ABCD-EFGH", presenter.LastInfo?.UserCode);
        Assert.Contains(http.Requests, request => request.Url.Contains("devicecode", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(http.Requests, request => request.Url.Contains("oauth20_token", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("refresh-new", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        Assert.Equal("new-uuid", settings.Get(AppSettingKeys.CacheMsV2Uuid, ""));
    }

    [Fact]
    public async Task MicrosoftLoginUsesClientIdFromSettingsBeforeEnvironment()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client-from-settings");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh-old");
        var old = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"uuid","name":"Alex"}""");
        try
        {
            var service = new MicrosoftLoginService(http, settings);

            var session = await service.LoginAsync();

            Assert.Equal(LoginType.Ms, session.Type);
            Assert.Contains(http.Requests, request => request.Content.Contains("client_id=client-from-settings", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", old);
        }
    }

    [Fact]
    public async Task MicrosoftLoginRefreshesThroughSixStepFlow()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh-old");
        var old = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "client");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"uuid","name":"Alex"}""");
        try
        {
            var service = new MicrosoftLoginService(http, settings);

            var session = await service.LoginAsync();

            Assert.Equal(LoginType.Ms, session.Type);
            Assert.Equal("Alex", session.UserName);
            Assert.Equal("uuid", session.Uuid);
            Assert.Equal("mc", session.AccessToken);
            Assert.Equal("refresh-new", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", old);
        }
    }

    [Fact]
    public async Task MicrosoftLoginUpsertsAccountHistoryAfterRefresh()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh-old");
        settings.Set(AppSettingKeys.CacheMsV2AccountsJson, JsonSerializer.Serialize(new[]
        {
            new MicrosoftAccountCacheEntry("uuid", "OldAlex", "refresh-very-old", "old-mc", 1, """{"id":"uuid","name":"OldAlex"}""", DateTimeOffset.UtcNow.AddDays(-1)),
            new MicrosoftAccountCacheEntry("other-uuid", "Other", "other-refresh", "other-access", 2, """{"id":"other-uuid","name":"Other"}""", DateTimeOffset.UtcNow.AddDays(-2))
        }));
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc-new","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"uuid","name":"Alex"}""");
        var service = new MicrosoftLoginService(http, settings);

        var session = await service.LoginAsync();
        var accounts = JsonSerializer.Deserialize<MicrosoftAccountCacheEntry[]>(settings.Get(AppSettingKeys.CacheMsV2AccountsJson, ""))!;

        Assert.Equal("Alex", session.UserName);
        Assert.Equal(2, accounts.Length);
        Assert.Equal("uuid", accounts[0].Uuid);
        Assert.Equal("Alex", accounts[0].Name);
        Assert.Equal("refresh-new", accounts[0].RefreshToken);
        Assert.Equal("mc-new", accounts[0].AccessToken);
        Assert.Equal("other-uuid", accounts[1].Uuid);
    }

    [Fact]
    public async Task MicrosoftLoginTriesCachedAccountsBeforeDeviceCodeWhenCurrentRefreshFails()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "expired-current");
        settings.Set(AppSettingKeys.CacheMsV2AccountsJson, JsonSerializer.Serialize(new[]
        {
            new MicrosoftAccountCacheEntry("old-uuid", "OldAlex", "expired-current", "old-access", 1, """{"id":"old-uuid","name":"OldAlex"}""", DateTimeOffset.UtcNow.AddDays(-1)),
            new MicrosoftAccountCacheEntry("backup-uuid", "BackupAlex", "backup-refresh", "backup-access", 2, """{"id":"backup-uuid","name":"BackupAlex"}""", DateTimeOffset.UtcNow)
        }));
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"error":"invalid_grant"}""");
        http.Enqueue("oauth20_token", """{"access_token":"oauth-backup","refresh_token":"refresh-backup-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc-backup","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"backup-uuid","name":"BackupAlex"}""");
        var presenter = new CaptureMicrosoftDeviceCodePresenter();
        var service = new MicrosoftLoginService(http, settings, presenter);

        var session = await service.LoginAsync();
        var accounts = JsonSerializer.Deserialize<MicrosoftAccountCacheEntry[]>(settings.Get(AppSettingKeys.CacheMsV2AccountsJson, ""))!;

        Assert.Equal("BackupAlex", session.UserName);
        Assert.Equal("backup-uuid", session.Uuid);
        Assert.Equal("refresh-backup-new", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        Assert.Equal("backup-uuid", accounts[0].Uuid);
        Assert.Equal("refresh-backup-new", accounts[0].RefreshToken);
        Assert.Null(presenter.LastInfo);
        Assert.DoesNotContain(http.Requests, request => request.Url.Contains("devicecode", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, http.Requests.Count(request => request.Url.Contains("oauth20_token", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task MicrosoftLoginReadsOldPclLoginMsJsonBeforeDeviceCode()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.LoginMsJson, """{"BackupAlex":"backup-refresh"}""");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth-backup","refresh_token":"refresh-backup-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc-backup","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"backup-uuid","name":"BackupAlex"}""");
        var presenter = new CaptureMicrosoftDeviceCodePresenter();
        var service = new MicrosoftLoginService(http, settings, presenter);

        var session = await service.LoginAsync();
        var accounts = JsonSerializer.Deserialize<MicrosoftAccountCacheEntry[]>(settings.Get(AppSettingKeys.CacheMsV2AccountsJson, ""))!;
        using var legacy = JsonDocument.Parse(settings.Get(AppSettingKeys.LoginMsJson, "{}"));

        Assert.Equal("BackupAlex", session.UserName);
        Assert.Equal("backup-uuid", session.Uuid);
        Assert.Equal("refresh-backup-new", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        Assert.Equal("backup-uuid", accounts[0].Uuid);
        Assert.Equal("refresh-backup-new", legacy.RootElement.GetProperty("BackupAlex").GetString());
        Assert.Null(presenter.LastInfo);
        Assert.DoesNotContain(http.Requests, request => request.Url.Contains("devicecode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MicrosoftLoginStartsDeviceCodeFlowWhenNoRefreshToken()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var old = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "client");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("devicecode", """{"user_code":"ABCD-EFGH","device_code":"device","verification_uri":"https://microsoft.com/link","expires_in":900,"interval":1,"message":"Use ABCD-EFGH"}""");
        http.Enqueue("oauth2/v2.0/token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"uuid","name":"Alex"}""");
        var presenter = new CaptureMicrosoftDeviceCodePresenter();
        try
        {
            var service = new MicrosoftLoginService(http, settings, presenter);

            var session = await service.LoginAsync();

            Assert.Equal(LoginType.Ms, session.Type);
            Assert.Equal("Alex", session.UserName);
            Assert.Equal("ABCD-EFGH", presenter.LastInfo?.UserCode);
            Assert.Equal("https://microsoft.com/link", presenter.LastInfo?.VerificationUri);
            Assert.Contains(http.Requests, request => request.Url.Contains("devicecode", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(http.Requests, request => request.Url.Contains("devicecode", StringComparison.OrdinalIgnoreCase)
                && request.Content.Contains("tenant=/consumers", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(http.Requests, request => request.Content.Contains("device_code=device", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("refresh-new", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", old);
        }
    }

    [Fact]
    public async Task MicrosoftLoginContinuesDeviceCodeFlowWhenMicrosoftRequestsSlowDown()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("devicecode", """{"user_code":"ABCD-EFGH","device_code":"device","verification_uri":"https://microsoft.com/link","expires_in":900,"interval":1}""");
        http.Enqueue("oauth2/v2.0/token", """{"error":"slow_down","error_description":"polling too frequently"}""");
        http.Enqueue("oauth2/v2.0/token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"uuid","name":"Alex"}""");
        var deviceCodes = new MicrosoftDeviceCodeStatusService();
        var service = new MicrosoftLoginService(http, settings, deviceCodes);

        var session = await service.LoginAsync();

        Assert.Equal(LoginType.Ms, session.Type);
        Assert.Equal("Alex", session.UserName);
        Assert.Equal("refresh-new", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        Assert.Equal(2, http.Requests.Count(request => request.Url.Contains("oauth2/v2.0/token", StringComparison.OrdinalIgnoreCase)));
        Assert.False(deviceCodes.IsActive);
        Assert.Equal("", deviceCodes.StatusMessage);
    }

    [Fact]
    public async Task MicrosoftLoginContinuesDeviceCodeFlowWhenPendingIsReturnedAsHttp400()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("devicecode", """{"user_code":"ABCD-EFGH","device_code":"device","verification_uri":"https://microsoft.com/link","expires_in":900,"interval":1}""");
        http.EnqueueException(
            "oauth2/v2.0/token",
            new LaunchHttpException(System.Net.HttpStatusCode.BadRequest, "Bad Request", """{"error":"authorization_pending","error_description":"authorization is pending"}""", "https://login.microsoftonline.com/consumers/oauth2/v2.0/token"));
        http.Enqueue("oauth2/v2.0/token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"uuid","name":"Alex"}""");
        var service = new MicrosoftLoginService(http, settings, new CaptureMicrosoftDeviceCodePresenter());

        var session = await service.LoginAsync();

        Assert.Equal(LoginType.Ms, session.Type);
        Assert.Equal("Alex", session.UserName);
        Assert.Equal(2, http.Requests.Count(request => request.Url.Contains("oauth2/v2.0/token", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task MicrosoftDeviceCodeLoginClearsStatusWhenPollingFails()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("devicecode", """{"user_code":"ABCD-EFGH","device_code":"device","verification_uri":"https://microsoft.com/link","expires_in":900,"interval":1}""");
        http.Enqueue("oauth2/v2.0/token", """{"error":"authorization_declined","error_description":"user declined"}""");
        var deviceCodes = new MicrosoftDeviceCodeStatusService();
        var service = new MicrosoftLoginService(http, settings, deviceCodes);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

        Assert.Contains("PCL Sharp", ex.Message);
        Assert.False(deviceCodes.IsActive);
        Assert.Equal("", deviceCodes.StatusMessage);
    }

    [Fact]
    public async Task MicrosoftDeviceCodeLoginMapsHttp400PollingErrorBody()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("devicecode", """{"user_code":"ABCD-EFGH","device_code":"device","verification_uri":"https://microsoft.com/link","expires_in":900,"interval":1}""");
        http.EnqueueException(
            "oauth2/v2.0/token",
            new LaunchHttpException(System.Net.HttpStatusCode.BadRequest, "Bad Request", """{"error":"authorization_declined","error_description":"user declined"}""", "https://login.microsoftonline.com/consumers/oauth2/v2.0/token"));
        var service = new MicrosoftLoginService(http, settings, new CaptureMicrosoftDeviceCodePresenter());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

        Assert.Contains("PCL Sharp", ex.Message);
    }

    [Fact]
    public async Task MicrosoftLoginFallsBackToDeviceCodeWhenRefreshTokenIsInvalid()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "expired-refresh");
        settings.Set(AppSettingKeys.CacheMsV2Access, "old-access");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "old-uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "OldName");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"error":"invalid_grant"}""");
        http.Enqueue("devicecode", """{"user_code":"ABCD-EFGH","device_code":"device","verification_uri":"https://microsoft.com/link","expires_in":900,"interval":1}""");
        http.Enqueue("oauth2/v2.0/token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"uuid","name":"Alex"}""");
        var presenter = new CaptureMicrosoftDeviceCodePresenter();
        var service = new MicrosoftLoginService(http, settings, presenter);

        var session = await service.LoginAsync();

        Assert.Equal("Alex", session.UserName);
        Assert.Equal("ABCD-EFGH", presenter.LastInfo?.UserCode);
        Assert.Equal("refresh-new", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        Assert.Equal("uuid", settings.Get(AppSettingKeys.CacheMsV2Uuid, ""));
        Assert.DoesNotContain(http.Requests, request => request.Content.Contains("old-access", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("authorization_declined", "", "拒绝")]
    [InlineData("invalid_grant", "Account security interrupt", "安全问题")]
    [InlineData("invalid_grant", "service abuse", "封禁")]
    [InlineData("AADSTS70000", "", "\u5931\u6548")]
    public async Task MicrosoftDeviceCodeLoginMapsOldPclPollingErrors(string error, string description, string expectedMessagePart)
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var old = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "client");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("devicecode", """{"user_code":"ABCD-EFGH","device_code":"device","verification_uri":"https://microsoft.com/link","expires_in":900,"interval":1}""");
        http.Enqueue("oauth2/v2.0/token", $$"""{"error":"{{error}}","error_description":"{{description}}"}""");
        try
        {
            var service = new MicrosoftLoginService(http, settings, new CaptureMicrosoftDeviceCodePresenter());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

            Assert.Contains(expectedMessagePart, ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", old);
        }
    }

    [Theory]
    [InlineData("2148916233", "Xbox")]
    [InlineData("2148916235", "地区")]
    [InlineData("2148916238", "儿童账号")]
    public async Task MicrosoftLoginMapsXstsErrorsToReadableMessages(string xerr, string expectedMessagePart)
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh-old");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", $$"""{"XErr":{{xerr}},"Message":"blocked"}""");
        var service = new MicrosoftLoginService(http, settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Theory]
    [InlineData("""{""error"":""Unauthorized""}""", "重新登录")]
    [InlineData("""{"path":"/minecraft/profile","error":"NOT_FOUND"}""", "Minecraft Java Edition")]
    public async Task MicrosoftLoginMapsMinecraftServicesHttpErrors(string responseBody, string expectedMessagePart)
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh-old");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.EnqueueException(
            "entitlements",
            new LaunchHttpException(System.Net.HttpStatusCode.Unauthorized, "Unauthorized", responseBody, "https://api.minecraftservices.com/entitlements/mcstore"));
        var service = new MicrosoftLoginService(http, settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests, "Too Many Requests", """{"error":"too_many_requests"}""", "太过频繁")]
    [InlineData(System.Net.HttpStatusCode.Forbidden, "Forbidden", """{"error":"ForbiddenOperationException"}""", "IP")]
    [InlineData(System.Net.HttpStatusCode.Forbidden, "Forbidden", """{"error":"ACCOUNT_SUSPENDED"}""", "封禁")]
    public async Task MicrosoftLoginMapsMinecraftLoginWithXboxErrors(System.Net.HttpStatusCode statusCode, string reason, string responseBody, string expectedMessagePart)
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh-old");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.EnqueueException(
            "login_with_xbox",
            new LaunchHttpException(statusCode, reason, responseBody, "https://api.minecraftservices.com/authentication/login_with_xbox"));
        var service = new MicrosoftLoginService(http, settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Fact]
    public async Task MicrosoftLoginMapsMissingMinecraftProfileLikeOldPcl()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh-old");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"game_minecraft"}]}""");
        http.EnqueueException(
            "minecraft/profile",
            new LaunchHttpException(System.Net.HttpStatusCode.NotFound, "Not Found", """{"error":"NOT_FOUND"}""", "https://api.minecraftservices.com/minecraft/profile"));
        var service = new MicrosoftLoginService(http, settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

        Assert.Contains("创建 Minecraft 玩家档案", ex.Message);
    }

    [Fact]
    public async Task MicrosoftLoginRejectsEntitlementsWithoutJavaEdition()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh-old");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"minecraft_bedrock"}]}""");
        var service = new MicrosoftLoginService(http, settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

        Assert.Contains("Minecraft Java Edition", ex.Message);
        Assert.DoesNotContain(http.Requests, request => request.Url.Contains("minecraft/profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MicrosoftLoginAcceptsProductMinecraftEntitlement()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh-old");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("oauth20_token", """{"access_token":"oauth","refresh_token":"refresh-new"}""");
        http.Enqueue("user.auth.xboxlive.com", """{"Token":"xbl"}""");
        http.Enqueue("xsts.auth.xboxlive.com", """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs"}]}}""");
        http.Enqueue("login_with_xbox", """{"access_token":"mc","expires_in":3600}""");
        http.Enqueue("entitlements", """{"items":[{"name":"product_minecraft"}]}""");
        http.Enqueue("minecraft/profile", """{"id":"uuid","name":"Alex"}""");
        var service = new MicrosoftLoginService(http, settings);

        var session = await service.LoginAsync();

        Assert.Equal(LoginType.Ms, session.Type);
        Assert.Equal("Alex", session.UserName);
        Assert.Equal("uuid", session.Uuid);
    }

    [Fact]
    public async Task YggdrasilLoginAuthenticatesAndCaches()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var http = new FakeLaunchHttpClient();
        http.Enqueue("/authenticate", """{"accessToken":"access","clientToken":"client","selectedProfile":{"id":"uuid","name":"Player"}}""");
        http.Enqueue("https://auth.example", """{"meta":{"serverName":"Example Auth"}}""");
        var service = new YggdrasilLoginService(http, settings);

        var session = await service.LoginAsync(new LoginRequest(LoginType.Auth, "", "email", "pass", "https://auth.example", true));

        Assert.Equal(LoginType.Auth, session.Type);
        Assert.Equal("Player", session.UserName);
        Assert.Contains("Example Auth", session.AuthlibInjectorMetadata);
        Assert.Equal("access", settings.Get(AppSettingKeys.CacheAuthAccess, ""));
        Assert.Equal("email", settings.Get(AppSettingKeys.CacheAuthUsername, ""));
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(HttpMethod.Get, http.Requests[1].Method);
    }

    [Fact]
    public async Task YggdrasilLoginAutoSelectsSingleAvailableProfile()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var http = new FakeLaunchHttpClient();
        http.Enqueue("/authenticate", """{"accessToken":"access","clientToken":"client","availableProfiles":[{"id":"uuid-single","name":"OnlyOne"}]}""");
        var service = new YggdrasilLoginService(http, settings);

        var session = await service.LoginAsync(new LoginRequest(LoginType.Nide, "", "email", "pass", "server", true));

        Assert.Equal("OnlyOne", session.UserName);
        Assert.Equal("uuid-single", session.Uuid);
        Assert.Equal("OnlyOne", settings.Get(AppSettingKeys.CacheNideName, ""));
        Assert.Equal("uuid-single", settings.Get(AppSettingKeys.CacheNideUuid, ""));
    }

    [Fact]
    public async Task YggdrasilLoginSelectsCachedProfileFromMultipleAvailableProfiles()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.CacheNideName, "CachedRole");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("/authenticate", """{"accessToken":"access","clientToken":"client","availableProfiles":[{"id":"uuid-other","name":"OtherRole"},{"id":"uuid-cached","name":"CachedRole"}]}""");
        var service = new YggdrasilLoginService(http, settings);

        var session = await service.LoginAsync(new LoginRequest(LoginType.Nide, "", "email", "pass", "server", true));

        Assert.Equal("CachedRole", session.UserName);
        Assert.Equal("uuid-cached", session.Uuid);
        Assert.Equal("CachedRole", settings.Get(AppSettingKeys.CacheNideName, ""));
    }

    [Fact]
    public async Task YggdrasilLoginUsesProfileSelectorWhenMultipleProfilesNeedChoice()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var http = new FakeLaunchHttpClient();
        http.Enqueue("/authenticate", """{"accessToken":"access","clientToken":"client","availableProfiles":[{"id":"uuid-a","name":"RoleA"},{"id":"uuid-b","name":"RoleB"}]}""");
        var selector = new CaptureYggdrasilProfileSelector("RoleB");
        var service = new YggdrasilLoginService(http, settings, selector);

        var session = await service.LoginAsync(new LoginRequest(LoginType.Nide, "", "email", "pass", "server", true));

        Assert.Equal("RoleB", session.UserName);
        Assert.Equal("uuid-b", session.Uuid);
        Assert.Equal("RoleB", settings.Get(AppSettingKeys.CacheNideName, ""));
        Assert.Equal("uuid-b", settings.Get(AppSettingKeys.CacheNideUuid, ""));
        Assert.Equal(1, selector.Calls);
        Assert.Equal("选择统一通行证角色", selector.LastTitle);
        Assert.Equal(["RoleA", "RoleB"], selector.LastProfiles.Select(profile => profile.Name));
    }

    [Fact]
    public async Task YggdrasilLoginFailsClearlyWhenMultipleProfilesNeedChoice()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var http = new FakeLaunchHttpClient();
        http.Enqueue("/authenticate", """{"accessToken":"access","clientToken":"client","availableProfiles":[{"id":"uuid-a","name":"RoleA"},{"id":"uuid-b","name":"RoleB"}]}""");
        var service = new YggdrasilLoginService(http, settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAsync(new LoginRequest(LoginType.Nide, "", "email", "pass", "server", true)));

        Assert.Contains("多个角色", ex.Message);
    }

    [Fact]
    public async Task YggdrasilLoginFailsClearlyWhenNoProfileExists()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var http = new FakeLaunchHttpClient();
        http.Enqueue("/authenticate", """{"accessToken":"access","clientToken":"client","availableProfiles":[]}""");
        var service = new YggdrasilLoginService(http, settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAsync(new LoginRequest(LoginType.Nide, "", "email", "pass", "server", true)));

        Assert.Contains("还没有创建角色", ex.Message);
    }

    [Fact]
    public async Task YggdrasilLoginUsesValidateCacheWhenAvailable()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.CacheNideAccess, "access");
        settings.Set(AppSettingKeys.CacheNideClient, "client");
        settings.Set(AppSettingKeys.CacheNideUuid, "uuid");
        settings.Set(AppSettingKeys.CacheNideName, "Cached");
        var http = new FakeLaunchHttpClient();
        http.Enqueue("/validate", "");
        var service = new YggdrasilLoginService(http, settings);

        var session = await service.LoginAsync(new LoginRequest(LoginType.Nide, "", "email", "pass", "server", true));

        Assert.Equal("Cached", session.UserName);
        Assert.Single(http.Requests);
    }

    [Fact]
    public void LaunchArgumentBuilderHandlesInheritanceRulesAndServer()
    {
        using var temp = new TempDirectory();
        WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "jvm": [
              {"rules":[{"action":"allow","os":{"name":"windows"}}],"value":"-Dwindows=true"},
              {"rules":[{"action":"allow","os":{"name":"linux"}}],"value":"-Dlinux=true"}
            ],
            "game": ["--username", "${auth_player_name}"]
          },
          "libraries": []
        }
        """, createJar: true);
        var child = WriteVersion(temp.Path, "Forge", """
        {
          "id": "Forge",
          "inheritsFrom": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": { "game": ["--demo"] },
          "libraries": []
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(
            new LaunchRequest(child, temp.Path, null, "Steve", 512, 2048, 1280, 720, "-Dfoo=1 -Dfoo=2", "", false, ServerIp: "mc.example:25566"),
            CreateJava(17),
            new LegacyLoginService().CreateSession("Steve"));

        Assert.Contains("-Dwindows=true", result.Arguments);
        Assert.DoesNotContain("-Dlinux=true", result.Arguments);
        Assert.Contains("--server mc.example --port 25566", result.Arguments);
        Assert.DoesNotContain("-Dfoo=1", result.Arguments);
        Assert.Contains("-Dfoo=2", result.Arguments);
    }

    [Fact]
    public async Task LaunchFileCompleterReportsAssetsIndex()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var completer = new LaunchFileCompleter();

        var missing = await completer.CheckMissingFilesAsync(CreateRequest(instance, temp.Path), []);

        Assert.Contains(Path.Combine(temp.Path, "assets", "indexes", "5.json"), missing);
    }

    [Fact]
    public async Task NativesExtractorExtractsWindowsClassifier()
    {
        using var temp = new TempDirectory();
        var nativeJar = Path.Combine(temp.Path, "libraries", "org", "demo", "native", "1.0", "native-1.0-natives-windows.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(nativeJar)!);
        using (var archive = ZipFile.Open(nativeJar, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("demo.dll");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("dll");
        }

        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": [{
            "downloads": {
              "classifiers": {
                "natives-windows": { "path": "org/demo/native/1.0/native-1.0-natives-windows.jar" }
              }
            }
          }]
        }
        """, createJar: true);
        var extractor = new NativesExtractor(new NullLoggerService());

        var natives = await extractor.ExtractAsync(instance);

        Assert.True(File.Exists(Path.Combine(natives, "demo.dll")));
    }

    [Fact]
    public async Task LaunchScriptExporterWritesSanitizedBatch()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """{"id":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""", createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchArgumentInfo, "GlobalInfo");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentInfo}", "InstanceInfo");
        settings.Set(AppSettingKeys.LaunchAdvanceRun, "echo global {user} {login} {minecraft} {setup:LaunchArgumentInfo}");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceRun}", "echo version {name} {version} {uuid} {setup:VersionArgumentInfo}");
        var java = CreateJava(17);
        var login = new LoginSession(LoginType.Ms, "Steve", "ABCDEF", "secret-token", "client");
        var startInfo = new System.Diagnostics.ProcessStartInfo(java.PathJava) { WorkingDirectory = instance.VersionPath };
        var profile = new LaunchProfile(instance, java, login, "--accessToken secret-token", "\"java\" --accessToken ***", startInfo, []);
        var target = Path.Combine(temp.Path, "LatestLaunch.bat");

        var exported = await new LaunchScriptExporter(settings).ExportAsync(profile, target);
        var text = await File.ReadAllTextAsync(target);

        Assert.Equal(target, exported);
        Assert.Contains("chcp 65001>nul", text);
        Assert.Contains("echo global Steve 正版 " + temp.Path, text);
        Assert.Contains("echo version 1.20.1 1.20.1 abcdef", text);
        Assert.Contains("GlobalInfo", text);
        Assert.Contains("InstanceInfo", text);
        Assert.Contains("--accessToken ***", text);
        Assert.DoesNotContain("secret-token", text);
    }

    [Fact]
    public async Task CustomCommandServiceRunsGlobalThenInstanceCommandWithWaitSettings()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """{"id":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""", createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchArgumentInfo, "GlobalInfo");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentInfo}", "InstanceInfo");
        settings.Set(AppSettingKeys.LaunchAdvanceRun, "echo global {user} {minecraft} {setup:LaunchArgumentInfo}");
        settings.Set(AppSettingKeys.LaunchAdvanceRunWait, false);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceRun}", "echo instance {name} {version} {uuid} {setup:VersionArgumentInfo}");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceRunWait}", true);
        var runner = new CaptureCustomCommandRunner();
        var java = CreateJava(17);
        var login = new LoginSession(LoginType.Legacy, "Steve", "ABCDEF", "0", "client");
        var profile = new LaunchProfile(
            instance,
            java,
            login,
            "",
            "",
            new System.Diagnostics.ProcessStartInfo(java.PathJava) { WorkingDirectory = instance.VersionPath },
            []);
        var request = CreateRequest(instance, temp.Path);
        var service = new CustomCommandService(settings, new NullLoggerService(), runner);

        await service.RunAsync(request, profile);

        Assert.Collection(
            runner.Calls,
            call =>
            {
                Assert.Equal("echo global Steve " + temp.Path + " GlobalInfo", call.Command);
                Assert.Equal(instance.VersionPath, call.WorkingDirectory);
                Assert.False(call.WaitForExit);
            },
            call =>
            {
                Assert.Equal("echo instance 1.20.1 1.20.1 abcdef InstanceInfo", call.Command);
                Assert.Equal(instance.VersionPath, call.WorkingDirectory);
                Assert.True(call.WaitForExit);
            });
    }

    [Fact]
    public void LaunchArgumentBuilderSanitizesRealAccessToken()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": { "game": ["--accessToken", "${auth_access_token}"] },
          "libraries": []
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(
            CreateRequest(instance, temp.Path),
            CreateJava(17),
            new LoginSession(LoginType.Ms, "Alex", "uuid", "secret-token", "client"));

        Assert.DoesNotContain("secret-token", result.SanitizedCommandLine);
        Assert.Contains("--accessToken ***", result.SanitizedCommandLine);
    }

    [Fact]
    public async Task LaunchPageUsesInstanceJavaSelectionBeforeGlobal()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var globalJavaPath = Path.Combine(temp.Path, "java-global", "bin", "java.exe");
        var instanceJavaPath = Path.Combine(temp.Path, "java-instance", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LoginLegacyName, "Steve¨Alex");
        settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, globalJavaPath);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentJavaSelect}", instanceJavaPath);
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([
                new JavaEntry(globalJavaPath, new Version(1, 17, 0, 0), false, true, false, true),
                new JavaEntry(instanceJavaPath, new Version(1, 17, 0, 0), false, true, false, true)
            ]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        Assert.Equal(["Steve", "Alex"], viewModel.LegacyNameHistory);
        viewModel.LegacyName = "Alex";
        viewModel.LaunchWindowType = 4;
        viewModel.LauncherVisibility = 3;
        viewModel.LaunchWindowWidth = 1600;
        viewModel.LaunchWindowHeight = 900;
        await viewModel.LaunchGameAsync();

        Assert.Equal(instanceJavaPath, viewModel.SelectedJava?.PathJava);
        Assert.Equal(instanceJavaPath, pipeline.LastRequest?.JavaPath);
        Assert.Equal(4, pipeline.LastRequest?.WindowType);
        Assert.Equal(3, pipeline.LastRequest?.LauncherVisibility);
        Assert.Equal(1600, pipeline.LastRequest?.WindowWidth);
        Assert.Equal(900, pipeline.LastRequest?.WindowHeight);
        Assert.Equal(globalJavaPath, settings.Get(AppSettingKeys.LaunchArgumentJavaSelect, ""));
        Assert.Equal("Alex¨Steve", settings.Get(AppSettingKeys.LoginLegacyName, ""));
        Assert.Equal(4, settings.Get(AppSettingKeys.LaunchArgumentWindowType, 1));
        Assert.Equal(3, settings.Get(AppSettingKeys.LaunchArgumentVisible, 5));
        Assert.Equal(1600, settings.Get(AppSettingKeys.LaunchArgumentWindowWidth, 854));
        Assert.Equal(900, settings.Get(AppSettingKeys.LaunchArgumentWindowHeight, 480));
        Assert.Equal(["Alex", "Steve"], viewModel.LegacyNameHistory);
    }

    [Fact]
    public async Task LaunchPageReplacesSavedJavaAboveCompatibleRange()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var java17Path = Path.Combine(temp.Path, "java-17", "bin", "java.exe");
        var java25Path = Path.Combine(temp.Path, "java-25", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, java25Path);
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([
                new JavaEntry(java25Path, new Version(1, 25, 0, 2), false, true, false, true),
                new JavaEntry(java17Path, new Version(1, 17, 0, 8), false, true, false, true)
            ]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();

        Assert.Equal(java17Path, viewModel.SelectedJava?.PathJava);
        Assert.Contains("自动切换到兼容 Java", viewModel.StatusMessage);
        await viewModel.GenerateProfileCommand.ExecuteAsync(null);

        Assert.Equal(java17Path, pipeline.LastRequest?.JavaPath);
    }

    [Fact]
    public async Task LaunchPageUsesCompatibleJavaWhenInstanceJavaOverrideIsTooNew()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var java17Path = Path.Combine(temp.Path, "java-17", "bin", "java.exe");
        var java25Path = Path.Combine(temp.Path, "java-25", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentJavaSelect}", java25Path);
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([
                new JavaEntry(java25Path, new Version(1, 25, 0, 2), false, true, false, true),
                new JavaEntry(java17Path, new Version(1, 17, 0, 8), false, true, false, true)
            ]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        await viewModel.GenerateProfileCommand.ExecuteAsync(null);

        Assert.Equal(java17Path, viewModel.SelectedJava?.PathJava);
        Assert.Equal(java17Path, pipeline.LastRequest?.JavaPath);
        Assert.Contains(viewModel.JavaEntryOptions, option =>
            option.Entry.PathJava == java17Path
            && option.IsCompatible
            && option.DetailText.Contains("兼容当前版本", StringComparison.Ordinal));
        Assert.Contains(viewModel.JavaEntryOptions, option =>
            option.Entry.PathJava == java25Path
            && !option.IsCompatible
            && option.DetailText.Contains("不兼容，当前版本需要 Java 17", StringComparison.Ordinal));
        Assert.Equal(java25Path, settings.Get($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentJavaSelect}", ""));
    }

    [Fact]
    public async Task LaunchPageLeavesJavaUnselectedWhenOnlyIncompatibleJavaIsAvailable()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var java25Path = Path.Combine(temp.Path, "java-25", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, java25Path);
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([
                new JavaEntry(java25Path, new Version(1, 25, 0, 2), false, true, false, true)
            ]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();

        Assert.Null(viewModel.SelectedJava);
        var option = Assert.Single(viewModel.JavaEntryOptions);
        Assert.False(option.IsCompatible);
        Assert.Contains("不兼容，当前版本需要 Java 17", option.DetailText);
        Assert.Contains("未找到满足", viewModel.StatusMessage);
        await viewModel.GenerateProfileCommand.ExecuteAsync(null);

        Assert.Null(pipeline.LastRequest?.JavaPath);
    }

    [Fact]
    public async Task LaunchPageDoesNotUseManuallySelectedIncompatibleJavaWithoutOverride()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var java17Path = Path.Combine(temp.Path, "java-17", "bin", "java.exe");
        var java25Path = Path.Combine(temp.Path, "java-25", "bin", "java.exe");
        var java25 = new JavaEntry(java25Path, new Version(1, 25, 0, 2), false, true, false, true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([
                java25,
                new JavaEntry(java17Path, new Version(1, 17, 0, 8), false, true, false, true)
            ]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        viewModel.SelectedJava = java25;
        await viewModel.GenerateProfileCommand.ExecuteAsync(null);

        Assert.Null(pipeline.LastRequest?.JavaPath);
    }

    [Fact]
    public async Task LaunchPageRejectsIncompatibleJavaSelectedFromDropdown()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var java17Path = Path.Combine(temp.Path, "java-17", "bin", "java.exe");
        var java25Path = Path.Combine(temp.Path, "java-25", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([
                new JavaEntry(java25Path, new Version(1, 25, 0, 2), false, true, false, true),
                new JavaEntry(java17Path, new Version(1, 17, 0, 8), false, true, false, true)
            ]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        var incompatible = Assert.Single(viewModel.JavaEntryOptions, option => option.Entry.PathJava == java25Path);

        viewModel.SelectedJavaOption = incompatible;

        Assert.Equal(java17Path, viewModel.SelectedJava?.PathJava);
        Assert.Equal(java17Path, viewModel.SelectedJavaOption?.Entry.PathJava);
        Assert.Contains("不能为当前版本选择", viewModel.StatusMessage);
        Assert.Equal("", settings.Get(AppSettingKeys.LaunchArgumentJavaSelect, ""));
    }

    [Fact]
    public async Task LaunchPageRefreshesJavaCompatibilityWhenSelectedInstanceChanges()
    {
        using var temp = new TempDirectory();
        var oldInstance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var newInstance = WriteVersion(temp.Path, "1.20.5", """
        {
          "id": "1.20.5",
          "releaseTime": "2024-04-24T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var java17Path = Path.Combine(temp.Path, "java-17", "bin", "java.exe");
        var java21Path = Path.Combine(temp.Path, "java-21", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, oldInstance.Name);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [oldInstance, newInstance]),
            new FakeJavaDiscoveryService([
                new JavaEntry(java17Path, new Version(1, 17, 0, 8), false, true, false, true),
                new JavaEntry(java21Path, new Version(1, 21, 0, 2), false, true, false, true)
            ]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();

        Assert.Equal(java17Path, viewModel.SelectedJava?.PathJava);

        viewModel.SelectVersionCommand.Execute(newInstance);

        Assert.Equal(java21Path, viewModel.SelectedJava?.PathJava);
        Assert.Contains(viewModel.JavaEntryOptions, option =>
            option.Entry.PathJava == java17Path
            && !option.IsCompatible
            && option.DetailText.Contains("不兼容，当前版本需要 Java 21", StringComparison.Ordinal));
        Assert.Contains(viewModel.JavaEntryOptions, option =>
            option.Entry.PathJava == java21Path
            && option.IsCompatible
            && option.DetailText.Contains("兼容当前版本", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaunchPageSavesSelectedGlobalJavaAsOldPclJson()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var java = new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, true, false);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentJavaSelect}", "\u4f7f\u7528\u5168\u5c40\u8bbe\u7f6e");
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([java]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        await viewModel.GenerateProfileCommand.ExecuteAsync(null);

        var saved = settings.Get(AppSettingKeys.LaunchArgumentJavaSelect, "");
        Assert.StartsWith("{", saved);
        Assert.Contains("\"Path\"", saved);
        Assert.Contains("\"VersionString\":\"1.17.0.0\"", saved);
        Assert.Equal(java.PathJava, JavaEntry.ResolveSettingJavaPath(saved));
        Assert.Equal(java.PathJava, pipeline.LastRequest?.JavaPath);
    }

    [Fact]
    public async Task LaunchPageUsesInstanceServerLoginBeforeGlobalLogin()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LoginType, LoginType.Legacy);
        settings.Set(AppSettingKeys.LoginAuthEmail, "auth-user@example.com");
        settings.Set(AppSettingKeys.LoginAuthPass, "auth-password");
        settings.Set(AppSettingKeys.CacheAuthAccess, "old-access");
        settings.Set(AppSettingKeys.CacheAuthServerServer, "https://old.example.com/api/yggdrasil");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionServerLogin}", 4);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionServerAuthServer}", "https://auth.example.com/api/yggdrasil");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionServerAuthRegister}", "https://auth.example.com/auth/register");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionServerAuthName}", "Example Auth");
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        await viewModel.GenerateProfileCommand.ExecuteAsync(null);

        Assert.Equal(LoginType.Auth, pipeline.LastRequest?.LoginType);
        Assert.Equal("auth-user@example.com", pipeline.LastRequest?.LoginUserName);
        Assert.Equal("auth-password", pipeline.LastRequest?.LoginPassword);
        Assert.Equal("https://auth.example.com/api/yggdrasil", pipeline.LastRequest?.LoginServer);
        Assert.Equal(LoginType.Legacy, viewModel.SelectedLoginType);
        Assert.Equal("", settings.Get(AppSettingKeys.CacheAuthAccess, ""));
        Assert.Equal("https://auth.example.com/api/yggdrasil", settings.Get(AppSettingKeys.CacheAuthServerServer, ""));
        Assert.Equal("https://auth.example.com/auth/register", settings.Get(AppSettingKeys.CacheAuthServerRegister, ""));
        Assert.Equal("Example Auth", settings.Get(AppSettingKeys.CacheAuthServerName, ""));
    }

    [Fact]
    public async Task LaunchPageViewModelNormalizesAutoJoinServerIpLikeOldPcl()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        viewModel.ServerIp = "play。example。com：25565";
        await viewModel.GenerateProfileCommand.ExecuteAsync(null);

        Assert.Equal("play.example.com:25565", viewModel.ServerIp);
        Assert.Equal("play.example.com:25565", pipeline.LastRequest?.ServerIp);
        Assert.Equal("play.example.com:25565", settings.Get($"Instance.{instance.Name}.{AppSettingKeys.VersionServerEnter}", ""));
    }

    [Fact]
    public async Task LaunchPageExecutesCustomLaunchEventWithVersionAndServer()
    {
        using var temp = new TempDirectory();
        var oldInstance = WriteVersion(temp.Path, "1.19.4", """
        {
          "id": "1.19.4",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var targetInstance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, oldInstance.Name);
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [oldInstance, targetInstance]),
            new FakeJavaDiscoveryService([]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        var result = await viewModel.ExecuteCustomLaunchEventAsync("1.20.1|mc。hypixel。net：25565");

        Assert.True(result.Success);
        Assert.Equal(1, pipeline.LaunchCalls);
        Assert.NotNull(pipeline.LastRequest);
        var request = pipeline.LastRequest!;
        Assert.NotNull(request.Instance);
        Assert.Equal("1.20.1", request.Instance.Name);
        Assert.Equal("mc.hypixel.net:25565", request.ServerIp);
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
    }

    [Fact]
    public async Task LaunchPageReportsMissingCustomLaunchEventVersion()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        var result = await viewModel.ExecuteCustomLaunchEventAsync("missing-version|mc.hypixel.net");

        Assert.False(result.Success);
        Assert.Contains("未找到启动事件指定版本", result.Message);
        Assert.Equal(0, pipeline.LaunchCalls);
    }

    [Fact]
    public async Task LaunchPageClearsNideCacheWhenInstanceServerChanges()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LoginNideEmail, "nide@example.com");
        settings.Set(AppSettingKeys.LoginNidePass, "nide-password");
        settings.Set(AppSettingKeys.CacheNideAccess, "old-access");
        settings.Set(AppSettingKeys.CacheNideServer, "old-server");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionServerLogin}", 3);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionServerNide}", "00000000000000000000000000000000");
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        await viewModel.GenerateProfileCommand.ExecuteAsync(null);

        Assert.Equal(LoginType.Nide, pipeline.LastRequest?.LoginType);
        Assert.Equal("nide@example.com", pipeline.LastRequest?.LoginUserName);
        Assert.Equal("nide-password", pipeline.LastRequest?.LoginPassword);
        Assert.Equal("00000000000000000000000000000000", pipeline.LastRequest?.LoginServer);
        Assert.Equal("", settings.Get(AppSettingKeys.CacheNideAccess, ""));
        Assert.Equal("00000000000000000000000000000000", settings.Get(AppSettingKeys.CacheNideServer, ""));
    }

    [Fact]
    public async Task LaunchPageSavesThirdPartyAccountHistoryWithoutPasswordWhenRememberDisabled()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginNideEmail, "old@example.com");
        settings.Set(AppSettingKeys.LoginNidePass, "old-password");
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        viewModel.SelectedLoginType = LoginType.Nide;
        Assert.Equal(["old@example.com"], viewModel.LoginUserNameHistory);
        viewModel.LoginUserName = "new@example.com";
        viewModel.LoginPassword = "secret";
        viewModel.RememberLogin = false;

        await viewModel.LaunchGameAsync();

        Assert.Equal("new@example.com¨old@example.com", settings.Get(AppSettingKeys.LoginNideEmail, ""));
        Assert.Equal("", settings.Get(AppSettingKeys.LoginNidePass, ""));
        Assert.Equal(["new@example.com", "old@example.com"], viewModel.LoginUserNameHistory);
    }

    [Fact]
    public async Task LaunchPageGenerateProfileDoesNotStartProcess()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.GenerateProfileAsync();

        Assert.Equal(1, pipeline.GenerateCalls);
        Assert.Equal(0, pipeline.LaunchCalls);
        Assert.False(pipeline.LastRequest?.StartProcess);
        Assert.Equal("", pipeline.LastRequest?.SaveBatchPath);
        Assert.Equal("Process", viewModel.LaunchCurrentStepText);
        Assert.True(viewModel.LaunchProgressPercent > 0);
    }

    [Fact]
    public async Task LaunchPageShowsFailureDiagnosticsWithIssueCodesAndMissingFiles()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var missingFiles = Enumerable.Range(1, 6)
            .Select(index => Path.Combine(temp.Path, "missing", index + ".jar"))
            .ToArray();
        var pipeline = new FailedLaunchPipelineService(missingFiles);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.LaunchGameAsync();

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.HasLaunchDiagnostics);
        Assert.True(viewModel.HasLaunchFileCompletionAction);
        Assert.Contains("6", viewModel.FileCompletionSummary);
        Assert.Contains(viewModel.FileCompletionDetails, detail => detail.Contains(missingFiles[0], StringComparison.Ordinal));
        Assert.Contains(viewModel.FileCompletionDetails, detail => detail.Contains("另有 1 个文件未显示", StringComparison.Ordinal));
        Assert.Contains("本地文件缺失", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchPageShowsJavaDiagnosticSuggestion()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var pipeline = new FixedFailedLaunchPipelineService(new LaunchValidationIssue("JavaNotFound", "未找到满足 Java 17 的 Java"));
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.LaunchGameAsync();

        Assert.False(viewModel.HasLaunchFileCompletionAction);
        Assert.Contains("扫描 Java", viewModel.LaunchDiagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchPageShowsEarlyExitDiagnosticSuggestion()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var pipeline = new FixedFailedLaunchPipelineService(new LaunchValidationIssue(
            "GameExitedEarly",
            "游戏进程很快退出，退出码：1；Java 版本过新，Forge / Mixin 或部分 Mod 不兼容当前 Java；最近日志：Unsupported class file major version 69"));
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.LaunchGameAsync();

        Assert.False(viewModel.HasLaunchFileCompletionAction);
        Assert.Contains("GameExitedEarly", viewModel.LaunchDiagnostics, StringComparison.Ordinal);
        Assert.Contains("Java 版本过新", viewModel.LaunchDiagnostics, StringComparison.Ordinal);
        Assert.Contains("切换到推荐 Java", viewModel.LaunchDiagnostics, StringComparison.Ordinal);
        Assert.Contains("1.18-1.20.4 通常使用 Java 17", viewModel.LaunchDiagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchPageCanCancelRunningLaunchLikeOldPclCancelButton()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var pipeline = new BlockingLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        var launchTask = viewModel.LaunchGameAsync();
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var runningStep = Assert.Single(viewModel.Steps, step => step.Name == "文件补全");
        Assert.Equal(LaunchStepStatus.Running, runningStep.Status);
        Assert.Equal("文件补全", viewModel.LaunchCurrentStepText);
        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.CancelBusyCommand.CanExecute(null));
        viewModel.CancelBusyCommand.Execute(null);
        await launchTask;

        Assert.True(pipeline.WasCanceled);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("已取消启动", viewModel.StatusMessage);
        Assert.True(viewModel.LaunchProgressPercent >= 0);
        Assert.NotEqual("", viewModel.LaunchCurrentStepText);
    }

    [Fact]
    public async Task LaunchPageQueuesLiveLaunchStepsThroughUiDispatcher()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        var pipeline = new BlockingLaunchPipelineService();
        var dispatcher = new QueueingUiDispatcherService(checkAccess: false);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            dispatcher: dispatcher);

        await viewModel.InitializeAsync();
        dispatcher.RunQueued();
        var launchTask = viewModel.LaunchGameAsync();
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(viewModel.Steps);
        Assert.True(dispatcher.QueuedCount > 0);

        dispatcher.RunQueued();

        var runningStep = Assert.Single(viewModel.Steps, step => step.Name == "文件补全");
        Assert.Equal(LaunchStepStatus.Running, runningStep.Status);

        viewModel.CancelBusyCommand.Execute(null);
        await launchTask;
    }

    [Fact]
    public async Task LaunchPageExportLaunchScriptUsesSaveFilePicker()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var target = Path.Combine(temp.Path, "LatestLaunch.bat");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LoginType, LoginType.Legacy);
        settings.Set(AppSettingKeys.LoginLegacyName, "Steve");
        var pipeline = new CaptureLaunchPipelineService();
        var fileDialogs = new SaveFileDialogService(target);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
            pipeline,
            settings,
            fileDialogs,
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        await viewModel.ExportLaunchScriptAsync();

        Assert.Equal(0, pipeline.GenerateCalls);
        Assert.Equal(LoginType.Legacy, pipeline.LastRequest?.LoginType);
        Assert.Equal(string.Empty, viewModel.LaunchDiagnostics);
        Assert.Equal(1, pipeline.LaunchCalls);
        Assert.False(pipeline.LastRequest?.StartProcess);
        Assert.Equal(target, pipeline.LastRequest?.SaveBatchPath);
        Assert.Equal("LatestLaunch.bat", fileDialogs.LastDefaultFileName);
        Assert.Contains(target, viewModel.StatusMessage);
    }

    [Fact]
    public void LaunchPageViewModelExposesChineseComboOptionsWithOldValues()
    {
        using var temp = new TempDirectory();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            new AppSettingsService(new TestAppPathService(temp.Path)),
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        Assert.Equal([LoginType.Ms, LoginType.Legacy, LoginType.Nide, LoginType.Auth], viewModel.LoginTypeOptions.Select(option => option.Value));
        Assert.Contains(viewModel.LoginTypeOptions, option => option.DisplayName == "正版登录");
        Assert.False(viewModel.HasInstances);
        Assert.False(viewModel.HasSelectedInstance);
        Assert.True(viewModel.HasNoSelectedInstance);
        Assert.Equal("未找到可用的游戏版本", viewModel.CurrentVersionTitle);
        Assert.Contains("Minecraft", viewModel.CurrentVersionSubtitle);
        Assert.Equal(viewModel.LoginTypeOptions.Single(option => option.Value == LoginType.Legacy).DisplayName, viewModel.SelectedLoginTypeDisplayName);
        viewModel.SelectedLoginType = LoginType.Auth;
        Assert.Equal("Authlib-Injector", viewModel.SelectedLoginTypeDisplayName);
        Assert.Equal([0, 1, 2, 3, 4], viewModel.WindowTypeOptions.Select(option => option.Value));
        Assert.Equal([0, 2, 3, 4, 5], viewModel.LauncherVisibilityOptions.Select(option => option.Value));
        Assert.Equal([0, 1, 2, 3, 4], viewModel.LegacySkinTypeOptions.Select(option => option.Value));
        Assert.False(viewModel.IsLegacySkinIdVisible);
        Assert.False(viewModel.IsLegacySkinBrowseVisible);
        Assert.False(viewModel.IsLegacySkinSlimVisible);
        Assert.Contains("UUID", viewModel.LegacySkinSummary);
    }

    [Fact]
    public void LaunchPageViewModelSavesLegacySkinSettingsAndUsesFilePicker()
    {
        using var temp = new TempDirectory();
        var skinPath = Path.Combine(temp.Path, "skin.png");
        File.WriteAllText(skinPath, "png");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var fileDialogs = new SkinFileDialogService(skinPath);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            fileDialogs,
            new LegacyLoginService(),
            new NullLoggerService());

        viewModel.LaunchSkinType = 3;
        viewModel.LaunchSkinId = "Notch";

        Assert.True(viewModel.IsLegacySkinIdVisible);
        Assert.False(viewModel.IsLegacySkinBrowseVisible);
        Assert.Equal(3, settings.Get(AppSettingKeys.LaunchSkinType, 0));
        Assert.Equal("Notch", settings.Get(AppSettingKeys.LaunchSkinID, ""));

        viewModel.LaunchSkinType = 4;
        viewModel.LaunchSkinSlim = true;
        viewModel.BrowseLegacySkinCommand.Execute(null);

        Assert.True(viewModel.IsLegacySkinBrowseVisible);
        Assert.True(viewModel.IsLegacySkinSlimVisible);
        Assert.Equal(skinPath, viewModel.LaunchSkinId);
        Assert.Equal(skinPath, settings.Get(AppSettingKeys.LaunchSkinID, ""));
        Assert.True(settings.Get(AppSettingKeys.LaunchSkinSlim, false));
        Assert.Equal("", fileDialogs.LastInitialDirectory);
    }

    [Fact]
    public async Task LaunchPageShowsAndClearsCachedMicrosoftAccount()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh");
        settings.Set(AppSettingKeys.CacheMsV2Access, "access");
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, "{}");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "Alex");
        settings.Set(AppSettingKeys.CacheMsV2Expires, 123L);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            loginService: new CaptureLoginService(settings, new LoginSession(LoginType.Ms, "Alex", "uuid", "token", "client", "{}")));

        Assert.True(viewModel.IsMicrosoftLogin);
        Assert.True(viewModel.HasMicrosoftAccount);
        Assert.False(viewModel.HasValidMicrosoftAccessToken);
        Assert.True(viewModel.MicrosoftCacheNeedsRefresh);
        Assert.Equal("网页登录 / 换号", viewModel.MicrosoftLoginActionText);
        Assert.Contains("Client ID", viewModel.MicrosoftClientIdHelp);
        Assert.True(viewModel.LogoutMicrosoftAccountCommand.CanExecute(null));
        Assert.Contains("Alex", viewModel.MicrosoftAccountSummary);
        Assert.Contains("uuid", viewModel.MicrosoftAccountSummary);
        Assert.Contains("刷新授权", viewModel.MicrosoftReadinessSummary);
        Assert.True(viewModel.CanStartMicrosoftLogin);
        Assert.True(viewModel.CanRefreshMicrosoftLogin);
        Assert.Equal("", viewModel.MicrosoftLoginUnavailableReason);
        Assert.Equal("", viewModel.MicrosoftRefreshUnavailableReason);
        Assert.True(viewModel.RefreshMicrosoftAccountCommand.CanExecute(null));
        Assert.Contains("下次登录会刷新授权", viewModel.MicrosoftAccountSummary);

        await viewModel.LogoutMicrosoftAccountCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasMicrosoftAccount);
        Assert.False(viewModel.HasValidMicrosoftAccessToken);
        Assert.False(viewModel.MicrosoftCacheNeedsRefresh);
        Assert.False(viewModel.IsMicrosoftClientIdEditorVisible);
        viewModel.ToggleMicrosoftClientIdEditorCommand.Execute(null);
        Assert.True(viewModel.IsMicrosoftClientIdEditorVisible);
        Assert.Contains("收起", viewModel.MicrosoftClientIdEditorActionText);
        Assert.Equal("登录正版账号", viewModel.MicrosoftLoginActionText);
        Assert.False(viewModel.LogoutMicrosoftAccountCommand.CanExecute(null));
        Assert.Contains("尚未登录", viewModel.MicrosoftReadinessSummary);
        Assert.Contains("尚未登录", viewModel.MicrosoftAccountSummary);
        Assert.Equal("", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        Assert.Equal("", settings.Get(AppSettingKeys.CacheMsV2Access, ""));
        Assert.Equal("", settings.Get(AppSettingKeys.CacheMsV2ProfileJson, ""));
        Assert.Equal("", settings.Get(AppSettingKeys.CacheMsV2Uuid, ""));
        Assert.Equal("", settings.Get(AppSettingKeys.CacheMsV2Name, ""));
        Assert.Equal(0L, settings.Get<long>(AppSettingKeys.CacheMsV2Expires, -1));
        Assert.Equal("已退出正版账号", viewModel.StatusMessage);
    }

    [Fact]
    public void LaunchPageShowsValidMicrosoftCacheState()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2Access, "access");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "Alex");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        Assert.True(viewModel.HasMicrosoftAccount);
        Assert.True(viewModel.HasValidMicrosoftAccessToken);
        Assert.False(viewModel.MicrosoftCacheNeedsRefresh);
        Assert.False(viewModel.IsMicrosoftClientIdEditorVisible);
        viewModel.ToggleMicrosoftClientIdEditorCommand.Execute(null);
        Assert.True(viewModel.IsMicrosoftClientIdEditorVisible);
        Assert.Contains("收起", viewModel.MicrosoftClientIdEditorActionText);
        Assert.Contains("已就绪", viewModel.MicrosoftReadinessSummary);
        Assert.Contains("缓存仍有效", viewModel.MicrosoftAccountSummary);
        Assert.Equal("网页登录 / 换号", viewModel.MicrosoftLoginActionText);
    }

    [Fact]
    public void LaunchPageShowsValidMicrosoftCacheStateFromProfileJson()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2Access, "access");
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, """{"id":"json-uuid","name":"JsonAlex"}""");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        Assert.True(viewModel.HasMicrosoftAccount);
        Assert.True(viewModel.HasValidMicrosoftAccessToken);
        Assert.False(viewModel.MicrosoftCacheNeedsRefresh);
        Assert.Contains("JsonAlex", viewModel.MicrosoftAccountSummary);
        Assert.Contains("json-uuid", viewModel.MicrosoftAccountSummary);
        Assert.Contains("缓存仍有效", viewModel.MicrosoftAccountSummary);
    }

    [Fact]
    public void LaunchPageExplainsMissingMicrosoftClientId()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        var old = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "");
        try
        {
            var viewModel = new LaunchPageViewModel(
                new FakeMinecraftDiscoveryService(temp.Path, []),
                new FakeJavaDiscoveryService([]),
                new CaptureLaunchPipelineService(),
                settings,
                new NullFileDialogService(),
                new LegacyLoginService(),
                new NullLoggerService(),
                loginService: new CaptureLoginService(settings, new LoginSession(LoginType.Ms, "Alex", "uuid", "token", "client", "{}")));

            Assert.False(viewModel.HasMicrosoftClientId);
            Assert.Contains("Microsoft Client ID", viewModel.MicrosoftAccountSummary);
            Assert.Contains("PCL_MS_CLIENT_ID", viewModel.MicrosoftClientIdHelp);
            Assert.Contains("Microsoft Client ID", viewModel.MicrosoftReadinessSummary);
            Assert.False(viewModel.CanStartMicrosoftLogin);
            Assert.False(viewModel.CanRefreshMicrosoftLogin);
            Assert.Contains("PCL_MS_CLIENT_ID", viewModel.MicrosoftLoginUnavailableReason);
            Assert.Contains("PCL_MS_CLIENT_ID", viewModel.MicrosoftRefreshUnavailableReason);
            Assert.False(viewModel.LoginMicrosoftAccountCommand.CanExecute(null));
            Assert.True(viewModel.IsMicrosoftClientIdEditorVisible);
            Assert.Contains("收起", viewModel.MicrosoftClientIdEditorActionText);

            viewModel.MicrosoftClientId = "client";

            Assert.True(viewModel.HasMicrosoftClientId);
            Assert.True(viewModel.CanStartMicrosoftLogin);
            Assert.False(viewModel.CanRefreshMicrosoftLogin);
            Assert.Equal("", viewModel.MicrosoftLoginUnavailableReason);
            Assert.Contains("尚未缓存", viewModel.MicrosoftRefreshUnavailableReason);
            Assert.True(viewModel.LoginMicrosoftAccountCommand.CanExecute(null));
            Assert.Contains("Client ID", viewModel.MicrosoftClientIdHelp);
            Assert.Contains("尚未登录", viewModel.MicrosoftAccountSummary);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", old);
        }
    }

    [Fact]
    public async Task LaunchPageShowsMicrosoftDeviceCodeStatus()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        var deviceCodes = new MicrosoftDeviceCodeStatusService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            microsoftDeviceCodes: deviceCodes);

        Assert.False(viewModel.IsMicrosoftDeviceCodeActive);

        await deviceCodes.ShowAsync(new MicrosoftDeviceCodeInfo("ABCD-EFGH", "device", "https://microsoft.com/link", 900, 1, "OPEN WEB PAGE"));

        Assert.True(viewModel.IsMicrosoftDeviceCodeActive);
        Assert.Equal("ABCD-EFGH", viewModel.MicrosoftDeviceCode);
        Assert.Equal("https://microsoft.com/link", viewModel.MicrosoftDeviceCodeVerificationUri);
        Assert.Contains("WEB", viewModel.MicrosoftDeviceCodeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.MicrosoftDeviceCodeExpiresText.Length > 0);
        Assert.True(viewModel.OpenMicrosoftDeviceCodePageCommand.CanExecute(null));
        Assert.True(viewModel.CopyMicrosoftDeviceCodeCommand.CanExecute(null));

        deviceCodes.UpdateStatus("CONTINUE AUTHORIZATION");

        Assert.Contains("CONTINUE", viewModel.MicrosoftDeviceCodeMessage);

        deviceCodes.Clear();

        Assert.False(viewModel.IsMicrosoftDeviceCodeActive);
        Assert.Equal("", viewModel.MicrosoftDeviceCode);
        Assert.False(viewModel.OpenMicrosoftDeviceCodePageCommand.CanExecute(null));
        Assert.False(viewModel.CopyMicrosoftDeviceCodeCommand.CanExecute(null));
    }

    [Fact]
    public async Task LaunchPageCopiesMicrosoftDeviceCodeThroughClipboardService()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        var deviceCodes = new MicrosoftDeviceCodeStatusService();
        var clipboard = new CaptureClipboardService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            microsoftDeviceCodes: deviceCodes,
            clipboard: clipboard);

        await deviceCodes.ShowAsync(new MicrosoftDeviceCodeInfo("ABCD-EFGH", "device", "https://microsoft.com/link", 900, 1, "OPEN WEB PAGE"));
        viewModel.CopyMicrosoftDeviceCodeCommand.Execute(null);

        Assert.Equal("ABCD-EFGH", clipboard.LastText);
        Assert.Contains("已复制", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchPageCanCancelMicrosoftDeviceCodeLogin()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        var deviceCodes = new MicrosoftDeviceCodeStatusService();
        var login = new BlockingLoginService(deviceCodes);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            loginService: login,
            microsoftDeviceCodes: deviceCodes);

        var loginTask = viewModel.LoginMicrosoftAccountAsync();
        await login.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.IsMicrosoftDeviceCodeActive);
        Assert.True(viewModel.CancelBusyCommand.CanExecute(null));

        viewModel.CancelBusyCommand.Execute(null);
        await loginTask;

        Assert.True(login.WasCanceled);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsMicrosoftDeviceCodeActive);
        Assert.Equal("已取消正版网页登录", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchPageBlocksMicrosoftLaunchBeforeClientIdAndLogin()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        var old = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "");
        var pipeline = new CaptureLaunchPipelineService();
        try
        {
            var viewModel = new LaunchPageViewModel(
                new FakeMinecraftDiscoveryService(temp.Path, [instance]),
                new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
                pipeline,
                settings,
                new NullFileDialogService(),
                new LegacyLoginService(),
                new NullLoggerService());

            await viewModel.InitializeAsync();
            await viewModel.LaunchGameAsync();

            Assert.Equal(0, pipeline.LaunchCalls);
            Assert.Contains("MicrosoftClientIdMissing", viewModel.LaunchDiagnostics);
            Assert.Contains("PCL_MS_CLIENT_ID", viewModel.LaunchDiagnostics);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", old);
        }
    }

    [Fact]
    public async Task LaunchPageAllowsMicrosoftPipelineToLoginWhenClientIdConfigured()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
            pipeline,
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        await viewModel.GenerateProfileAsync();

        Assert.Equal(1, pipeline.GenerateCalls);
        Assert.Equal(LoginType.Ms, pipeline.LastRequest?.LoginType);
        Assert.Equal(string.Empty, viewModel.LaunchDiagnostics);
    }

    [Fact]
    public async Task LaunchPageAllowsMicrosoftScriptExportWhenClientIdConfigured()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var target = Path.Combine(temp.Path, "LatestLaunch.bat");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        var pipeline = new CaptureLaunchPipelineService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
            pipeline,
            settings,
            new SaveFileDialogService(target),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        await viewModel.ExportLaunchScriptAsync();

        Assert.Equal(0, pipeline.GenerateCalls);
        Assert.Equal(1, pipeline.LaunchCalls);
        Assert.Equal(LoginType.Ms, pipeline.LastRequest?.LoginType);
        Assert.False(pipeline.LastRequest?.StartProcess);
        Assert.Equal(target, pipeline.LastRequest?.SaveBatchPath);
        Assert.Equal(string.Empty, viewModel.LaunchDiagnostics);
    }

    [Fact]
    public async Task LaunchPageAllowsMicrosoftLaunchWithValidCacheWithoutClientId()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.CacheMsV2Access, "cached-access");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "cached-uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "CachedAlex");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var old = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "");
        var pipeline = new CaptureLaunchPipelineService();
        try
        {
            var viewModel = new LaunchPageViewModel(
                new FakeMinecraftDiscoveryService(temp.Path, [instance]),
                new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
                pipeline,
                settings,
                new NullFileDialogService(),
                new LegacyLoginService(),
                new NullLoggerService());

            await viewModel.InitializeAsync();
            await viewModel.LaunchGameAsync();

            Assert.Equal(1, pipeline.LaunchCalls);
            Assert.Equal(LoginType.Ms, pipeline.LastRequest?.LoginType);
            Assert.Equal(string.Empty, viewModel.LaunchDiagnostics);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", old);
        }
    }

    [Fact]
    public async Task LaunchPageCanLoginMicrosoftAccountBeforeLaunching()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        var login = new CaptureLoginService(settings, new LoginSession(LoginType.Ms, "Alex", "uuid", "token", "uuid", "{}"));
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            loginService: login);

        Assert.True(viewModel.LoginMicrosoftAccountCommand.CanExecute(null));

        await viewModel.LoginMicrosoftAccountCommand.ExecuteAsync(null);

        Assert.Equal(1, login.Calls);
        Assert.Equal(LoginType.Ms, login.LastRequest?.Type);
        Assert.True(viewModel.HasMicrosoftAccount);
        Assert.Contains("Alex", viewModel.MicrosoftAccountSummary);
        Assert.Contains("Alex", viewModel.StatusMessage);
        Assert.Equal("uuid", settings.Get(AppSettingKeys.CacheMsV2Uuid, ""));
    }

    [Fact]
    public async Task LaunchPageForcesMicrosoftReloginWhenCachedAccountExists()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "old-refresh");
        settings.Set(AppSettingKeys.CacheMsV2Access, "old-access");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "old-uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "OldAlex");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var login = new CaptureLoginService(settings, new LoginSession(LoginType.Ms, "NewAlex", "new-uuid", "new-token", "new-uuid", "{}"));
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            loginService: login);

        await viewModel.LoginMicrosoftAccountCommand.ExecuteAsync(null);

        Assert.Equal(1, login.Calls);
        Assert.True(login.LastRequest?.ForceNewLogin);
        Assert.Equal("new-uuid", settings.Get(AppSettingKeys.CacheMsV2Uuid, ""));
        Assert.Contains("NewAlex", viewModel.MicrosoftAccountSummary);
    }

    [Fact]
    public async Task LaunchPageRefreshesMicrosoftAccountWithoutForcingRelogin()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.MicrosoftClientId, "client");
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "old-refresh");
        settings.Set(AppSettingKeys.CacheMsV2Access, "old-access");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "old-uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "OldAlex");
        settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());
        var login = new CaptureLoginService(settings, new LoginSession(LoginType.Ms, "RefreshedAlex", "refreshed-uuid", "new-token", "refreshed-uuid", "{}"));
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            loginService: login);

        Assert.True(viewModel.RefreshMicrosoftAccountCommand.CanExecute(null));

        await viewModel.RefreshMicrosoftAccountCommand.ExecuteAsync(null);

        Assert.Equal(1, login.Calls);
        Assert.False(login.LastRequest?.ForceNewLogin);
        Assert.Equal("refreshed-uuid", settings.Get(AppSettingKeys.CacheMsV2Uuid, ""));
        Assert.Contains("RefreshedAlex", viewModel.MicrosoftAccountSummary);
        Assert.Contains("刷新完成", viewModel.StatusMessage);
    }

    [Fact]
    public void LaunchPageSwitchesCachedMicrosoftAccount()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.CacheMsV2AccountsJson, JsonSerializer.Serialize(new[]
        {
            new MicrosoftAccountCacheEntry("first-uuid", "FirstAlex", "first-refresh", "first-access", 10, """{"id":"first-uuid","name":"FirstAlex"}""", DateTimeOffset.UtcNow.AddMinutes(-5)),
            new MicrosoftAccountCacheEntry("second-uuid", "SecondAlex", "second-refresh", "second-access", 20, """{"id":"second-uuid","name":"SecondAlex"}""", DateTimeOffset.UtcNow)
        }));
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        viewModel.SelectedMicrosoftAccount = viewModel.MicrosoftAccounts.Single(account => account.Name == "FirstAlex");
        viewModel.SwitchMicrosoftAccountCommand.Execute(null);
        var accounts = JsonSerializer.Deserialize<MicrosoftAccountCacheEntry[]>(settings.Get(AppSettingKeys.CacheMsV2AccountsJson, ""))!;

        Assert.True(viewModel.HasMicrosoftAccountHistory);
        Assert.True(viewModel.HasMicrosoftAccount);
        Assert.Equal("first-refresh", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        Assert.Equal("first-access", settings.Get(AppSettingKeys.CacheMsV2Access, ""));
        Assert.Equal("first-uuid", settings.Get(AppSettingKeys.CacheMsV2Uuid, ""));
        Assert.Equal("FirstAlex", accounts[0].Name);
        Assert.Equal("SecondAlex", accounts[1].Name);
        Assert.Contains("FirstAlex", viewModel.MicrosoftAccountSummary);
        Assert.Contains("已切换正版账号", viewModel.StatusMessage);
    }

    [Fact]
    public void LaunchPageDeletesSelectedMicrosoftAccountAndClearsCurrentAccount()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "first-refresh");
        settings.Set(AppSettingKeys.CacheMsV2Access, "first-access");
        settings.Set(AppSettingKeys.CacheMsV2Uuid, "first-uuid");
        settings.Set(AppSettingKeys.CacheMsV2Name, "FirstAlex");
        settings.Set(AppSettingKeys.CacheMsV2Expires, 10L);
        settings.Set(AppSettingKeys.CacheMsV2ProfileJson, """{"id":"first-uuid","name":"FirstAlex"}""");
        settings.Set(AppSettingKeys.CacheMsV2AccountsJson, JsonSerializer.Serialize(new[]
        {
            new MicrosoftAccountCacheEntry("first-uuid", "FirstAlex", "first-refresh", "first-access", 10, """{"id":"first-uuid","name":"FirstAlex"}""", DateTimeOffset.UtcNow),
            new MicrosoftAccountCacheEntry("second-uuid", "SecondAlex", "second-refresh", "second-access", 20, """{"id":"second-uuid","name":"SecondAlex"}""", DateTimeOffset.UtcNow.AddMinutes(-1))
        }));
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        viewModel.SelectedMicrosoftAccount = viewModel.MicrosoftAccounts.Single(account => account.Name == "FirstAlex");
        viewModel.DeleteMicrosoftAccountCommand.Execute(null);
        var accounts = JsonSerializer.Deserialize<MicrosoftAccountCacheEntry[]>(settings.Get(AppSettingKeys.CacheMsV2AccountsJson, ""))!;

        Assert.False(viewModel.HasMicrosoftAccount);
        Assert.Single(accounts);
        Assert.Equal("SecondAlex", accounts[0].Name);
        Assert.Equal("", settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""));
        Assert.Equal("", settings.Get(AppSettingKeys.CacheMsV2Access, ""));
        Assert.Equal("", settings.Get(AppSettingKeys.CacheMsV2Uuid, ""));
        Assert.Contains("已删除正版账号缓存", viewModel.StatusMessage);
    }

    [Fact]
    public void LaunchPageReadsAndDeletesOldPclMicrosoftAccountCache()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.LoginMsJson, """{"LegacyAlex":"legacy-refresh","OtherAlex":"other-refresh"}""");
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        Assert.Contains(viewModel.MicrosoftAccounts, account => account.Name == "LegacyAlex" && account.RefreshToken == "legacy-refresh");

        viewModel.SelectedMicrosoftAccount = viewModel.MicrosoftAccounts.Single(account => account.Name == "LegacyAlex");
        viewModel.DeleteMicrosoftAccountCommand.Execute(null);
        using var legacyJson = JsonDocument.Parse(settings.Get(AppSettingKeys.LoginMsJson, "{}"));

        Assert.False(legacyJson.RootElement.TryGetProperty("LegacyAlex", out _));
        Assert.Equal("other-refresh", legacyJson.RootElement.GetProperty("OtherAlex").GetString());
    }

    [Theory]
    [InlineData(LoginType.Nide, "Nide", "统一通行证")]
    [InlineData(LoginType.Auth, "Auth", "Authlib-Injector")]
    public async Task LaunchPageShowsAndClearsCachedServerAccount(LoginType loginType, string prefix, string displayName)
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, loginType);
        settings.Set("Cache" + prefix + "Access", "access");
        settings.Set("Cache" + prefix + "Client", "client");
        settings.Set("Cache" + prefix + "Uuid", "uuid");
        settings.Set("Cache" + prefix + "Name", "Player");
        settings.Set("Cache" + prefix + "Username", "email");
        settings.Set("Cache" + prefix + "Pass", "password");
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        Assert.True(viewModel.IsServerLogin);
        Assert.True(viewModel.HasServerAccount);
        Assert.True(viewModel.LogoutServerAccountCommand.CanExecute(null));
        Assert.Contains(displayName, viewModel.ServerAccountSummary);
        Assert.Contains("Player", viewModel.ServerAccountSummary);
        Assert.Contains("uuid", viewModel.ServerAccountSummary);

        await viewModel.LogoutServerAccountCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasServerAccount);
        Assert.False(viewModel.LogoutServerAccountCommand.CanExecute(null));
        Assert.Contains("尚未登录", viewModel.ServerAccountSummary);
        Assert.Equal("", settings.Get("Cache" + prefix + "Access", ""));
        Assert.Equal("", settings.Get("Cache" + prefix + "Client", ""));
        Assert.Equal("", settings.Get("Cache" + prefix + "Uuid", ""));
        Assert.Equal("", settings.Get("Cache" + prefix + "Name", ""));
        Assert.Equal("", settings.Get("Cache" + prefix + "Username", ""));
        Assert.Equal("", settings.Get("Cache" + prefix + "Pass", ""));
        Assert.Contains("退出", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchPageCanLoginServerAccountBeforeLaunching()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LoginType, LoginType.Auth);
        var login = new CaptureLoginService(settings, new LoginSession(LoginType.Auth, "Player", "uuid", "access", "client", "{}"));
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, []),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            loginService: login);
        viewModel.LoginUserName = "mail@example.com";
        viewModel.LoginPassword = "password";
        viewModel.LoginServer = "https://auth.example";

        Assert.True(viewModel.LoginServerAccountCommand.CanExecute(null));

        await viewModel.LoginServerAccountCommand.ExecuteAsync(null);

        Assert.Equal(1, login.Calls);
        Assert.Equal(LoginType.Auth, login.LastRequest?.Type);
        Assert.Equal("mail@example.com", login.LastRequest?.UserName);
        Assert.Equal("https://auth.example", login.LastRequest?.Server);
        Assert.True(viewModel.HasServerAccount);
        Assert.Contains("Player", viewModel.ServerAccountSummary);
        Assert.Contains("Player", viewModel.StatusMessage);
        Assert.Equal("uuid", settings.Get(AppSettingKeys.CacheAuthUuid, ""));
    }

    [Fact]
    public async Task LaunchPageViewModelSelectsVersionThroughOldPclStyleSelector()
    {
        using var temp = new TempDirectory();
        var first = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var second = WriteVersion(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "managed-elsewhere");
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [first, second]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        Assert.True(viewModel.HasInstances);
        Assert.True(viewModel.HasSelectedInstance);
        Assert.False(viewModel.HasNoSelectedInstance);
        Assert.Equal("1.20.1", viewModel.CurrentVersionTitle);
        Assert.Equal("managed-elsewhere", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        var initialCurrent = Assert.Single(viewModel.VersionSelectorRows, row => row.IsCurrentLaunchVersion);
        Assert.Equal("1.20.1", initialCurrent.Instance?.Name);
        Assert.Equal("/Resources/Images/ReleaseTypes/Release.png", initialCurrent.IconPath);
        Assert.Equal("正式版", initialCurrent.IconDescription);

        viewModel.OpenVersionSelectorCommand.Execute(null);
        viewModel.SelectVersionCommand.Execute(second);

        Assert.False(viewModel.IsVersionSelectorOpen);
        Assert.Equal("1.19.4", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.19.4", viewModel.CurrentVersionTitle);
        Assert.Equal("1.19.4", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("managed-elsewhere", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        var current = Assert.Single(viewModel.VersionSelectorRows, row => row.IsCurrentLaunchVersion);
        Assert.Equal("1.19.4", current.Instance?.Name);
        Assert.Equal("当前启动", current.CurrentLaunchText);
        Assert.Contains("已设为启动版本", viewModel.StatusMessage);

        viewModel.PrepareSelectedVersionForManagement();

        Assert.Equal("1.19.4", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
    }

    [Fact]
    public async Task LaunchPageVersionSelectorSelectsVersionWhenWholeRowIsSelected()
    {
        using var temp = new TempDirectory();
        var first = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var second = WriteVersion(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [first, second]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        viewModel.OpenVersionSelectorCommand.Execute(null);
        var row = Assert.Single(viewModel.VersionSelectorRows, item => item.Instance?.Name == "1.19.4");

        viewModel.SelectedVersionSelectorRow = row;

        Assert.False(viewModel.IsVersionSelectorOpen);
        Assert.Equal("1.19.4", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.19.4", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
    }

    [Fact]
    public async Task LaunchPageVersionSelectorDoesNotRebuildRowsWhenSelectingVersion()
    {
        using var temp = new TempDirectory();
        var first = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var second = WriteVersion(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, first.Name);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [first, second]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        viewModel.OpenVersionSelectorCommand.Execute(null);
        var firstRow = viewModel.VersionSelectorRows.First();
        var rowCount = viewModel.VersionSelectorRows.Count;

        viewModel.SelectVersionCommand.Execute(second);

        Assert.False(viewModel.IsVersionSelectorOpen);
        Assert.Equal(second.Name, viewModel.SelectedInstance?.Name);
        Assert.Equal(rowCount, viewModel.VersionSelectorRows.Count);
        Assert.Same(firstRow, viewModel.VersionSelectorRows.First());
    }

    [Fact]
    public async Task LaunchPageVersionSelectorDoesNotSelectBrokenVersion()
    {
        using var temp = new TempDirectory();
        var ready = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var brokenPath = Path.Combine(temp.Path, "versions", "broken");
        Directory.CreateDirectory(brokenPath);
        var broken = new MinecraftDiscoveryService().InspectInstance(
            temp.Path,
            brokenPath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1.20.1", "broken" });
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [ready, broken]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        viewModel.OpenVersionSelectorCommand.Execute(null);

        Assert.True(broken.HasError);
        Assert.Equal(MinecraftInstanceState.MissingJson, broken.State);

        viewModel.SelectVersionCommand.Execute(broken);

        Assert.True(viewModel.IsVersionSelectorOpen);
        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        var current = Assert.Single(viewModel.VersionSelectorRows, row => row.IsCurrentLaunchVersion);
        Assert.Equal("1.20.1", current.Instance?.Name);
        Assert.Contains("无法设为启动版本", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchPageVersionSelectorCanOpenBrokenVersionFolderLikeOldPcl()
    {
        using var temp = new TempDirectory();
        var ready = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var brokenPath = Path.Combine(temp.Path, "versions", "broken");
        Directory.CreateDirectory(brokenPath);
        var broken = new MinecraftDiscoveryService().InspectInstance(
            temp.Path,
            brokenPath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1.20.1", "broken" });
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        var folders = new CaptureFolderOpenService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [ready, broken]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            folders: folders);

        await viewModel.InitializeAsync();
        viewModel.OpenVersionSelectorCommand.Execute(null);

        viewModel.OpenVersionFolderCommand.Execute(broken);

        Assert.Equal([broken.VersionPath], folders.OpenedPaths);
        Assert.True(viewModel.IsVersionSelectorOpen);
        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);
        Assert.Contains("broken", viewModel.StatusMessage);
    }

    [Fact]
    public async Task MainWindowNavigatingFromLaunchToInstancePreparesCurrentLaunchVersionForManagement()
    {
        using var temp = new TempDirectory();
        var first = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var second = WriteVersion(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.LastRoute, PageRoute.Launch);
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "managed-elsewhere");
        var launchPage = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [first, second]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());
        await launchPage.InitializeAsync();
        launchPage.SelectVersionCommand.Execute(second);
        var navigation = new FakeNavigationService(launchPage, new TestPageViewModel(PageRoute.Instance));
        var mainWindow = new MainWindowViewModel(navigation, settings, new NullLoggerService());

        mainWindow.NavigateCommand.Execute(PageRoute.Instance);

        Assert.Equal("1.19.4", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal(PageRoute.Instance, mainWindow.SelectedRoute);
        Assert.Equal(PageRoute.Instance, mainWindow.CurrentPage.Route);
    }

    [Fact]
    public async Task MainWindowOpenInstanceManagementCommandNavigatesToInstanceWithoutChangingLaunchVersion()
    {
        using var temp = new TempDirectory();
        var first = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var second = WriteVersion(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.LastRoute, PageRoute.Launch);
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "managed-elsewhere");
        var launchPage = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [first, second]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());
        await launchPage.InitializeAsync();
        launchPage.SelectVersionCommand.Execute(second);
        var navigation = new FakeNavigationService(launchPage, new TestPageViewModel(PageRoute.Instance));
        var mainWindow = new MainWindowViewModel(navigation, settings, new NullLoggerService());
        launchPage.OpenVersionSelectorCommand.Execute(null);
        Assert.True(launchPage.IsVersionSelectorOpen);

        mainWindow.OpenInstanceManagementCommand.Execute(first);

        Assert.Equal("1.19.4", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal("1.19.4", launchPage.SelectedInstance?.Name);
        Assert.False(launchPage.IsVersionSelectorOpen);
        Assert.Equal(PageRoute.Instance, mainWindow.SelectedRoute);
        Assert.Equal(PageRoute.Instance, mainWindow.CurrentPage.Route);
    }

    [Fact]
    public void MainWindowOpenDownloadManagementCommandNavigatesToDownloadPage()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LastRoute, PageRoute.Launch);
        var launchPage = new TestPageViewModel(PageRoute.Launch);
        var instancePage = new TestPageViewModel(PageRoute.Instance);
        var downloadPage = new TestPageViewModel(PageRoute.Download);
        var navigation = new FakeNavigationService(launchPage, instancePage, downloadPage);
        var mainWindow = new MainWindowViewModel(navigation, settings, new NullLoggerService());

        mainWindow.OpenDownloadManagementCommand.Execute(null);

        Assert.Equal(PageRoute.Download, mainWindow.SelectedRoute);
        Assert.Same(downloadPage, mainWindow.CurrentPage);
        Assert.Equal(PageRoute.Download, settings.Get(AppSettingKeys.LastRoute, PageRoute.Launch));
    }

    [Fact]
    public async Task MainWindowOpenInstanceManagementCommandSelectsTargetVersionOnRealInstancePage()
    {
        using var temp = new TempDirectory();
        var first = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var second = WriteVersion(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var appData = Path.Combine(temp.Path, "appdata");
        var settings = new AppSettingsService(new TestAppPathService(appData));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.LastRoute, PageRoute.Launch);
        settings.Set(AppSettingKeys.SelectedInstanceName, first.Name);
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, first.Name);
        var logger = new NullLoggerService();
        var instanceManagement = new TestMinecraftInstanceManagementService();
        var launchPage = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [first, second]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            logger,
            instanceManagement: instanceManagement);
        var instancePage = new InstancePageViewModel(
            new MinecraftDiscoveryService(instanceManagement),
            instanceManagement,
            new LaunchFileCompleter(),
            new CaptureLaunchPipelineService(),
            new DownloadManagerService(new FakeDownloadByteClient(), new FileCheckService(logger), logger),
            null,
            settings,
            new NullFileDialogService(),
            new TestPromptService(confirm: true),
            logger);
        await launchPage.InitializeAsync();
        launchPage.OpenVersionSelectorCommand.Execute(null);
        var navigation = new FakeNavigationService(launchPage, instancePage);
        var mainWindow = new MainWindowViewModel(navigation, settings, logger);

        mainWindow.OpenInstanceManagementCommand.Execute(second);
        await instancePage.OnNavigatedToAsync();

        Assert.False(launchPage.IsVersionSelectorOpen);
        Assert.Equal(PageRoute.Instance, mainWindow.SelectedRoute);
        Assert.Same(instancePage, mainWindow.CurrentPage);
        Assert.Equal(first.Name, settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal(first.Name, new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Equal(second.Name, settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal(second.Name, instancePage.SelectedInstance?.Name);
        Assert.False(instancePage.IsSelectedInstanceLaunchVersion);
        Assert.Contains("正在管理：1.19.4", instancePage.VersionManagementSummary);
        Assert.Contains(instancePage.InstanceRows, row => row.Instance?.Name == first.Name && row.IsLaunchVersion);
        Assert.Contains(instancePage.InstanceRows, row => row.Instance?.Name == second.Name && row.IsManagedVersion);
    }

    [Fact]
    public async Task MainWindowF11TogglesHiddenVersionsOnLaunchSelectorLikeOldPcl()
    {
        using var temp = new TempDirectory();
        var visible = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var hidden = WriteVersion(temp.Path, "hidden-1.19.4", """
        { "id": "hidden-1.19.4", "type": "release", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """) with { DisplayType = MinecraftInstanceDisplayType.Hidden };
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var launchPage = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [visible, hidden]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());
        await launchPage.InitializeAsync();
        launchPage.OpenVersionSelectorCommand.Execute(null);
        var navigation = new FakeNavigationService(launchPage, new TestPageViewModel(PageRoute.Instance));
        var mainWindow = new MainWindowViewModel(navigation, settings, new NullLoggerService());

        Assert.False(launchPage.ShowHiddenVersions);
        Assert.DoesNotContain(launchPage.VersionSelectorRows, row => row.Instance?.Name == "hidden-1.19.4");

        mainWindow.ToggleHiddenVersionsCommand.Execute(null);

        Assert.True(launchPage.ShowHiddenVersions);
        Assert.Contains(launchPage.VersionSelectorRows, row => row.Instance?.Name == "hidden-1.19.4");
        Assert.Contains("隐藏版本", mainWindow.StatusText);

        mainWindow.ToggleHiddenVersionsCommand.Execute(null);

        Assert.False(launchPage.ShowHiddenVersions);
        Assert.DoesNotContain(launchPage.VersionSelectorRows, row => row.Instance?.Name == "hidden-1.19.4");
        Assert.Contains("可用版本", mainWindow.StatusText);
    }

    [Fact]
    public async Task LaunchPageVersionSelectorGroupsSearchesAndShowsHiddenVersionsLikeOldPcl()
    {
        using var temp = new TempDirectory();
        var vanilla = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var starredFabric = WriteVersion(temp.Path, "fabric-1.20.1", """
        { "id": "fabric-1.20.1", "inheritsFrom": "1.20.1", "releaseTime": "2023-06-13T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """) with { State = MinecraftInstanceState.Ready, ErrorMessage = "", IsStar = true };
        var hidden = WriteVersion(temp.Path, "hidden-1.19.4", """
        { "id": "hidden-1.19.4", "type": "release", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """) with { DisplayType = MinecraftInstanceDisplayType.Hidden };
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [vanilla, starredFabric, hidden]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasVersionSelectorRows);
        Assert.Equal(2, viewModel.VersionSelectorVisibleCount);
        Assert.Contains("显示 2 / 2 个可用版本", viewModel.VersionSelectorSummary);
        Assert.Contains(viewModel.VersionSelectorRows, row => row.IsHeader && row.GroupTitle == "收藏夹");
        Assert.Contains(viewModel.VersionSelectorRows, row => row.IsHeader && row.GroupTitle.StartsWith("Fabric", StringComparison.Ordinal));
        Assert.Equal(2, viewModel.VersionSelectorRows.Count(row => row.Instance?.Name == "fabric-1.20.1"));
        Assert.DoesNotContain(viewModel.VersionSelectorRows, row => row.Instance?.Name == "hidden-1.19.4");

        viewModel.VersionSearchText = "fabric";
        Assert.Equal(1, viewModel.VersionSelectorVisibleCount);
        Assert.Contains("fabric", viewModel.VersionSelectorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.All(viewModel.VersionSelectorRows.Where(row => row.IsSelectable), row => Assert.Contains("fabric", row.Name, StringComparison.OrdinalIgnoreCase));

        viewModel.VersionSearchText = "missing";
        Assert.False(viewModel.HasVersionSelectorRows);
        Assert.Equal("没有匹配当前搜索条件的版本。", viewModel.VersionSelectorEmptyText);

        viewModel.VersionSearchText = "";
        viewModel.ShowHiddenVersions = true;
        Assert.Equal(1, viewModel.VersionSelectorVisibleCount);
        Assert.Contains("隐藏版本", viewModel.VersionSelectorSummary);
        Assert.Contains(viewModel.VersionSelectorRows, row => row.IsHeader && row.GroupTitle.Contains("(1)", StringComparison.Ordinal));
        var hiddenRow = Assert.Single(viewModel.VersionSelectorRows, row => row.IsSelectable);
        Assert.Equal("hidden-1.19.4", hiddenRow.Instance?.Name);
    }

    [Fact]
    public async Task LaunchPageVersionSelectorSortsVersionsAndPersistsMode()
    {
        using var temp = new TempDirectory();
        var alpha = WriteVersion(temp.Path, "Alpha", """
        { "id": "Alpha", "type": "release", "releaseTime": "2023-01-01T12:00:00+00:00", "libraries": [] }
        """);
        var beta = WriteVersion(temp.Path, "Beta", """
        { "id": "Beta", "type": "release", "releaseTime": "2023-02-01T12:00:00+00:00", "libraries": [] }
        """);
        var gamma = WriteVersion(temp.Path, "Gamma", """
        { "id": "Gamma", "type": "release", "releaseTime": "2023-03-01T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [alpha, beta, gamma]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();

        Assert.Equal(new[] { "Gamma", "Beta", "Alpha" }, viewModel.VersionSelectorRows.Where(row => row.IsSelectable).Select(row => row.Name));

        viewModel.VersionSortMode = 1;

        Assert.Equal(1, settings.Get(AppSettingKeys.VersionSortMode, 0));
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, viewModel.VersionSelectorRows.Where(row => row.IsSelectable).Select(row => row.Name));
    }

    [Fact]
    public async Task LaunchPageVersionSelectorSyncsSharedSortModeWhenNavigatedBack()
    {
        using var temp = new TempDirectory();
        var alpha = WriteVersion(temp.Path, "Alpha", """
        { "id": "Alpha", "type": "release", "releaseTime": "2023-03-01T12:00:00+00:00", "libraries": [] }
        """);
        var beta = WriteVersion(temp.Path, "Beta", """
        { "id": "Beta", "type": "release", "releaseTime": "2023-01-01T12:00:00+00:00", "libraries": [] }
        """);
        var gamma = WriteVersion(temp.Path, "Gamma", """
        { "id": "Gamma", "type": "release", "releaseTime": "2023-02-01T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [alpha, beta, gamma]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        Assert.Equal(new[] { "Alpha", "Gamma", "Beta" }, viewModel.VersionSelectorRows.Where(row => row.IsSelectable).Select(row => row.Name));

        settings.Set(AppSettingKeys.VersionSortMode, 1);
        await viewModel.OnNavigatedToAsync();

        Assert.Equal(1, viewModel.VersionSortMode);
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, viewModel.VersionSelectorRows.Where(row => row.IsSelectable).Select(row => row.Name));
    }

    [Fact]
    public async Task LaunchPageVersionSelectorRevealsHiddenCurrentLaunchVersion()
    {
        using var temp = new TempDirectory();
        var vanilla = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var hidden = WriteVersion(temp.Path, "hidden-1.19.4", """
        { "id": "hidden-1.19.4", "type": "release", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """) with { DisplayType = MinecraftInstanceDisplayType.Hidden };
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "hidden-1.19.4");
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [vanilla, hidden]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();

        Assert.Equal("hidden-1.19.4", viewModel.SelectedInstance?.Name);
        Assert.False(viewModel.ShowHiddenVersions);
        Assert.DoesNotContain(viewModel.VersionSelectorRows, row => row.Instance?.Name == "hidden-1.19.4");

        viewModel.OpenVersionSelectorCommand.Execute(null);

        Assert.True(viewModel.IsVersionSelectorOpen);
        Assert.True(viewModel.ShowHiddenVersions);
        var current = Assert.Single(viewModel.VersionSelectorRows, row => row.IsCurrentLaunchVersion);
        Assert.Equal("hidden-1.19.4", current.Instance?.Name);
        Assert.Equal("当前启动", current.CurrentLaunchText);
    }

    [Fact]
    public async Task LaunchPageVersionSelectorClearsSearchWhenCurrentLaunchVersionIsFilteredOut()
    {
        using var temp = new TempDirectory();
        var vanilla = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var fabric = WriteVersion(temp.Path, "fabric-1.20.1", """
        { "id": "fabric-1.20.1", "inheritsFrom": "1.20.1", "releaseTime": "2023-06-13T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [vanilla, fabric]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();
        viewModel.VersionSearchText = "fabric";

        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);
        Assert.DoesNotContain(viewModel.VersionSelectorRows, row => row.Instance?.Name == "1.20.1");

        viewModel.OpenVersionSelectorCommand.Execute(null);

        Assert.True(viewModel.IsVersionSelectorOpen);
        Assert.Equal("", viewModel.VersionSearchText);
        var current = Assert.Single(viewModel.VersionSelectorRows, row => row.IsCurrentLaunchVersion);
        Assert.Equal("1.20.1", current.Instance?.Name);
        Assert.Equal("当前启动", current.CurrentLaunchText);
    }

    [Fact]
    public async Task LaunchPageVersionSelectorCanToggleStarLikeOldPcl()
    {
        using var temp = new TempDirectory();
        WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = new LaunchPageViewModel(
            new MinecraftDiscoveryService(),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            instanceManagement: new MinecraftInstanceManagementService());

        await viewModel.InitializeAsync();
        viewModel.OpenVersionSelectorCommand.Execute(null);
        var version = Assert.Single(viewModel.VersionSelectorRows, row => row.IsSelectable).Instance!;

        await viewModel.ToggleVersionStarCommand.ExecuteAsync(version);

        var setupPath = Path.Combine(temp.Path, "versions", "1.20.1", "PCL", "Setup.ini");
        Assert.Contains("IsStar:True", File.ReadAllText(setupPath));
        Assert.True(viewModel.IsVersionSelectorOpen);
        Assert.Contains(viewModel.VersionSelectorRows, row => row.IsHeader && row.GroupTitle == "收藏夹");
        Assert.Contains(viewModel.VersionSelectorRows, row => row.Instance?.Name == "1.20.1" && row.StarActionText == "取消收藏");
        Assert.Contains("已加入收藏夹", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchPageVersionSelectorCanHideAndUnhideCurrentVersionLikeOldPcl()
    {
        using var temp = new TempDirectory();
        WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        var viewModel = new LaunchPageViewModel(
            new MinecraftDiscoveryService(),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            instanceManagement: new MinecraftInstanceManagementService());

        await viewModel.InitializeAsync();
        viewModel.OpenVersionSelectorCommand.Execute(null);
        var version = Assert.Single(viewModel.VersionSelectorRows, row => row.IsSelectable).Instance!;

        await viewModel.ToggleVersionHiddenCommand.ExecuteAsync(version);

        var setupPath = Path.Combine(temp.Path, "versions", "1.20.1", "PCL", "Setup.ini");
        Assert.Contains("DisplayType:1", File.ReadAllText(setupPath));
        Assert.True(viewModel.IsVersionSelectorOpen);
        Assert.True(viewModel.ShowHiddenVersions);
        var hiddenRow = Assert.Single(viewModel.VersionSelectorRows, row => row.Instance?.Name == "1.20.1");
        Assert.Equal("取消隐藏", hiddenRow.HiddenActionText);
        Assert.True(hiddenRow.Instance?.IsHidden);
        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);

        await viewModel.ToggleVersionHiddenCommand.ExecuteAsync(hiddenRow.Instance);

        Assert.Contains("DisplayType:0", File.ReadAllText(setupPath));
        Assert.False(viewModel.ShowHiddenVersions);
        var visibleRow = Assert.Single(viewModel.VersionSelectorRows, row => row.Instance?.Name == "1.20.1");
        Assert.Equal("隐藏", visibleRow.HiddenActionText);
        Assert.False(visibleRow.Instance?.IsHidden);
    }

    [Fact]
    public async Task LaunchPageVersionSelectorCanDeleteVersionAfterConfirmation()
    {
        using var temp = new TempDirectory();
        WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersion(temp.Path, "1.19.4", """
        { "id": "1.19.4", "type": "release", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        var viewModel = new LaunchPageViewModel(
            new MinecraftDiscoveryService(),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            prompts: new TestPromptService(confirm: true),
            instanceManagement: new TestMinecraftInstanceManagementService());

        await viewModel.InitializeAsync();
        viewModel.OpenVersionSelectorCommand.Execute(null);
        var current = Assert.Single(viewModel.VersionSelectorRows, row => row.Instance?.Name == "1.20.1").Instance!;

        await viewModel.DeleteVersionCommand.ExecuteAsync(current);

        Assert.False(Directory.Exists(Path.Combine(temp.Path, "versions", "1.20.1")));
        Assert.True(viewModel.IsVersionSelectorOpen);
        Assert.Equal("1.19.4", viewModel.SelectedInstance?.Name);
        Assert.DoesNotContain(viewModel.VersionSelectorRows, row => row.Instance?.Name == "1.20.1");
        Assert.Equal("1.19.4", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("1.19.4", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Contains("已删除", viewModel.StatusMessage);
        Assert.Contains("已切换到 1.19.4", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchPageVersionSelectorClearsLaunchSelectionWhenDeletingLastVersion()
    {
        using var temp = new TempDirectory();
        WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, "1.20.1");
        var viewModel = new LaunchPageViewModel(
            new MinecraftDiscoveryService(),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            prompts: new TestPromptService(confirm: true),
            instanceManagement: new TestMinecraftInstanceManagementService());

        await viewModel.InitializeAsync();
        viewModel.OpenVersionSelectorCommand.Execute(null);
        var current = Assert.Single(viewModel.VersionSelectorRows, row => row.Instance?.Name == "1.20.1").Instance!;

        await viewModel.DeleteVersionCommand.ExecuteAsync(current);

        Assert.False(Directory.Exists(Path.Combine(temp.Path, "versions", "1.20.1")));
        Assert.Null(viewModel.SelectedInstance);
        Assert.False(viewModel.HasSelectedInstance);
        Assert.True(viewModel.HasNoSelectedInstance);
        Assert.False(viewModel.HasVersionSelectorRows);
        Assert.Equal("", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Contains("已删除", viewModel.StatusMessage);
        Assert.Contains("当前没有可用启动版本", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchPageVersionSelectorKeepsVersionWhenDeleteCanceled()
    {
        using var temp = new TempDirectory();
        WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = new LaunchPageViewModel(
            new MinecraftDiscoveryService(),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            prompts: new TestPromptService(confirm: false),
            instanceManagement: new TestMinecraftInstanceManagementService());

        await viewModel.InitializeAsync();
        var version = Assert.Single(viewModel.VersionSelectorRows, row => row.IsSelectable).Instance!;

        await viewModel.DeleteVersionCommand.ExecuteAsync(version);

        Assert.True(Directory.Exists(Path.Combine(temp.Path, "versions", "1.20.1")));
        Assert.Contains(viewModel.VersionSelectorRows, row => row.Instance?.Name == "1.20.1");
        Assert.Contains("已取消删除", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchPageRestoresSelectedVersionFromCurrentMinecraftFolderPclIni()
    {
        using var temp = new TempDirectory();
        var rootA = Path.Combine(temp.Path, "A");
        var rootB = Path.Combine(temp.Path, "B");
        var first = WriteVersion(rootA, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var second = WriteVersion(rootB, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, rootB);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        new MinecraftSelectionService().WriteSelectedInstanceName(rootB, "1.19.4");
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(rootB, [first, second]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.InitializeAsync();

        Assert.Equal("1.19.4", viewModel.SelectedInstance?.Name);
    }

    [Fact]
    public async Task LaunchPageSwitchesAwayFromMissingSavedLaunchVersion()
    {
        using var temp = new TempDirectory();
        var fallback = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "DeletedVersion");
        var selections = new MinecraftSelectionService();
        selections.WriteSelectedInstanceName(temp.Path, "DeletedVersion");
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [fallback]),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            selections: selections);

        await viewModel.OnNavigatedToAsync();

        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("1.20.1", selections.ReadSelectedInstanceName(temp.Path));
        Assert.Contains("DeletedVersion", viewModel.StatusMessage);
        Assert.Contains("已切换到 1.20.1", viewModel.StatusMessage);
        var current = Assert.Single(viewModel.VersionSelectorRows, row => row.IsCurrentLaunchVersion);
        Assert.Equal("1.20.1", current.Instance?.Name);
    }

    [Fact]
    public async Task LaunchPageRefreshesVersionSelectionWhenNavigatedBackAfterDownload()
    {
        using var temp = new TempDirectory();
        var first = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var installed = WriteVersion(temp.Path, "Downloaded Pack", """
        { "id": "Downloaded Pack", "inheritsFrom": "1.20.1", "releaseTime": "2023-06-13T12:00:00+00:00", "libraries": [] }
        """);
        var instances = new List<MinecraftInstance> { first };
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var selections = new MinecraftSelectionService();
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, instances),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            selections: selections);

        await viewModel.OnNavigatedToAsync();
        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);

        instances.Add(installed);
        selections.WriteSelectedInstanceName(temp.Path, "Downloaded Pack");
        selections.ClearInstanceCache(temp.Path);
        await viewModel.OnNavigatedToAsync();

        Assert.Equal("Downloaded Pack", viewModel.SelectedInstance?.Name);
        Assert.Contains(viewModel.VersionSelectorRows, row => row.Instance?.Name == "Downloaded Pack");
    }

    [Fact]
    public async Task LaunchPageSyncsMinecraftRootPathWhenNavigatedBackAfterOtherPageChange()
    {
        using var temp = new TempDirectory();
        var rootA = Path.Combine(temp.Path, "A");
        var rootB = Path.Combine(temp.Path, "B");
        WriteVersion(rootA, "RootA", """
        { "id": "RootA", "releaseTime": "2023-01-01T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersion(rootB, "RootB", """
        { "id": "RootB", "releaseTime": "2023-02-01T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, rootA);
        var selections = new MinecraftSelectionService();
        var viewModel = new LaunchPageViewModel(
            new MinecraftDiscoveryService(),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService(),
            selections: selections);

        await viewModel.OnNavigatedToAsync();
        Assert.Equal("RootA", viewModel.SelectedInstance?.Name);

        settings.Set(AppSettingKeys.MinecraftRootPath, rootB);
        await viewModel.OnNavigatedToAsync();

        Assert.Equal(Path.GetFullPath(rootB), viewModel.MinecraftRootPath);
        Assert.Equal("RootB", viewModel.SelectedInstance?.Name);
        Assert.Contains(viewModel.VersionSelectorRows, row => row.Instance?.Name == "RootB");
        Assert.DoesNotContain(viewModel.VersionSelectorRows, row => row.Instance?.Name == "RootA");
        Assert.Equal(Path.GetFullPath(rootB), settings.Get(AppSettingKeys.MinecraftRootPath, ""));
        Assert.Equal("RootB", selections.ReadSelectedInstanceName(rootB));
    }

    [Fact]
    public async Task LaunchPageRefreshesManagedVersionCustomInfoWhenNavigatedBack()
    {
        using var temp = new TempDirectory();
        var first = WriteVersion(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var updated = first with { CustomInfo = "CustomPackTest" };
        var instances = new List<MinecraftInstance> { first };
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, instances),
            new FakeJavaDiscoveryService([]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.OnNavigatedToAsync();

        Assert.DoesNotContain("CustomPackTest", viewModel.SelectedInstanceSummary);

        instances[0] = updated;
        await viewModel.OnNavigatedToAsync();

        Assert.Contains("CustomPackTest", viewModel.SelectedInstanceSummary);
        viewModel.VersionSearchText = "Custom";
        Assert.Contains(viewModel.VersionSelectorRows, row => row.Instance?.Name == "1.20.1");
    }

    [Fact]
    public async Task LaunchPageConsumesSuccessfulInstanceFileCompletionFeedback()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LastFileCompletionInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LastFileCompletionMessage, "1.20.1 文件补全完成，可以重试启动");
        settings.Set(AppSettingKeys.LastFileCompletionSucceeded, true);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.OnNavigatedToAsync();

        Assert.False(viewModel.HasLaunchFileCompletionAction);
        Assert.Equal("", viewModel.LaunchDiagnostics);
        Assert.Contains("可以重试", viewModel.FileCompletionSummary);
        Assert.Contains(viewModel.FileCompletionDetails, detail => detail.Contains("文件补全完成", StringComparison.Ordinal));
        Assert.Contains("可以重试", viewModel.StatusMessage);
        Assert.Equal("", settings.Get(AppSettingKeys.LastFileCompletionInstanceName, ""));
    }

    [Fact]
    public async Task LaunchPageKeepsCompletionActionWhenInstanceFileCompletionStillFails()
    {
        using var temp = new TempDirectory();
        var instance = WriteVersion(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LastFileCompletionInstanceName, instance.Name);
        settings.Set(AppSettingKeys.LastFileCompletionMessage, "补全后仍缺少 2 个文件");
        settings.Set(AppSettingKeys.LastFileCompletionSucceeded, false);
        var viewModel = new LaunchPageViewModel(
            new FakeMinecraftDiscoveryService(temp.Path, [instance]),
            new FakeJavaDiscoveryService([new JavaEntry(javaPath, new Version(1, 17, 0, 0), false, true, false, true)]),
            new CaptureLaunchPipelineService(),
            settings,
            new NullFileDialogService(),
            new LegacyLoginService(),
            new NullLoggerService());

        await viewModel.OnNavigatedToAsync();

        Assert.True(viewModel.HasLaunchFileCompletionAction);
        Assert.Contains("仍需处理", viewModel.FileCompletionSummary);
        Assert.Contains(viewModel.FileCompletionDetails, detail => detail.Contains("仍缺少", StringComparison.Ordinal));
        Assert.Equal("", settings.Get(AppSettingKeys.LastFileCompletionInstanceName, ""));
    }

    private static LaunchRequest CreateRequest(MinecraftInstance instance, string root)
    {
        return new LaunchRequest(instance, root, null, "Steve", 512, 2048, 854, 480, "", "", false);
    }

    private static JavaEntry CreateJava(int major)
    {
        return new JavaEntry("C:\\Java\\bin\\java.exe", new Version(1, major, 0, 0), false, true, false, false);
    }

    private static MinecraftInstance WriteVersion(string root, string name, string json, bool createJar = false)
    {
        var versionPath = Path.Combine(root, "versions", name);
        Directory.CreateDirectory(versionPath);
        File.WriteAllText(Path.Combine(versionPath, $"{name}.json"), json);
        if (createJar)
        {
            File.WriteAllText(Path.Combine(versionPath, $"{name}.jar"), "");
        }

        return new MinecraftDiscoveryService().InspectInstance(root, versionPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name });
    }

    private sealed class FakeLaunchHttpClient : ILaunchHttpClient
    {
        private readonly Queue<(string UrlPart, string? Response, Exception? Exception)> _responses = new();

        public List<LaunchHttpRequest> Requests { get; } = [];

        public void Enqueue(string urlPart, string response)
        {
            _responses.Enqueue((urlPart, response, null));
        }

        public void EnqueueException(string urlPart, Exception exception)
        {
            _responses.Enqueue((urlPart, null, exception));
        }

        public Task<string> SendAsync(LaunchHttpRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("没有 fake HTTP 响应。");
            }

            var next = _responses.Dequeue();
            Assert.Contains(next.UrlPart, request.Url);
            if (next.Exception is not null)
            {
                throw next.Exception;
            }

            return Task.FromResult(next.Response ?? "");
        }
    }

    private sealed class CaptureMicrosoftDeviceCodePresenter : IMicrosoftDeviceCodePresenter
    {
        public MicrosoftDeviceCodeInfo? LastInfo { get; private set; }

        public Task ShowAsync(MicrosoftDeviceCodeInfo info, CancellationToken cancellationToken = default)
        {
            LastInfo = info;
            return Task.CompletedTask;
        }
    }

    private sealed class QueueingUiDispatcherService(bool checkAccess) : IUiDispatcherService
    {
        private readonly Queue<Action> _queued = [];

        public int QueuedCount => _queued.Count;

        public bool CheckAccess()
        {
            return checkAccess;
        }

        public void Invoke(Action action)
        {
            action();
        }

        public Task InvokeAsync(Action action)
        {
            _queued.Enqueue(action);
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            return Task.FromResult(action());
        }

        public void RunQueued()
        {
            while (_queued.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private sealed class CaptureYggdrasilProfileSelector(string profileName) : IYggdrasilProfileSelector
    {
        public int Calls { get; private set; }

        public string? LastTitle { get; private set; }

        public IReadOnlyList<YggdrasilProfileOption> LastProfiles { get; private set; } = [];

        public Task<YggdrasilProfileOption?> SelectAsync(
            string title,
            IReadOnlyList<YggdrasilProfileOption> profiles,
            string cachedProfileName,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastTitle = title;
            LastProfiles = profiles.ToArray();
            return Task.FromResult<YggdrasilProfileOption?>(profiles.FirstOrDefault(profile => profile.Name == profileName));
        }
    }

    private sealed class CaptureLoginService(IAppSettingsService settings, LoginSession session) : ILoginService
    {
        public int Calls { get; private set; }

        public LoginRequest? LastRequest { get; private set; }

        public Task<LoginSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            if (request.Type == LoginType.Ms)
            {
                settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "refresh");
                settings.Set(AppSettingKeys.CacheMsV2Access, session.AccessToken);
                settings.Set(AppSettingKeys.CacheMsV2ProfileJson, session.AuthlibInjectorMetadata);
                settings.Set(AppSettingKeys.CacheMsV2Uuid, session.Uuid);
                settings.Set(AppSettingKeys.CacheMsV2Name, session.UserName);
                settings.Set(AppSettingKeys.CacheMsV2Expires, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
            }
            else if (request.Type is LoginType.Nide or LoginType.Auth)
            {
                var prefix = request.Type == LoginType.Nide ? "Nide" : "Auth";
                settings.Set("Cache" + prefix + "Access", session.AccessToken);
                settings.Set("Cache" + prefix + "Client", session.ClientToken);
                settings.Set("Cache" + prefix + "Uuid", session.Uuid);
                settings.Set("Cache" + prefix + "Name", session.UserName);
                settings.Set("Cache" + prefix + "Username", request.UserName);
                settings.Set("Cache" + prefix + "Pass", request.Remember ? request.Password : "");
            }

            return Task.FromResult(session);
        }
    }

    private sealed class BlockingLoginService(IMicrosoftDeviceCodeStatusService deviceCodes) : ILoginService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCanceled { get; private set; }

        public async Task<LoginSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            await deviceCodes.ShowAsync(new MicrosoftDeviceCodeInfo("ABCD-EFGH", "device", "https://microsoft.com/link", 900, 1, "等待网页登录授权"), cancellationToken);
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCanceled = true;
                throw;
            }

            return new LoginSession(request.Type, "Alex", "uuid", "access", "client", "{}");
        }
    }

    private sealed class FakeMinecraftDiscoveryService(string root, IReadOnlyList<MinecraftInstance> instances) : IMinecraftDiscoveryService
    {
        public string GetDefaultMinecraftRoot()
        {
            return root;
        }

        public Task<IReadOnlyList<MinecraftInstance>> ScanAsync(string? rootPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(instances);
        }

        public MinecraftInstance InspectInstance(string rootPath, string versionPath, IReadOnlySet<string> availableInstances)
        {
            return new MinecraftDiscoveryService().InspectInstance(rootPath, versionPath, availableInstances);
        }
    }

    private sealed class FakeNavigationService(PageViewModelBase launchPage, PageViewModelBase instancePage, PageViewModelBase? downloadPage = null) : INavigationService
    {
        private readonly Dictionary<PageRoute, PageViewModelBase> _pages = new()
        {
            [PageRoute.Launch] = launchPage,
            [PageRoute.Download] = downloadPage ?? new TestPageViewModel(PageRoute.Download),
            [PageRoute.Instance] = instancePage
        };

        public IReadOnlyList<PageNavigationItem> Pages { get; } =
        [
            new(PageRoute.Launch, "启动", "启动"),
            new(PageRoute.Instance, "实例", "实例")
        ];

        public PageViewModelBase CurrentPage { get; private set; } = launchPage;

        public void Navigate(PageRoute route)
        {
            CurrentPage = _pages[route];
        }
    }

    private sealed class TestPageViewModel(PageRoute route) : PageViewModelBase(route, route.ToString(), route.ToString());

    private sealed class TestPromptService(bool confirm) : IUserPromptService
    {
        public bool Confirm(string title, string message)
        {
            return confirm;
        }

        public string? Prompt(string title, string message, string defaultValue)
        {
            return defaultValue;
        }
    }

    private sealed class TestMinecraftInstanceManagementService : IMinecraftInstanceManagementService
    {
        private readonly MinecraftInstanceManagementService inner = new();

        public MinecraftInstanceMetadata ReadMetadata(string versionPath)
        {
            return inner.ReadMetadata(versionPath);
        }

        public void SetStar(MinecraftInstance instance, bool isStar)
        {
            inner.SetStar(instance, isStar);
        }

        public void SetDisplayType(MinecraftInstance instance, MinecraftInstanceDisplayType displayType)
        {
            inner.SetDisplayType(instance, displayType);
        }

        public void SetCustomInfo(MinecraftInstance instance, string customInfo)
        {
            inner.SetCustomInfo(instance, customInfo);
        }

        public string RenameInstance(MinecraftInstance instance, string newName)
        {
            return inner.RenameInstance(instance, newName);
        }

        public string CloneInstance(MinecraftInstance instance, string newName)
        {
            return inner.CloneInstance(instance, newName);
        }

        public string ImportInstance(string sourceVersionPath, string targetMinecraftRoot, string? targetName = null)
        {
            return inner.ImportInstance(sourceVersionPath, targetMinecraftRoot, targetName);
        }

        public void DeleteInstance(MinecraftInstance instance, bool permanent = false)
        {
            inner.DeleteInstance(instance, permanent: true);
        }
    }

    private sealed class CaptureFolderOpenService : IFolderOpenService
    {
        public List<string> OpenedPaths { get; } = [];

        public void OpenFolder(string folderPath)
        {
            OpenedPaths.Add(folderPath);
        }
    }

    private sealed class FakeDownloadByteClient : IDownloadByteClient
    {
        public Task<byte[]> GetBytesAsync(string url, bool simulateBrowserHeaders = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Array.Empty<byte>());
        }
    }

    private sealed class FakeJavaDiscoveryService(IReadOnlyList<JavaEntry> entries) : IJavaDiscoveryService
    {
        public Task<IReadOnlyList<JavaEntry>> DiscoverAsync(string minecraftRootPath, string? instancePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entries);
        }

        public Task<JavaEntry?> InspectJavaAsync(string javaPath, bool isUserImport = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entries.FirstOrDefault(entry => string.Equals(entry.PathJava, javaPath, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private sealed class SaveFileDialogService(string targetPath) : IFileDialogService
    {
        public string? LastDefaultFileName { get; private set; }

        public string? PickFolder(string title, string initialDirectory)
        {
            return null;
        }

        public string? PickJavaExecutable(string initialDirectory)
        {
            return null;
        }

        public string? PickSkinFile(string initialDirectory)
        {
            return null;
        }

        public string? PickModpackFile(string initialDirectory)
        {
            return null;
        }

        public IReadOnlyList<string> PickModFiles(string initialDirectory)
        {
            return [];
        }

        public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter)
        {
            LastDefaultFileName = defaultFileName;
            return targetPath;
        }
    }

    private sealed class SkinFileDialogService(string skinPath) : IFileDialogService
    {
        public string? LastInitialDirectory { get; private set; }

        public string? PickFolder(string title, string initialDirectory)
        {
            return null;
        }

        public string? PickJavaExecutable(string initialDirectory)
        {
            return null;
        }

        public string? PickSkinFile(string initialDirectory)
        {
            LastInitialDirectory = initialDirectory;
            return skinPath;
        }

        public string? PickModpackFile(string initialDirectory)
        {
            return null;
        }

        public IReadOnlyList<string> PickModFiles(string initialDirectory)
        {
            return [];
        }

        public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter)
        {
            return null;
        }
    }

    private sealed class CaptureCustomCommandRunner : ICustomCommandRunner
    {
        public List<Call> Calls { get; } = [];

        public Task RunAsync(string command, string workingDirectory, bool waitForExit, CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call(command, workingDirectory, waitForExit));
            return Task.CompletedTask;
        }

        public sealed record Call(string Command, string WorkingDirectory, bool WaitForExit);
    }

    private sealed class CaptureLaunchPipelineService : ILaunchPipelineService
    {
        private readonly List<LaunchStepState> _steps = [];

        public event EventHandler<IReadOnlyList<LaunchStepState>>? StepsChanged;

        public LaunchRequest? LastRequest { get; private set; }

        public int GenerateCalls { get; private set; }

        public int LaunchCalls { get; private set; }

        public IReadOnlyList<LaunchStepState> Steps => _steps;

        public Task<LaunchResult> GenerateProfileAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            LastRequest = request;
            SetGenerateSteps();
            return Task.FromResult(new LaunchResult(true, null, [], null));
        }

        public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            LaunchCalls++;
            LastRequest = request;
            SetLaunchSteps();
            return Task.FromResult(new LaunchResult(true, null, [], null));
        }

        private void SetGenerateSteps()
        {
            _steps.Clear();
            _steps.AddRange([
                new LaunchStepState("Precheck", LaunchStepStatus.Succeeded, "OK"),
                new LaunchStepState("Java", LaunchStepStatus.Succeeded, "Java 17"),
                new LaunchStepState("Login", LaunchStepStatus.Succeeded, "Steve"),
                new LaunchStepState("FileCompleter", LaunchStepStatus.Skipped, "Generate only"),
                new LaunchStepState("Arguments", LaunchStepStatus.Succeeded, "Generated"),
                new LaunchStepState("Process", LaunchStepStatus.Succeeded, "Prepared"),
                new LaunchStepState("Natives", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("PreRun", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("CustomCommand", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("StartProcess", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("Window", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("Finish", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("Done", LaunchStepStatus.Waiting, "")
            ]);
            StepsChanged?.Invoke(this, _steps.ToArray());
        }

        private void SetLaunchSteps()
        {
            _steps.Clear();
            _steps.AddRange([
                new LaunchStepState("Precheck", LaunchStepStatus.Succeeded, "OK"),
                new LaunchStepState("Java", LaunchStepStatus.Succeeded, "Java 17"),
                new LaunchStepState("Login", LaunchStepStatus.Succeeded, "Steve"),
                new LaunchStepState("FileCompleter", LaunchStepStatus.Succeeded, "OK"),
                new LaunchStepState("Arguments", LaunchStepStatus.Succeeded, "Generated"),
                new LaunchStepState("Process", LaunchStepStatus.Succeeded, "Prepared"),
                new LaunchStepState("Natives", LaunchStepStatus.Succeeded, "Extracted"),
                new LaunchStepState("PreRun", LaunchStepStatus.Succeeded, "Done"),
                new LaunchStepState("CustomCommand", LaunchStepStatus.Skipped, "None"),
                new LaunchStepState("StartProcess", LaunchStepStatus.Succeeded, "Started"),
                new LaunchStepState("Window", LaunchStepStatus.Succeeded, "Found"),
                new LaunchStepState("Finish", LaunchStepStatus.Succeeded, "Done"),
                new LaunchStepState("Done", LaunchStepStatus.Succeeded, "OK")
            ]);
            StepsChanged?.Invoke(this, _steps.ToArray());
        }
    }

    private sealed class CaptureClipboardService : IClipboardService
    {
        public string LastText { get; private set; } = "";

        public void SetText(string text)
        {
            LastText = text;
        }
    }

    private sealed class BlockingLaunchPipelineService : ILaunchPipelineService
    {
        private readonly List<LaunchStepState> _steps = [];

        public event EventHandler<IReadOnlyList<LaunchStepState>>? StepsChanged;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCanceled { get; private set; }

        public IReadOnlyList<LaunchStepState> Steps => _steps;

        public Task<LaunchResult> GenerateProfileAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LaunchResult(true, null, [], null));
        }

        public async Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            _steps.Clear();
            _steps.Add(new LaunchStepState("文件补全", LaunchStepStatus.Running, "正在补全 0/2"));
            StepsChanged?.Invoke(this, _steps.ToArray());
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new LaunchResult(true, null, [], null);
            }
            catch (OperationCanceledException)
            {
                WasCanceled = true;
                throw;
            }
        }
    }

    private sealed class FailedLaunchPipelineService(IReadOnlyList<string> missingFiles) : ILaunchPipelineService
    {
        public event EventHandler<IReadOnlyList<LaunchStepState>>? StepsChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<LaunchStepState> Steps { get; } =
        [
            new LaunchStepState("FileCompleter", LaunchStepStatus.Failed, "Missing local files")
        ];

        public Task<LaunchResult> GenerateProfileAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateFailedResult(request));
        }

        public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateFailedResult(request));
        }

        private LaunchResult CreateFailedResult(LaunchRequest request)
        {
            var java = new JavaEntry(request.JavaPath ?? "java.exe", new Version(1, 17, 0, 0), false, true, false, true);
            var profile = new LaunchProfile(
                request.Instance!,
                java,
                new LoginSession(LoginType.Legacy, request.LegacyName, "uuid", "token", "client"),
                "",
                "",
                new System.Diagnostics.ProcessStartInfo(java.PathJava),
                missingFiles);
            return new LaunchResult(
                false,
                profile,
                [new LaunchValidationIssue("MissingLocalFiles", "本地文件缺失，无法启动")],
                null);
        }
    }

    private sealed class FixedFailedLaunchPipelineService(params LaunchValidationIssue[] issues) : ILaunchPipelineService
    {
        public event EventHandler<IReadOnlyList<LaunchStepState>>? StepsChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<LaunchStepState> Steps { get; } =
        [
            new LaunchStepState("Precheck", LaunchStepStatus.Failed, "Validation failed")
        ];

        public Task<LaunchResult> GenerateProfileAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LaunchResult.Failed(issues));
        }

        public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LaunchResult.Failed(issues));
        }
    }
}
