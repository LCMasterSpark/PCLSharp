using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class LaunchPageViewModel
{
    private void LoadLaunchSettings(string? instanceName)
    {
        MinMemoryMb = GetInstanceSetting(instanceName, AppSettingKeys.LaunchMinMemoryMb, 512);
        MaxMemoryMb = GetInstanceSetting(instanceName, AppSettingKeys.LaunchMaxMemoryMb, 4096);
        LaunchWindowWidth = _settings.Get(AppSettingKeys.LaunchArgumentWindowWidth, 854);
        LaunchWindowHeight = _settings.Get(AppSettingKeys.LaunchArgumentWindowHeight, 480);
        LaunchWindowType = _settings.Get(AppSettingKeys.LaunchArgumentWindowType, 1);
        ExtraJvmArgs = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceJvm, _settings.Get(AppSettingKeys.LaunchAdvanceJvm, ""));
        ExtraGameArgs = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceGame, _settings.Get(AppSettingKeys.LaunchAdvanceGame, ""));
        ServerIp = GetInstanceSetting(instanceName, AppSettingKeys.VersionServerEnter, "");
        OnPropertyChanged(nameof(InstanceLaunchSettingsSummary));
    }

    private T GetInstanceSetting<T>(string? instanceName, string key, T defaultValue)
    {
        return string.IsNullOrWhiteSpace(instanceName)
            ? _settings.Get(key, defaultValue)
            : _settings.Get(GetInstanceSettingKey(instanceName, key), _settings.Get(key, defaultValue));
    }

    private string ResolveJavaPath(string? instanceName)
    {
        var globalJava = _settings.Get(AppSettingKeys.LaunchArgumentJavaSelect, "");
        var globalJavaPath = JavaEntry.ResolveSettingJavaPath(globalJava);
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return globalJavaPath;
        }

        var versionJava = _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionArgumentJavaSelect), UseGlobalJavaSettingText);
        return string.IsNullOrWhiteSpace(versionJava)
            || string.Equals(versionJava, UseGlobalJavaSettingText, StringComparison.OrdinalIgnoreCase)
            ? globalJavaPath
            : JavaEntry.ResolveSettingJavaPath(versionJava);
    }

    private bool HasInstanceJavaOverride(string? instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return false;
        }

        var versionJava = _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionArgumentJavaSelect), UseGlobalJavaSettingText);
        return !string.IsNullOrWhiteSpace(versionJava)
            && !string.Equals(versionJava, UseGlobalJavaSettingText, StringComparison.OrdinalIgnoreCase);
    }

    private void SaveLaunchSettings()
    {
        _settings.Set(AppSettingKeys.MinecraftRootPath, MinecraftRootPath);
        _settings.Set(AppSettingKeys.LoginType, SelectedLoginType);
        _settings.Set(AppSettingKeys.LoginRemember, RememberLogin);
        _settings.Set(AppSettingKeys.MicrosoftClientId, MicrosoftClientId.Trim());
        _settings.Set(AppSettingKeys.LaunchSkinType, LaunchSkinType);
        _settings.Set(AppSettingKeys.LaunchSkinID, LaunchSkinId);
        _settings.Set(AppSettingKeys.LaunchSkinSlim, LaunchSkinSlim);
        _legacyLogin.SaveHistory(LegacyName, _settings);
        _settings.Set(AppSettingKeys.LaunchArgumentWindowWidth, LaunchWindowWidth);
        _settings.Set(AppSettingKeys.LaunchArgumentWindowHeight, LaunchWindowHeight);
        _settings.Set(AppSettingKeys.LaunchArgumentWindowType, LaunchWindowType);
        _settings.Set(AppSettingKeys.LaunchArgumentVisible, LauncherVisibility);
        SaveLoginInputs();
        if (!string.IsNullOrWhiteSpace(SelectedInstance?.Name))
        {
            SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchMinMemoryMb, MinMemoryMb);
            SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchMaxMemoryMb, MaxMemoryMb);
            SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceJvm, ExtraJvmArgs);
            SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceGame, ExtraGameArgs);
            SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerEnter, ServerIp);
        }

        if (!HasInstanceJavaOverride(SelectedInstance?.Name))
        {
            _settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, JavaEntry.ToPclSettingJson(SelectedJava));
        }
    }

    private void SaveLoginInputs()
    {
        if (SelectedLoginType == LoginType.Nide)
        {
            SaveDelimitedHistory(AppSettingKeys.LoginNideEmail, LoginUserName);
            _settings.Set(AppSettingKeys.LoginNidePass, RememberLogin ? LoginPassword : "");
            _settings.Set(AppSettingKeys.CacheNideServer, LoginServer);
        }
        else if (SelectedLoginType == LoginType.Auth)
        {
            SaveDelimitedHistory(AppSettingKeys.LoginAuthEmail, LoginUserName);
            _settings.Set(AppSettingKeys.LoginAuthPass, RememberLogin ? LoginPassword : "");
            _settings.Set(AppSettingKeys.CacheAuthServerServer, LoginServer);
        }

        OnPropertyChanged(nameof(LegacyNameHistory));
        OnPropertyChanged(nameof(LoginUserNameHistory));
    }

    private void OnMicrosoftAccountChanged()
    {
        RefreshMicrosoftAccounts();
        OnLoginAccountChanged();
    }

    private void RefreshMicrosoftAccounts()
    {
        var current = GetCurrentMicrosoftAccount();
        var accounts = ReadMicrosoftAccounts();
        if (HasMicrosoftAccountData(current) && accounts.All(account => !IsSameMicrosoftAccount(account, current)))
        {
            accounts = [current, .. accounts];
        }

        accounts = accounts
            .Where(HasMicrosoftAccountData)
            .GroupBy(account => string.IsNullOrWhiteSpace(account.Uuid) ? account.Name : account.Uuid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(account => account.LastUsedAt).First())
            .OrderByDescending(account => account.LastUsedAt)
            .Take(10)
            .ToArray();

        lock (_microsoftAccountsSync)
        {
            MicrosoftAccounts.Clear();
            foreach (var account in accounts)
            {
                MicrosoftAccounts.Add(account);
            }
        }

        SelectedMicrosoftAccount = accounts.FirstOrDefault(account => IsSameMicrosoftAccount(account, current))
            ?? accounts.FirstOrDefault();
        OnPropertyChanged(nameof(HasMicrosoftAccountHistory));
        OnPropertyChanged(nameof(MicrosoftAccountHistorySummary));
        SwitchMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        DeleteMicrosoftAccountCommand?.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<MicrosoftAccountCacheEntry> ReadMicrosoftAccounts()
    {
        var accounts = new List<MicrosoftAccountCacheEntry>();
        var json = _settings.Get(AppSettingKeys.CacheMsV2AccountsJson, "");
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

        accounts.AddRange(ReadLegacyMicrosoftAccounts());
        return accounts
            .Where(HasMicrosoftAccountData)
            .GroupBy(account => string.IsNullOrWhiteSpace(account.Uuid) ? account.Name : account.Uuid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(account => account.LastUsedAt).First())
            .OrderByDescending(account => account.LastUsedAt)
            .Take(10)
            .ToArray();
    }

    private IReadOnlyList<MicrosoftAccountCacheEntry> ReadLegacyMicrosoftAccounts()
    {
        var json = _settings.Get(AppSettingKeys.LoginMsJson, "{}");
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

    private void UpsertMicrosoftAccount(MicrosoftAccountCacheEntry account)
    {
        if (!HasMicrosoftAccountData(account))
        {
            return;
        }

        var accounts = ReadMicrosoftAccounts()
            .Where(item => !IsSameMicrosoftAccount(item, account))
            .Prepend(account)
            .OrderByDescending(item => item.LastUsedAt)
            .Take(10)
            .ToArray();
        _settings.Set(AppSettingKeys.CacheMsV2AccountsJson, JsonSerializer.Serialize(accounts));
        UpsertLegacyMicrosoftAccount(account);
    }

    private void UpsertLegacyMicrosoftAccount(MicrosoftAccountCacheEntry account)
    {
        if (string.IsNullOrWhiteSpace(account.Name) || string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            return;
        }

        var legacy = ReadLegacyMicrosoftAccounts()
            .Where(item => !string.Equals(item.Name, account.Name, StringComparison.OrdinalIgnoreCase))
            .Select(item => new KeyValuePair<string, string>(item.Name, item.RefreshToken))
            .Prepend(new KeyValuePair<string, string>(account.Name, account.RefreshToken))
            .Take(10);
        _settings.Set(AppSettingKeys.LoginMsJson, JsonSerializer.Serialize(legacy.ToDictionary(item => item.Key, item => item.Value)));
    }

    private void RemoveLegacyMicrosoftAccount(MicrosoftAccountCacheEntry account)
    {
        var legacy = ReadLegacyMicrosoftAccounts()
            .Where(item => !IsSameMicrosoftAccount(item, account))
            .Select(item => new KeyValuePair<string, string>(item.Name, item.RefreshToken));
        _settings.Set(AppSettingKeys.LoginMsJson, JsonSerializer.Serialize(legacy.ToDictionary(item => item.Key, item => item.Value)));
    }

    private void ApplyMicrosoftAccount(MicrosoftAccountCacheEntry account)
    {
        _settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, account.RefreshToken);
        _settings.Set(AppSettingKeys.CacheMsV2Access, account.AccessToken);
        _settings.Set(AppSettingKeys.CacheMsV2ProfileJson, account.ProfileJson);
        _settings.Set(AppSettingKeys.CacheMsV2Uuid, account.Uuid);
        _settings.Set(AppSettingKeys.CacheMsV2Name, account.Name);
        _settings.Set(AppSettingKeys.CacheMsV2Expires, account.ExpiresAt);
    }

    private MicrosoftAccountCacheEntry GetCurrentMicrosoftAccount()
    {
        var (name, uuid) = GetCachedMicrosoftProfile();
        return new MicrosoftAccountCacheEntry(
            uuid,
            name,
            _settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""),
            _settings.Get(AppSettingKeys.CacheMsV2Access, ""),
            _settings.Get<long>(AppSettingKeys.CacheMsV2Expires, 0),
            _settings.Get(AppSettingKeys.CacheMsV2ProfileJson, ""),
            DateTimeOffset.UtcNow);
    }

    private void ClearCurrentMicrosoftAccount()
    {
        _settings.Set(AppSettingKeys.CacheMsV2OAuthRefresh, "");
        _settings.Set(AppSettingKeys.CacheMsV2Access, "");
        _settings.Set(AppSettingKeys.CacheMsV2ProfileJson, "");
        _settings.Set(AppSettingKeys.CacheMsV2Uuid, "");
        _settings.Set(AppSettingKeys.CacheMsV2Name, "");
        _settings.Set(AppSettingKeys.CacheMsV2Expires, 0L);
    }

    private static bool HasMicrosoftAccountData(MicrosoftAccountCacheEntry account)
    {
        return !string.IsNullOrWhiteSpace(account.Uuid) || !string.IsNullOrWhiteSpace(account.Name);
    }

    private static bool IsSameMicrosoftAccount(MicrosoftAccountCacheEntry left, MicrosoftAccountCacheEntry right)
    {
        return (!string.IsNullOrWhiteSpace(left.Uuid)
                && !string.IsNullOrWhiteSpace(right.Uuid)
                && string.Equals(left.Uuid, right.Uuid, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(left.Name)
                && !string.IsNullOrWhiteSpace(right.Name)
                && string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
    }

    private (string Name, string Uuid) GetCachedMicrosoftProfile()
    {
        var name = _settings.Get(AppSettingKeys.CacheMsV2Name, "");
        var uuid = _settings.Get(AppSettingKeys.CacheMsV2Uuid, "");
        var profileJson = _settings.Get(AppSettingKeys.CacheMsV2ProfileJson, "");
        if ((string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uuid))
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
                return (name, uuid);
            }
        }

        return (name, uuid);
    }

    private void OnLoginAccountChanged()
    {
        OnPropertyChanged(nameof(HasMicrosoftClientId));
        OnPropertyChanged(nameof(HasMicrosoftAccount));
        OnPropertyChanged(nameof(HasValidMicrosoftAccessToken));
        OnPropertyChanged(nameof(MicrosoftCacheNeedsRefresh));
        OnPropertyChanged(nameof(MicrosoftAccountSummary));
        OnPropertyChanged(nameof(MicrosoftReadinessSummary));
        OnPropertyChanged(nameof(MicrosoftLoginActionText));
        OnPropertyChanged(nameof(MicrosoftClientIdHelp));
        OnPropertyChanged(nameof(CanStartMicrosoftLogin));
        OnPropertyChanged(nameof(CanRefreshMicrosoftLogin));
        OnPropertyChanged(nameof(MicrosoftLoginUnavailableReason));
        OnPropertyChanged(nameof(MicrosoftRefreshUnavailableReason));
        OnPropertyChanged(nameof(HasServerAccount));
        OnPropertyChanged(nameof(ServerAccountSummary));
        LoginMicrosoftAccountCommand.NotifyCanExecuteChanged();
        RefreshMicrosoftAccountCommand.NotifyCanExecuteChanged();
        LoginServerAccountCommand.NotifyCanExecuteChanged();
        LogoutMicrosoftAccountCommand.NotifyCanExecuteChanged();
        LogoutServerAccountCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetServerCachePrefix(out string prefix)
    {
        if (SelectedLoginType == LoginType.Nide)
        {
            prefix = "Nide";
            return true;
        }

        if (SelectedLoginType == LoginType.Auth)
        {
            prefix = "Auth";
            return true;
        }

        prefix = "";
        return false;
    }

    private string GetLoginUserName(LoginType type)
    {
        return type == LoginType.Nide
            ? GetHistory(AppSettingKeys.LoginNideEmail).FirstOrDefault() ?? ""
            : GetHistory(AppSettingKeys.LoginAuthEmail).FirstOrDefault() ?? "";
    }

    private string GetLoginPassword(LoginType type)
    {
        return type == LoginType.Nide
            ? _settings.Get(AppSettingKeys.LoginNidePass, "").Split('¨', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""
            : _settings.Get(AppSettingKeys.LoginAuthPass, "").Split('¨', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    }

    private string GetLoginServer(LoginType type)
    {
        return type == LoginType.Nide
            ? _settings.Get(AppSettingKeys.CacheNideServer, "")
            : _settings.Get(AppSettingKeys.CacheAuthServerServer, "");
    }

    private IReadOnlyList<string> GetHistory(string key)
    {
        return _settings.Get(key, "")
            .Split('¨', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
    }

    private void SaveDelimitedHistory(string key, string value)
    {
        var normalized = value.Trim().Replace("¨", "", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var history = GetHistory(key)
            .Where(item => !string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
            .Prepend(normalized)
            .Take(20);
        _settings.Set(key, string.Join('¨', history));
    }

    private void SetInstanceSetting<T>(string instanceName, string key, T value)
    {
        _settings.Set(GetInstanceSettingKey(instanceName, key), value);
    }

    private static string GetInstanceSettingKey(string instanceName, string key)
    {
        return $"Instance.{instanceName}.{key}";
    }

}
