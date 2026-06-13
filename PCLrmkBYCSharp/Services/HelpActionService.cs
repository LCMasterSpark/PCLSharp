using System.Diagnostics;
using System.Windows;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public sealed class HelpActionService : IHelpActionService
{
    public const string EventOpenWebsite = "打开网页";
    public const string EventOpenFile = "打开文件";
    public const string EventExecuteCommand = "执行命令";
    public const string EventCopyText = "复制文本";
    public const string EventMessageBox = "弹出窗口";
    public const string EventHint = "弹出提示";
    public const string EventModifySetting = "修改设置";
    public const string EventWriteSetting = "写入设置";
    public const string EventOpenHelp = "打开帮助";
    public const string EventRefreshHome = "刷新主页";
    public const string EventRefreshHelp = "刷新帮助";
    public const string EventRefreshPage = "刷新页面";
    public const string EventSwitchPage = "切换页面";
    public const string EventImportModpack = "导入整合包";
    public const string EventInstallModpack = "安装整合包";
    public const string EventDownloadFile = "下载文件";
    public const string EventModifyVariable = "修改变量";
    public const string EventWriteVariable = "写入变量";
    public const string EventJoinRoom = "加入房间";
    public const string EventCheckUpdate = "检查更新";
    public const string EventMemoryOptimize = "内存优化";
    public const string EventClearRubbish = "清理垃圾";
    public const string EventJrrp = "今日人品";
    public const string EventLaunchGame = "启动游戏";

    private readonly Func<Uri, CancellationToken, Task> _openUri;
    private readonly Func<string, string, string?, CancellationToken, Task> _startProcess;
    private readonly Func<Uri, string?, CancellationToken, Task>? _downloadFile;
    private readonly Action<string, string> _showMessage;
    private readonly Action<string, string?> _showHint;
    private readonly Action<string> _setClipboardText;
    private readonly Func<PageRoute, CancellationToken, Task>? _switchPage;
    private readonly IAppSettingsService? _settings;
    private readonly Dictionary<string, Func<string, CancellationToken, Task<HelpActionResult>>> _eventHandlers = new(StringComparer.OrdinalIgnoreCase);

    public sealed record ModpackDownloadPreset(string SearchText, string GameVersion, string Loader);

    public HelpActionService(
        Func<Uri, CancellationToken, Task>? openUri = null,
        Action<string, string>? showMessage = null,
        Func<string, string, string?, CancellationToken, Task>? startProcess = null,
        Func<Uri, string?, CancellationToken, Task>? downloadFile = null,
        Action<string>? setClipboardText = null,
        Action<string, string?>? showHint = null,
        Func<PageRoute, CancellationToken, Task>? switchPage = null,
        IAppSettingsService? settings = null)
    {
        _openUri = openUri ?? OpenUriWithShellAsync;
        _startProcess = startProcess ?? StartProcessAsync;
        _downloadFile = downloadFile;
        _showMessage = showMessage ?? ((_, _) => { });
        _setClipboardText = setClipboardText ?? Clipboard.SetText;
        _showHint = showHint ?? ((_, _) => { });
        _switchPage = switchPage;
        _settings = settings;
    }

    public void SetLaunchGameHandler(Func<string, CancellationToken, Task<HelpActionResult>> launchGame)
    {
        SetEventHandler(EventLaunchGame, launchGame);
    }

    public void SetEventHandler(string eventType, Func<string, CancellationToken, Task<HelpActionResult>> handler)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("事件类型不能为空", nameof(eventType));
        }

        _eventHandlers[eventType.Trim()] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public async Task<HelpActionResult> ExecuteAsync(HelpEntry entry, CancellationToken cancellationToken = default)
    {
        if (!entry.IsEvent)
        {
            return new HelpActionResult(true, "已显示帮助正文");
        }

        var eventType = entry.EventType.Trim();
        if (string.Equals(eventType, EventOpenWebsite, StringComparison.OrdinalIgnoreCase))
        {
            return await OpenWebsiteAsync(entry.EventData, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(eventType, EventOpenFile, StringComparison.OrdinalIgnoreCase))
        {
            var command = ParseCommandData(entry.EventData);
            if (string.IsNullOrWhiteSpace(command.FileName))
            {
                return new HelpActionResult(false, "打开文件事件缺少文件路径");
            }

            await _startProcess(command.FileName, command.Arguments, command.WorkingDirectory, cancellationToken).ConfigureAwait(false);
            return new HelpActionResult(true, "已打开文件：" + command.FileName);
        }

        if (string.Equals(eventType, EventExecuteCommand, StringComparison.OrdinalIgnoreCase))
        {
            var command = ParseCommandData(entry.EventData);
            if (string.IsNullOrWhiteSpace(command.FileName))
            {
                return new HelpActionResult(false, "执行命令事件缺少命令路径");
            }

            await _startProcess(command.FileName, command.Arguments, command.WorkingDirectory, cancellationToken).ConfigureAwait(false);
            return new HelpActionResult(true, "已执行命令：" + command.FileName);
        }

        if (string.Equals(eventType, EventCopyText, StringComparison.OrdinalIgnoreCase))
        {
            _setClipboardText(entry.EventData);
            return new HelpActionResult(true, "已复制文本");
        }

        if (string.Equals(eventType, EventMessageBox, StringComparison.OrdinalIgnoreCase))
        {
            var parts = entry.EventData.Split('|', 3);
            if (parts.Length < 2)
            {
                return new HelpActionResult(false, "弹窗事件缺少标题或内容");
            }

            _showMessage(Unescape(parts[0]), Unescape(parts[1]));
            return new HelpActionResult(true, "已显示帮助提示：" + parts[0]);
        }

        if (string.Equals(eventType, EventHint, StringComparison.OrdinalIgnoreCase))
        {
            var parts = entry.EventData.Split('|', 2);
            var message = Unescape(parts[0]);
            var hintType = parts.Length > 1 ? parts[1] : null;
            _showHint(message, hintType);
            return new HelpActionResult(true, "已显示提示：" + message);
        }

        if (string.Equals(eventType, EventModifySetting, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, EventWriteSetting, StringComparison.OrdinalIgnoreCase))
        {
            if (_settings is null)
            {
                return new HelpActionResult(false, "设置服务未初始化，无法执行设置事件");
            }

            var parts = entry.EventData.Split('|', 2);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                return new HelpActionResult(false, $"EventType {eventType} 需要至少 2 个以 | 分割的参数");
            }

            _settings.Set(parts[0].Trim(), Unescape(parts[1]));
            await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
            return new HelpActionResult(true, $"已写入设置：{parts[0].Trim()} -> {Unescape(parts[1])}");
        }

        if (string.Equals(eventType, EventModifyVariable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, EventWriteVariable, StringComparison.OrdinalIgnoreCase))
        {
            return await WriteCustomVariableAsync(entry.EventData, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(eventType, EventDownloadFile, StringComparison.OrdinalIgnoreCase))
        {
            return await DownloadFileAsync(entry.EventData, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(eventType, EventMemoryOptimize, StringComparison.OrdinalIgnoreCase))
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            return new HelpActionResult(true, "已执行内存优化");
        }

        if (string.Equals(eventType, EventClearRubbish, StringComparison.OrdinalIgnoreCase))
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            return new HelpActionResult(true, "已执行基础垃圾清理");
        }

        if (string.Equals(eventType, EventJrrp, StringComparison.OrdinalIgnoreCase))
        {
            var seed = HashCode.Combine(DateTime.Today, Environment.UserName);
            var value = Math.Abs(seed % 101);
            _showHint($"今日人品：{value}", value >= 80 ? "Green" : value <= 20 ? "Red" : "Blue");
            return new HelpActionResult(true, $"今日人品：{value}");
        }

        if (string.Equals(eventType, EventOpenHelp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, EventRefreshHelp, StringComparison.OrdinalIgnoreCase))
        {
            var handled = await TryExecuteRegisteredHandlerAsync(eventType, entry.EventData, cancellationToken).ConfigureAwait(false);
            return handled ?? new HelpActionResult(false, "该帮助事件需要在帮助页内执行：" + eventType);
        }

        if (string.Equals(eventType, EventRefreshHome, StringComparison.OrdinalIgnoreCase))
        {
            var handled = await TryExecuteRegisteredHandlerAsync(eventType, entry.EventData, cancellationToken).ConfigureAwait(false);
            return handled ?? new HelpActionResult(false, "刷新主页事件需要接入启动页后执行");
        }

        if (string.Equals(eventType, EventRefreshPage, StringComparison.OrdinalIgnoreCase))
        {
            var handled = await TryExecuteRegisteredHandlerAsync(eventType, entry.EventData, cancellationToken).ConfigureAwait(false);
            return handled ?? new HelpActionResult(true, "已请求刷新页面");
        }

        if (string.Equals(eventType, EventSwitchPage, StringComparison.OrdinalIgnoreCase))
        {
            var handled = await TryExecuteRegisteredHandlerAsync(eventType, entry.EventData, cancellationToken).ConfigureAwait(false);
            if (handled is not null)
            {
                return handled;
            }

            return await SwitchPageAsync(entry.EventData, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(eventType, EventImportModpack, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, EventInstallModpack, StringComparison.OrdinalIgnoreCase))
        {
            var handled = await TryExecuteRegisteredHandlerAsync(eventType, entry.EventData, cancellationToken).ConfigureAwait(false);
            return handled ?? new HelpActionResult(false, "整合包事件需要接入下载页后执行：" + eventType);
        }

        if (string.Equals(eventType, EventJoinRoom, StringComparison.OrdinalIgnoreCase))
        {
            var handled = await TryExecuteRegisteredHandlerAsync(eventType, entry.EventData, cancellationToken).ConfigureAwait(false);
            return handled ?? new HelpActionResult(false, "加入房间事件需要接入联机页面后执行");
        }

        if (string.Equals(eventType, EventCheckUpdate, StringComparison.OrdinalIgnoreCase))
        {
            var handled = await TryExecuteRegisteredHandlerAsync(eventType, entry.EventData, cancellationToken).ConfigureAwait(false);
            return handled ?? new HelpActionResult(false, "检查更新事件需要接入更新服务后执行");
        }

        if (string.Equals(eventType, EventLaunchGame, StringComparison.OrdinalIgnoreCase))
        {
            var handled = await TryExecuteRegisteredHandlerAsync(eventType, entry.EventData, cancellationToken).ConfigureAwait(false);
            return handled ?? new HelpActionResult(false, "启动游戏事件需要接入启动页后执行");
        }

        return new HelpActionResult(false, "暂不支持该帮助事件：" + entry.EventType);
    }

    private async Task<HelpActionResult> SwitchPageAsync(string eventData, CancellationToken cancellationToken)
    {
        if (_switchPage is null)
        {
            return new HelpActionResult(false, "切换页面事件需要接入主窗口导航后执行");
        }

        var parts = eventData.Split('|', StringSplitOptions.TrimEntries);
        var pageType = parts.FirstOrDefault() ?? "";
        if (!TryMapOldPclPageRoute(pageType, out var route, out var message))
        {
            return new HelpActionResult(false, message);
        }

        await _switchPage(route, cancellationToken).ConfigureAwait(false);
        return new HelpActionResult(true, "已切换页面：" + GetRouteDisplayName(route));
    }

    public static bool TryMapOldPclPageRoute(string pageType, out PageRoute route, out string message)
    {
        var normalized = (pageType ?? "").Trim();
        route = PageRoute.Launch;
        message = "";
        if (string.IsNullOrWhiteSpace(normalized))
        {
            message = "切换页面事件缺少页面类型";
            return false;
        }

        if (int.TryParse(normalized, out var numeric))
        {
            return TryMapOldPclPageRouteNumber(numeric, out route, out message);
        }

        normalized = normalized
            .Replace("Page", "", StringComparison.OrdinalIgnoreCase)
            .Replace("页", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        return normalized.ToLowerInvariant() switch
        {
            "launch" or "启动" or "home" or "main" => Succeed(PageRoute.Launch, out route, out message),
            "download" or "downloadinstall" or "downloadmod" or "downloadpack" or "downloaddatapack" or "downloadresourcepack" or "downloadshader" or "downloadmanager" or "下载" or "下载管理" or "资源" or "社区资源" => Succeed(PageRoute.Download, out route, out message),
            "setup" or "setting" or "settings" or "设置" => Succeed(PageRoute.Setup, out route, out message),
            "link" or "linkmain" or "联机" or "陶瓦联机" or "easytier" or "terracotta" => Succeed(PageRoute.Link, out route, out message),
            "other" or "help" or "about" or "test" or "helpdetail" or "更多" or "帮助" or "关于" => Succeed(PageRoute.Other, out route, out message),
            "instanceselect" or "instancesetup" or "instance" or "version" or "versionselect" or "版本选择" or "版本设置" or "实例" or "版本" => Succeed(PageRoute.Instance, out route, out message),
            _ => Fail("未知页面类型：" + pageType, out route, out message)
        };
    }

    public static ModpackDownloadPreset ParseModpackDownloadPreset(string eventData)
    {
        var parts = eventData.Split('|', 3, StringSplitOptions.TrimEntries);
        return new ModpackDownloadPreset(
            Unescape(parts.ElementAtOrDefault(0) ?? "").Trim(),
            Unescape(parts.ElementAtOrDefault(1) ?? "").Trim(),
            Unescape(parts.ElementAtOrDefault(2) ?? "").Trim());
    }

    private static bool TryMapOldPclPageRouteNumber(int pageType, out PageRoute route, out string message)
    {
        return pageType switch
        {
            0 => Succeed(PageRoute.Launch, out route, out message),
            1 or 6 or 8 => Succeed(PageRoute.Download, out route, out message),
            3 => Succeed(PageRoute.Setup, out route, out message),
            4 or 9 => Succeed(PageRoute.Other, out route, out message),
            5 or 7 => Succeed(PageRoute.Instance, out route, out message),
            2 => Succeed(PageRoute.Link, out route, out message),
            _ => Fail("未知页面类型：" + pageType, out route, out message)
        };
    }

    private static bool Succeed(PageRoute value, out PageRoute route, out string message)
    {
        route = value;
        message = "";
        return true;
    }

    private static bool Fail(string value, out PageRoute route, out string message)
    {
        route = PageRoute.Launch;
        message = value;
        return false;
    }

    private static string GetRouteDisplayName(PageRoute route)
    {
        return route switch
        {
            PageRoute.Launch => "启动",
            PageRoute.Download => "下载",
            PageRoute.Link => "联机",
            PageRoute.Instance => "实例",
            PageRoute.Setup => "设置",
            PageRoute.Other => "更多",
            _ => route.ToString()
        };
    }

    private async Task<HelpActionResult?> TryExecuteRegisteredHandlerAsync(
        string eventType,
        string eventData,
        CancellationToken cancellationToken)
    {
        return _eventHandlers.TryGetValue(eventType, out var handler)
            ? await handler(eventData, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private async Task<HelpActionResult> OpenWebsiteAsync(string eventData, CancellationToken cancellationToken)
    {
        var url = eventData.Replace('\\', '/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return new HelpActionResult(false, "帮助网页地址无效：" + eventData);
        }

        await _openUri(uri, cancellationToken).ConfigureAwait(false);
        return new HelpActionResult(true, "已打开网页：" + uri);
    }

    private async Task<HelpActionResult> DownloadFileAsync(string eventData, CancellationToken cancellationToken)
    {
        var parts = eventData.Split('|', 2);
        var url = Unescape(parts.ElementAtOrDefault(0) ?? "").Trim().Replace('\\', '/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return new HelpActionResult(false, "下载文件地址无效：" + eventData);
        }

        var fileName = string.IsNullOrWhiteSpace(parts.ElementAtOrDefault(1))
            ? null
            : Unescape(parts[1]).Trim();
        if (_downloadFile is not null)
        {
            await _downloadFile(uri, fileName, cancellationToken).ConfigureAwait(false);
            return new HelpActionResult(true, "已创建下载任务：" + uri);
        }

        await _openUri(uri, cancellationToken).ConfigureAwait(false);
        return new HelpActionResult(true, "已打开下载链接：" + uri);
    }

    private async Task<HelpActionResult> WriteCustomVariableAsync(string eventData, CancellationToken cancellationToken)
    {
        if (_settings is null)
        {
            return new HelpActionResult(false, "设置服务未初始化，无法执行变量事件");
        }

        var parts = eventData.Split('|', 3);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return new HelpActionResult(false, "变量事件需要至少 2 个以 | 分割的参数");
        }

        var key = "CustomEvent." + parts[0].Trim();
        var value = Unescape(parts[1]);
        _settings.Set(key, value);
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new HelpActionResult(true, $"已写入变量：{parts[0].Trim()} -> {value}");
    }

    private static CommandData ParseCommandData(string data)
    {
        var parts = data.Split('|', 3);
        return new CommandData(
            Unescape(parts.ElementAtOrDefault(0) ?? "").Trim(),
            Unescape(parts.ElementAtOrDefault(1) ?? ""),
            string.IsNullOrWhiteSpace(parts.ElementAtOrDefault(2)) ? null : Unescape(parts[2]));
    }

    private static string Unescape(string value)
    {
        return value.Replace("\\n", Environment.NewLine, StringComparison.Ordinal);
    }

    private static Task OpenUriWithShellAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private static Task StartProcessAsync(string fileName, string arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            info.WorkingDirectory = workingDirectory;
        }

        Process.Start(info);
        return Task.CompletedTask;
    }

    private sealed record CommandData(string FileName, string Arguments, string? WorkingDirectory);
}
