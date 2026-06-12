using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class DownloadSourceService(IAppSettingsService settings) : IDownloadSourceService
{
    private const int OfficialFastThresholdMs = 4000;
    private bool _preferOfficialDownloadsWhenAuto;

    public bool PreferOfficialDownloadsWhenAuto => _preferOfficialDownloadsWhenAuto;

    public IReadOnlyList<string> OrderSources(IEnumerable<string> officialUrls, IEnumerable<string> mirrorUrls)
    {
        var sourceMode = settings.Get(AppSettingKeys.ToolDownloadSource, 1);
        var preferOfficial = sourceMode == 2 || (sourceMode == 1 && _preferOfficialDownloadsWhenAuto);
        var ordered = preferOfficial
            ? officialUrls.Concat(mirrorUrls)
            : mirrorUrls.Concat(officialUrls);
        return ordered.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> GetLauncherOrMetaSources(string original)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            throw new ArgumentException("\u65e0\u5bf9\u5e94\u7684 json \u4e0b\u8f7d\u5730\u5740", nameof(original));
        }

        return OrderSources([original], [
            original
                .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase)
                .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase)
                .Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase)
                .Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase)
        ]);
    }

    public IReadOnlyList<string> GetLibrarySources(string original)
    {
        var mirrors = new[]
        {
            original
                .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                .Replace("https://maven.fabricmc.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                .Replace("https://maven.quiltmc.org/repository/release", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                .Replace("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                .Replace("https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase),
            original
                .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/libraries", StringComparison.OrdinalIgnoreCase)
                .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/libraries", StringComparison.OrdinalIgnoreCase)
                .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/libraries", StringComparison.OrdinalIgnoreCase)
                .Replace("https://maven.fabricmc.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                .Replace("https://maven.quiltmc.org/repository/release", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                .Replace("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                .Replace("https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
        };
        if (original.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase)
            || original.Contains("fabricmc", StringComparison.OrdinalIgnoreCase)
            || original.Contains("quiltmc", StringComparison.OrdinalIgnoreCase)
            || original.Contains("neoforged", StringComparison.OrdinalIgnoreCase))
        {
            return mirrors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return OrderSources([original], mirrors);
    }

    public IReadOnlyList<string> GetAssetSources(string original)
    {
        original = original.Replace("http://resources.download.minecraft.net", "https://resources.download.minecraft.net", StringComparison.OrdinalIgnoreCase);
        return OrderSources([original], [
            original
                .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/assets", StringComparison.OrdinalIgnoreCase)
                .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/assets", StringComparison.OrdinalIgnoreCase)
                .Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets", StringComparison.OrdinalIgnoreCase)
        ]);
    }

    public string GetModMirrorSource(string original)
    {
        return original
            .Replace("api.modrinth.com", "mod.mcimirror.top/modrinth", StringComparison.OrdinalIgnoreCase)
            .Replace("staging-api.modrinth.com", "mod.mcimirror.top/modrinth", StringComparison.OrdinalIgnoreCase)
            .Replace("cdn.modrinth.com", "mod.mcimirror.top", StringComparison.OrdinalIgnoreCase)
            .Replace("api.curseforge.com", "mod.mcimirror.top/curseforge", StringComparison.OrdinalIgnoreCase)
            .Replace("edge.forgecdn.net", "mod.mcimirror.top", StringComparison.OrdinalIgnoreCase)
            .Replace("mediafilez.forgecdn.net", "mod.mcimirror.top", StringComparison.OrdinalIgnoreCase)
            .Replace("media.forgecdn.net", "mod.mcimirror.top", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetModFileSources(string original)
    {
        var mirror = GetModMirrorSource(original);
        if (string.Equals(mirror, original, StringComparison.OrdinalIgnoreCase))
        {
            return [original];
        }

        return settings.Get(AppSettingKeys.ToolDownloadMod, 2) switch
        {
            0 => [mirror, original],
            1 => [original, mirror],
            _ => [original, mirror]
        };
    }

    public void ReportOfficialVersionListLatency(TimeSpan elapsed)
    {
        if (!_preferOfficialDownloadsWhenAuto && elapsed.TotalMilliseconds < OfficialFastThresholdMs)
        {
            _preferOfficialDownloadsWhenAuto = true;
        }
    }
}
