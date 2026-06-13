using System.IO;
using System.Text.Json;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.FeatureHub;

public sealed class FeatureHubService(IAppPathService paths, IAppSettingsService settings) : IFeatureHubService
{
    public IReadOnlyList<FeatureModuleSnapshot> GetModules()
    {
        var account = GetAccountSummary();
        var crash = AnalyzeCrashes();
        var skin = GetSkinSummary();
        return
        [
            new("更新系统", "基础可用", "已有 GitHub Release 检查服务，更多页提供手动检查入口。", "补版本通道、更新日志、下载更新包与重启安装。"),
            new("崩溃分析", crash.ReportCount > 0 ? "可读取报告" : "等待报告", "已预留崩溃报告扫描摘要，后续接入启动失败弹窗和原因匹配规则。", "补日志聚合、常见崩溃规则库、复制诊断包。"),
            new("主页公告", "本地占位", "已预留主页信息流模型，先显示本地重构进度与重要提醒。", "接入公告源、缓存、分类和关闭公告设置。"),
            new("账号管理中心", account.CachedAccountCount > 0 ? "可读取缓存" : "入口占位", "已读取当前登录方式和缓存账号数量，暂不做批量删除。", "补账号列表、切换、删除、刷新凭据和安全提示。"),
            new("皮肤中心", skin.Status, "已读取离线皮肤设置，和启动链使用同一批设置键。", "补文件选择、皮肤预览、缓存刷新和资源包生成。"),
            new("扩展点", "目录占位", "已整理帮助事件、下载任务、联机、诊断等未来扩展点。", "补插件清单、实验功能开关和事件权限边界。")
        ];
    }

    public IReadOnlyList<HomeFeedItem> GetHomeFeedItems()
    {
        return
        [
            new("PCL Sharp 仍是实验性重构版", "当前目标是功能一比一还原原版 PCL，但内部实现会继续用服务化和测试体系重做。", "公告"),
            new("联机入口已预留", "陶瓦联机作为主入口，EasyTier 作为底层/高级模式保留，后续接入进程启动与端口转发。", "联机"),
            new("崩溃分析将接入启动链", "启动失败和游戏崩溃会逐步进入统一诊断页，减少只看日志猜问题的情况。", "诊断")
        ];
    }

    public CrashAnalysisSummary AnalyzeCrashes()
    {
        var roots = GetMinecraftRoots();
        var reports = roots
            .Select(root => Path.Combine(root, "crash-reports"))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        var latest = reports.FirstOrDefault();
        return latest is null
            ? new CrashAnalysisSummary("未发现 Minecraft 崩溃报告", "", null, 0)
            : new CrashAnalysisSummary(
                "发现最近崩溃报告，后续会接入规则分析",
                latest.FullName,
                latest.LastWriteTime,
                reports.Count);
    }

    public AccountCenterSummary GetAccountSummary()
    {
        var loginType = settings.Get(AppSettingKeys.LoginType, LoginType.Legacy);
        var legacyName = settings.Get(AppSettingKeys.LoginLegacyName, "Steve");
        var msName = settings.Get(AppSettingKeys.CacheMsV2Name, "");
        var accounts = CountMicrosoftAccounts(settings.Get(AppSettingKeys.LoginMsJson, "{}"));
        var displayName = loginType == LoginType.Ms && !string.IsNullOrWhiteSpace(msName)
            ? msName
            : legacyName;

        return new AccountCenterSummary(
            "账号中心入口已预留",
            loginType.ToString(),
            displayName,
            accounts);
    }

    public SkinCenterSummary GetSkinSummary()
    {
        var skinType = settings.Get(AppSettingKeys.LaunchSkinType, 0);
        var skinName = settings.Get(AppSettingKeys.LaunchSkinID, "");
        var slim = settings.Get(AppSettingKeys.LaunchSkinSlim, false);
        var mode = skinType switch
        {
            0 => "随机",
            1 => "Steve",
            2 => "Alex",
            3 => "正版皮肤",
            4 => "自定义",
            _ => "未知"
        };

        return new SkinCenterSummary(
            "读取设置",
            mode,
            string.IsNullOrWhiteSpace(skinName) ? "未指定" : skinName,
            slim);
    }

    public IReadOnlyList<ExtensionPointInfo> GetExtensionPoints()
    {
        return
        [
            new("帮助事件", "继续承接原版 Help 中的打开网页、切换页面、下载文件、加入房间等事件。", "部分接入"),
            new("下载任务", "自定义下载、资源下载、文件补全都走统一下载队列，便于后续插件化。", "基础可用"),
            new("联机后端", "陶瓦联机 / EasyTier 的二进制管理、进程启动、日志采集会在这里接入。", "占位"),
            new("诊断规则", "崩溃报告、启动日志、Java 兼容性和文件缺失将使用统一规则目录。", "占位"),
            new("实验开关", "未来承载新功能投票、灰度选项和高级调试开关。", "占位")
        ];
    }

    private IReadOnlyList<string> GetMinecraftRoots()
    {
        var roots = new List<string>();
        var saved = settings.Get(AppSettingKeys.MinecraftRootPath, "");
        if (!string.IsNullOrWhiteSpace(saved))
        {
            roots.Add(saved);
        }

        var folders = settings.Get(AppSettingKeys.LaunchFolders, "");
        foreach (var item in folders.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            roots.Add(item);
        }

        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"));
        roots.Add(paths.AppDataDirectory);
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int CountMicrosoftAccounts(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.GetArrayLength(),
                JsonValueKind.Object => document.RootElement.EnumerateObject().Count(),
                _ => 0
            };
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
