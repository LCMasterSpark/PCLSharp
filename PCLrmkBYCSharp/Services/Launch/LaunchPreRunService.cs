using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media.Imaging;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LaunchPreRunService(
    IAppSettingsService settings,
    IAppLoggerService logger,
    IMinecraftGameDirectoryService? gameDirectories = null,
    IAppPathService? paths = null) : ILaunchPreRunService
{
    public Task PrepareAsync(LaunchRequest request, LoginSession login, CancellationToken cancellationToken = default)
    {
        if (request.Instance is null)
        {
            return Task.CompletedTask;
        }

        if (login.Type is LoginType.Ms or LoginType.Microsoft)
        {
            UpdateLauncherProfiles(request.MinecraftRootPath, login);
        }

        var optionsPath = UpdateOptions(request, request.Instance);
        UpdateOfflineSkinResourcePack(request, login, request.Instance, optionsPath);
        return Task.CompletedTask;
    }

    private void UpdateLauncherProfiles(string minecraftRootPath, LoginSession login)
    {
        try
        {
            Directory.CreateDirectory(minecraftRootPath);
            var path = Path.Combine(minecraftRootPath, "launcher_profiles.json");
            JsonObject root;
            if (File.Exists(path))
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
            }
            else
            {
                root = [];
            }

            var profileId = NormalizeProfileId(login.Uuid);
            root["clientToken"] = login.ClientToken;
            root["selectedUser"] = new JsonObject
            {
                ["account"] = profileId,
                ["profile"] = profileId
            };
            var authenticationDatabase = root["authenticationDatabase"] as JsonObject ?? [];
            var account = authenticationDatabase[profileId] as JsonObject ?? [];
            var profiles = account["profiles"] as JsonObject ?? [];
            profiles[profileId] = new JsonObject
            {
                ["displayName"] = login.UserName
            };
            account["username"] = login.UserName.Replace("\"", "-", StringComparison.Ordinal);
            account["profiles"] = profiles;
            authenticationDatabase[profileId] = account;
            root["authenticationDatabase"] = authenticationDatabase;
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            logger.Info("已更新 launcher_profiles.json");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "更新 launcher_profiles.json 失败");
        }
    }

    private string UpdateOptions(LaunchRequest request, MinecraftInstance instance)
    {
        try
        {
            var gameDirectory = ResolveGameDirectory(request, instance);
            var optionsPath = ResolveOptionsPath(gameDirectory);
            var options = ReadOptions(optionsPath);
            var isYosbrOptions = IsYosbrOptionsPath(gameDirectory, optionsPath);
            var currentLang = isYosbrOptions ? "none" : options.TryGetValue("lang", out var lang) ? lang : "none";
            if (currentLang == "none")
            {
                options["lang"] = GetDefaultMinecraftLanguage(instance);
            }

            if (request.WindowType == 0)
            {
                options["fullscreen"] = "true";
            }
            else if (request.WindowType != 1)
            {
                options["fullscreen"] = "false";
            }

            if (!string.IsNullOrWhiteSpace(request.ServerIp))
            {
                options["lastServer"] = request.ServerIp.Trim();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(optionsPath)!);
            File.WriteAllLines(optionsPath, options.Select(pair => pair.Key + ":" + pair.Value));
            logger.Info(isYosbrOptions ? "已更新 Yosbr Mod 的 options.txt" : "已更新 options.txt");
            return optionsPath;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "更新 options.txt 失败");
            return Path.Combine(ResolveGameDirectory(request, instance), "options.txt");
        }
    }

    private void UpdateOfflineSkinResourcePack(
        LaunchRequest request,
        LoginSession login,
        MinecraftInstance instance,
        string optionsPath)
    {
        try
        {
            var gameDirectory = ResolveGameDirectory(request, instance);
            var resourcePackPath = Path.Combine(gameDirectory, "resourcepacks", "PCL2 Skin.zip");
            var version = ParseMinecraftVersionOrDefault(instance.Version.VanillaVersion);
            var skinPath = GetCustomSkinPath();

            if (login.Type == LoginType.Legacy
                && settings.Get(AppSettingKeys.LaunchSkinType, 0) == 4
                && File.Exists(skinPath)
                && version.Major == 1
                && version.Minor >= 6)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(resourcePackPath)!);
                CreateOfflineSkinPack(resourcePackPath, skinPath, version);
                var options = ReadOptions(optionsPath);
                options["resourcePacks"] = AddSkinResourcePack(
                    options.TryGetValue("resourcePacks", out var value) ? value : "[]",
                    UsesNewResourcePackEntry(version));
                Directory.CreateDirectory(Path.GetDirectoryName(optionsPath)!);
                File.WriteAllLines(optionsPath, options.Select(pair => pair.Key + ":" + pair.Value));
                logger.Info("已生成自定义离线皮肤资源包");
                return;
            }

            if (File.Exists(resourcePackPath))
            {
                File.Delete(resourcePackPath);
                var options = ReadOptions(optionsPath);
                options["resourcePacks"] = RemoveSkinResourcePack(
                    options.TryGetValue("resourcePacks", out var value) ? value : "[]",
                    UsesNewResourcePackEntry(version));
                Directory.CreateDirectory(Path.GetDirectoryName(optionsPath)!);
                File.WriteAllLines(optionsPath, options.Select(pair => pair.Key + ":" + pair.Value));
                logger.Info("已清理自定义离线皮肤资源包");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "更新自定义离线皮肤资源包失败");
        }
    }

    private string ResolveGameDirectory(LaunchRequest request, MinecraftInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(request.GameDirectory))
        {
            return Path.GetFullPath(request.GameDirectory);
        }

        return gameDirectories?.Resolve(instance).Path ?? instance.VersionPath;
    }

    private static string ResolveOptionsPath(string gameDirectory)
    {
        var optionsPath = Path.Combine(gameDirectory, "options.txt");
        if (File.Exists(optionsPath))
        {
            return optionsPath;
        }

        var yosbrPath = Path.Combine(gameDirectory, "config", "yosbr", "options.txt");
        return File.Exists(yosbrPath) ? yosbrPath : optionsPath;
    }

    private string GetDefaultMinecraftLanguage(MinecraftInstance instance)
    {
        var useChinese = settings.Get(AppSettingKeys.Language, "zh-CN").StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        if (!useChinese)
        {
            return "en_us";
        }

        return NeedsLegacyLanguageCode(instance.Version.VanillaVersion) ? "zh_CN" : "zh_cn";
    }

    private static bool NeedsLegacyLanguageCode(string vanillaVersion)
    {
        var normalized = vanillaVersion.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "old", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || major != 1)
        {
            return false;
        }

        var minorText = new string(parts[1].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(minorText, out var minor) && minor <= 10;
    }

    private static bool IsYosbrOptionsPath(string gameDirectory, string optionsPath)
    {
        var expected = Path.Combine(gameDirectory, "config", "yosbr", "options.txt");
        return string.Equals(Path.GetFullPath(expected), Path.GetFullPath(optionsPath), StringComparison.OrdinalIgnoreCase);
    }

    private string GetCustomSkinPath()
    {
        var appData = paths?.AppDataDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plain Craft Launcher Sharp");
        return Path.Combine(appData, "CustomSkin.png");
    }

    private void CreateOfflineSkinPack(string targetPath, string skinPath, Version version)
    {
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        var skinBytes = GetSkinBytesForVersion(skinPath, version);
        using var archive = ZipFile.Open(targetPath, ZipArchiveMode.Create);
        WriteTextEntry(
            archive,
            "pack.mcmeta",
            "{\"pack\":{\"pack_format\":" + GetResourcePackFormat(version) + ",\"description\":\"PCL 自定义离线皮肤资源包\"}}");
        WriteBytesEntry(archive, "pack.png", skinBytes);

        if (UsesModernPlayerSkinPath(version))
        {
            var model = settings.Get(AppSettingKeys.LaunchSkinSlim, false) ? "slim" : "wide";
            foreach (var skinName in new[] { "alex", "ari", "efe", "kai", "makena", "noor", "steve", "sunny", "zuri" })
            {
                WriteBytesEntry(archive, $"assets/minecraft/textures/entity/player/{model}/{skinName}.png", skinBytes);
            }
        }
        else
        {
            var skinName = settings.Get(AppSettingKeys.LaunchSkinSlim, false) ? "alex.png" : "steve.png";
            WriteBytesEntry(archive, "assets/minecraft/textures/entity/" + skinName, skinBytes);
        }
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void WriteBytesEntry(ZipArchive archive, string entryName, byte[] content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private byte[] GetSkinBytesForVersion(string skinPath, Version version)
    {
        var bytes = File.ReadAllBytes(skinPath);
        if (version.Major == 1 && version.Minor is 6 or 7)
        {
            return TryCropLegacySkin(bytes);
        }

        return bytes;
    }

    private byte[] TryCropLegacySkin(byte[] skinBytes)
    {
        try
        {
            using var input = new MemoryStream(skinBytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth < 64 || frame.PixelHeight != 64)
            {
                return skinBytes;
            }

            var cropped = new CroppedBitmap(frame, new Int32Rect(0, 0, 64, 32));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(cropped));
            using var output = new MemoryStream();
            encoder.Save(output);
            logger.Info("已为 1.6/1.7 裁剪双层离线皮肤");
            return output.ToArray();
        }
        catch (Exception ex)
        {
            logger.Warn("离线皮肤裁剪失败，已使用原始图片：" + ex.Message);
            return skinBytes;
        }
    }

    private static string AddSkinResourcePack(string resourcePacks, bool useNewEntry)
    {
        var desired = useNewEntry ? "\"file/PCL2 Skin.zip\"" : "\"PCL2 Skin.zip\"";
        var entries = ParseResourcePacks(resourcePacks)
            .Where(entry => !IsSkinResourcePackEntry(entry))
            .ToList();
        if (useNewEntry && entries.Count == 0)
        {
            entries.Add("\"vanilla\"");
        }

        entries.Add(desired);
        return "[" + string.Join(",", entries) + "]";
    }

    private static string RemoveSkinResourcePack(string resourcePacks, bool useNewEntry)
    {
        var entries = ParseResourcePacks(resourcePacks)
            .Where(entry => !IsSkinResourcePackEntry(entry))
            .ToList();
        if (useNewEntry && entries.Count == 0)
        {
            entries.Add("\"vanilla\"");
        }

        return "[" + string.Join(",", entries) + "]";
    }

    private static IReadOnlyList<string> ParseResourcePacks(string resourcePacks)
    {
        return resourcePacks
            .Trim()
            .TrimStart('[')
            .TrimEnd(']')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .ToList();
    }

    private static bool IsSkinResourcePackEntry(string entry)
    {
        return string.Equals(entry, "\"file/PCL2 Skin.zip\"", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry, "\"PCL2 Skin.zip\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UsesNewResourcePackEntry(Version version)
    {
        return version.Major == 1 && version.Minor >= 13;
    }

    private static bool UsesModernPlayerSkinPath(Version version)
    {
        return version.Major == 1 && (version.Minor > 19 || version.Minor == 19 && version.Build >= 3);
    }

    private static int GetResourcePackFormat(Version version)
    {
        if (version.Major != 1)
        {
            return 17;
        }

        return version.Minor switch
        {
            <= 8 => 1,
            <= 10 => 2,
            <= 12 => 3,
            <= 14 => 4,
            15 => 5,
            16 => 6,
            17 => 7,
            18 => version.Build <= 2 ? 8 : 9,
            19 => version.Build <= 3 ? 9 : 12,
            20 => version.Build <= 1 ? 15 : 17,
            _ => 17
        };
    }

    private static Version ParseMinecraftVersionOrDefault(string vanillaVersion)
    {
        return TryGetMinecraftVersion(vanillaVersion, out var version)
            ? version
            : new Version(1, 20, 2);
    }

    private static bool TryGetMinecraftVersion(string vanillaVersion, out Version version)
    {
        var normalized = vanillaVersion.Trim();
        if (Version.TryParse(normalized, out version!))
        {
            return true;
        }

        version = new Version(0, 0);
        return false;
    }

    private static Dictionary<string, string> ReadOptions(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return result;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var index = line.IndexOf(':');
            if (index > 0)
            {
                result[line[..index]] = line[(index + 1)..];
            }
        }

        return result;
    }

    private static string NormalizeProfileId(string uuid)
    {
        var normalized = uuid.Replace("-", "", StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "00000000000000000000000000000000"
            : normalized.ToLowerInvariant();
    }
}
