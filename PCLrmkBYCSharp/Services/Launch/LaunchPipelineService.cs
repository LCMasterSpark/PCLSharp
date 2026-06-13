using System.Diagnostics;
using System.IO;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LaunchPipelineService
    : ILaunchPipelineService
{
    private const string StepFileCompletion = "补全文件";
    private readonly IJavaDiscoveryService _javaDiscovery;
    private readonly IJavaSelectorService _javaSelector;
    private readonly ILoginService _login;
    private readonly ILaunchArgumentBuilder _argumentBuilder;
    private readonly ILaunchFileCompleter _fileCompleter;
    private readonly IDownloadManagerService _downloadManager;
    private readonly INativesExtractor _nativesExtractor;
    private readonly ILaunchPreRunService _preRun;
    private readonly ILaunchPatchService _patches;
    private readonly ICustomCommandService _customCommand;
    private readonly ILaunchScriptExporter _scriptExporter;
    private readonly ILaunchMemoryOptimizer _memoryOptimizer;
    private readonly IProcessLauncher _processLauncher;
    private readonly ILaunchProcessConfigurator _processConfigurator;
    private readonly IGameProcessWatcher _watcher;
    private readonly IGameWindowService _gameWindow;
    private readonly ILauncherVisibilityService _launcherVisibility;
    private readonly ILaunchWindowTitleService _windowTitle;
    private readonly IMinecraftGameDirectoryService? _gameDirectories;
    private readonly IAppSettingsService? _settings;
    private readonly IAppLoggerService _logger;
    private readonly object _stepsSync = new();
    private readonly List<LaunchStepState> _steps = [];

    public LaunchPipelineService(
        IJavaDiscoveryService javaDiscovery,
        IJavaSelectorService javaSelector,
        ILoginService login,
        ILaunchArgumentBuilder argumentBuilder,
        ILaunchFileCompleter fileCompleter,
        IDownloadManagerService downloadManager,
        INativesExtractor nativesExtractor,
        ILaunchPreRunService preRun,
        ILaunchPatchService patches,
        ICustomCommandService customCommand,
        ILaunchScriptExporter scriptExporter,
        IProcessLauncher processLauncher,
        ILaunchProcessConfigurator processConfigurator,
        IGameProcessWatcher watcher,
        IAppLoggerService logger,
        IGameWindowService? gameWindow = null,
        ILauncherVisibilityService? launcherVisibility = null,
        ILaunchWindowTitleService? windowTitle = null,
        IMinecraftGameDirectoryService? gameDirectories = null,
        IAppSettingsService? settings = null,
        ILaunchMemoryOptimizer? memoryOptimizer = null)
    {
        _javaDiscovery = javaDiscovery;
        _javaSelector = javaSelector;
        _login = login;
        _argumentBuilder = argumentBuilder;
        _fileCompleter = fileCompleter;
        _downloadManager = downloadManager;
        _nativesExtractor = nativesExtractor;
        _preRun = preRun;
        _patches = patches;
        _customCommand = customCommand;
        _scriptExporter = scriptExporter;
        _memoryOptimizer = memoryOptimizer ?? NoopLaunchMemoryOptimizer.Instance;
        _processLauncher = processLauncher;
        _processConfigurator = processConfigurator;
        _watcher = watcher;
        _gameWindow = gameWindow ?? NoopGameWindowService.Instance;
        _launcherVisibility = launcherVisibility ?? NoopLauncherVisibilityService.Instance;
        _windowTitle = windowTitle ?? NoopLaunchWindowTitleService.Instance;
        _gameDirectories = gameDirectories;
        _settings = settings;
        _logger = logger;
    }

    public event EventHandler<IReadOnlyList<LaunchStepState>>? StepsChanged;

    public IReadOnlyList<LaunchStepState> Steps
    {
        get
        {
            lock (_stepsSync)
            {
                return _steps.ToArray();
            }
        }
    }

    public async Task<LaunchResult> GenerateProfileAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        ResetSteps();
        var issues = ValidateRequest(request);
        if (issues.Count > 0)
        {
            SetStep("预检测", LaunchStepStatus.Failed, issues[0].Message);
            return LaunchResult.Failed(issues.ToArray());
        }

        var instance = request.Instance!;
        SetStep("预检测", LaunchStepStatus.Succeeded, "预检测已通过");

        SetStep("获取 Java", LaunchStepStatus.Running, "正在选择 Java");
        var javaCandidates = await GetJavaCandidatesAsync(request, instance, cancellationToken).ConfigureAwait(false);
        var java = SelectJava(request, instance, javaCandidates);
        if (java is null)
        {
            var requirement = _javaSelector.GetRequirement(instance);
            var issue = new LaunchValidationIssue("JavaNotFound", $"未找到满足 {requirement.MinVersion} - {requirement.MaxVersion} 的 Java");
            SetStep("获取 Java", LaunchStepStatus.Failed, issue.Message);
            return LaunchResult.Failed(issue);
        }

        SetStep("获取 Java", LaunchStepStatus.Succeeded, java.DisplayName);

        SetStep("登录", LaunchStepStatus.Running, "正在登录");
        LoginSession login;
        try
        {
            login = await _login.LoginAsync(CreateLoginRequest(request), cancellationToken).ConfigureAwait(false);
            SetStep("登录", LaunchStepStatus.Succeeded, login.UserName);
        }
        catch (Exception ex)
        {
            var issue = new LaunchValidationIssue("LoginInvalid", ex.Message);
            SetStep("登录", LaunchStepStatus.Failed, issue.Message);
            return LaunchResult.Failed(issue);
        }

        SetStep(StepFileCompletion, LaunchStepStatus.Skipped, "生成参数模式不下载文件");
        SetStep("获取启动参数", LaunchStepStatus.Running, "正在生成启动参数");
        var arguments = _argumentBuilder.Build(request, java, login);
        IReadOnlyList<string> missingFiles;
        if (ShouldDisableFileCheck(request))
        {
            missingFiles = [];
            SetStep("本地缺失检查", LaunchStepStatus.Skipped, "已按版本设置关闭文件校验");
        }
        else
        {
        var completion = await _fileCompleter.BuildCompletionPlanAsync(request, arguments.MissingFiles, cancellationToken).ConfigureAwait(false);
        SetStep(
            "本地缺失检查",
            completion.MissingFiles.Count == 0 ? LaunchStepStatus.Succeeded : LaunchStepStatus.Failed,
            completion.MissingFiles.Count == 0 ? "本地文件完整" : $"缺少 {completion.MissingFiles.Count} 个本地文件");
        SetStep("获取启动参数", LaunchStepStatus.Succeeded, "启动参数已生成");

            missingFiles = completion.MissingFiles;
        }

        var startInfo = CreateStartInfo(request, java, arguments.Arguments);
        var profile = new LaunchProfile(
            instance,
            java,
            login,
            arguments.Arguments,
            arguments.SanitizedCommandLine,
            startInfo,
            missingFiles);

        return new LaunchResult(true, profile, [], null);
    }

    public async Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        var profileResult = await GenerateProfileAsync(request, cancellationToken).ConfigureAwait(false);
        if (!profileResult.Success || profileResult.Profile is null)
        {
            return profileResult;
        }

        if (profileResult.Profile.MissingFiles.Count > 0)
        {
            var completionResult = await CompleteMissingFilesAsync(request, profileResult.Profile.MissingFiles, cancellationToken).ConfigureAwait(false);
            if (!completionResult.Success)
            {
                return new LaunchResult(false, profileResult.Profile, completionResult.Issues, null);
            }

            profileResult = await GenerateProfileAsync(request, cancellationToken).ConfigureAwait(false);
            if (!profileResult.Success || profileResult.Profile is null)
            {
                return profileResult;
            }

            SetStep("补全文件", LaunchStepStatus.Succeeded, completionResult.Message);
            if (profileResult.Profile.MissingFiles.Count > 0)
            {
                var sample = string.Join("、", profileResult.Profile.MissingFiles
                    .Take(3)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
                var suffix = string.IsNullOrWhiteSpace(sample) ? "" : $"：{sample}";
                var issue = new LaunchValidationIssue("MissingLocalFiles", $"已尝试自动补全，但仍缺少 {profileResult.Profile.MissingFiles.Count} 个本地文件{suffix}");
                SetStep("本地缺失检查", LaunchStepStatus.Failed, issue.Message);
                return new LaunchResult(false, profileResult.Profile, [issue], null);
            }
        }

        if (ShouldOptimizeMemory(request))
        {
            SetStep("内存优化", LaunchStepStatus.Running, "正在进行启动前内存优化");
            try
            {
                var optimizeResult = await _memoryOptimizer.OptimizeAsync(cancellationToken).ConfigureAwait(false);
                SetStep("内存优化", LaunchStepStatus.Succeeded, $"已处理 {optimizeResult.ProcessCount} 个进程");
            }
            catch (OperationCanceledException)
            {
                SetStep("内存优化", LaunchStepStatus.Failed, "内存优化已取消");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "启动前内存优化失败");
                var issue = new LaunchValidationIssue("MemoryOptimizeFailed", ex.Message);
                SetStep("内存优化", LaunchStepStatus.Failed, issue.Message);
                return new LaunchResult(false, profileResult.Profile, [issue], null);
            }
        }
        else
        {
            SetStep("内存优化", LaunchStepStatus.Skipped, "未启用启动前内存优化");
        }

        SetStep("解压文件", LaunchStepStatus.Running, "正在解压 natives");
        await _nativesExtractor.ExtractAsync(profileResult.Profile.Instance, cancellationToken).ConfigureAwait(false);
        SetStep("解压文件", LaunchStepStatus.Succeeded, "natives 已准备");

        SetStep("预启动处理", LaunchStepStatus.Running, "正在更新启动前配置");
        await _preRun.PrepareAsync(request, profileResult.Profile.Login, cancellationToken).ConfigureAwait(false);
        SetStep("预启动处理", LaunchStepStatus.Succeeded, "预启动处理完成");

        SetStep("准备补丁文件", LaunchStepStatus.Running, "正在准备 JLW/LUA 补丁文件");
        var patchResult = await _patches.PrepareAsync(profileResult.Profile, cancellationToken).ConfigureAwait(false);
        if (!patchResult.Success)
        {
            var issue = new LaunchValidationIssue("PatchPrepareFailed", patchResult.Message);
            SetStep("准备补丁文件", LaunchStepStatus.Failed, patchResult.Message);
            return new LaunchResult(false, profileResult.Profile, [issue], null);
        }

        SetStep(
            "准备补丁文件",
            patchResult.PreparedFiles.Count == 0 ? LaunchStepStatus.Skipped : LaunchStepStatus.Succeeded,
            patchResult.Message);

        SetStep("执行自定义命令", LaunchStepStatus.Running, "正在执行自定义命令");
        await _customCommand.RunAsync(request, profileResult.Profile, cancellationToken).ConfigureAwait(false);
        SetStep("执行自定义命令", LaunchStepStatus.Succeeded, "自定义命令执行完成");

        var exported = await _scriptExporter.ExportAsync(profileResult.Profile, request.SaveBatchPath, cancellationToken).ConfigureAwait(false);
        if (exported is not null)
        {
            SetStep("启动进程", LaunchStepStatus.Skipped, "已导出启动脚本：" + exported);
            return profileResult;
        }

        if (!request.StartProcess)
        {
            SetStep("启动进程", LaunchStepStatus.Skipped, "仅生成参数，未启动进程");
            return profileResult;
        }

        SetStep("启动进程", LaunchStepStatus.Running, "正在启动游戏进程");
        try
        {
            _processConfigurator.PrepareStart(profileResult.Profile);
            var process = _processLauncher.Start(profileResult.Profile.ProcessStartInfo);
            _processConfigurator.Configure(process);
            _logger.Info($"已启动游戏进程：{profileResult.Profile.Java.PathJava}");
            SetStep("启动进程", LaunchStepStatus.Succeeded, $"进程 ID：{process.Id}");

            SetStep("等待游戏窗口", LaunchStepStatus.Running, "正在监控游戏进程");
            var watchResult = await _watcher.WatchAsync(process, cancellationToken).ConfigureAwait(false);
            if (watchResult.HasExited && watchResult.ExitCode != 0)
            {
                var issue = BuildEarlyGameExitIssue(watchResult);
                SetStep("等待游戏窗口", LaunchStepStatus.Failed, issue.Message);
                return new LaunchResult(false, profileResult.Profile, [issue], process);
            }

            var windowTitle = _windowTitle.ResolveTitle(profileResult.Profile);
            if (!string.IsNullOrWhiteSpace(windowTitle))
            {
                _gameWindow.ScheduleSetTitle(process, windowTitle, TimeSpan.Zero, cancellationToken);
            }

            if (request.WindowType == 4)
            {
                _gameWindow.ScheduleMaximize(process, TimeSpan.FromSeconds(2), cancellationToken);
            }

            SetStep("等待游戏窗口", LaunchStepStatus.Succeeded, "游戏进程监控已接入");
            _launcherVisibility.ApplyAfterLaunch(request.LauncherVisibility, process, cancellationToken);
            SetStep("结束处理", LaunchStepStatus.Succeeded, $"启动器可见性：{request.LauncherVisibility}");
            return profileResult with { Process = process };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "启动游戏进程失败");
            var issue = new LaunchValidationIssue("ProcessStartFailed", ex.Message);
            SetStep("启动进程", LaunchStepStatus.Failed, issue.Message);
            return new LaunchResult(false, profileResult.Profile, [issue], null);
        }
    }

    private sealed class NoopGameWindowService : IGameWindowService
    {
        public static readonly NoopGameWindowService Instance = new();

        public void ScheduleMaximize(Process process, TimeSpan delay, CancellationToken cancellationToken = default)
        {
        }

        public void ScheduleSetTitle(Process process, string titleTemplate, TimeSpan delay, CancellationToken cancellationToken = default)
        {
        }
    }

    private sealed class NoopLauncherVisibilityService : ILauncherVisibilityService
    {
        public static readonly NoopLauncherVisibilityService Instance = new();

        public void ApplyAfterLaunch(int launcherVisibility, Process gameProcess, CancellationToken cancellationToken = default)
        {
        }
    }

    private sealed class NoopLaunchWindowTitleService : ILaunchWindowTitleService
    {
        public static readonly NoopLaunchWindowTitleService Instance = new();

        public string ResolveTitle(LaunchProfile profile)
        {
            return "";
        }
    }

    private sealed class NoopLaunchMemoryOptimizer : ILaunchMemoryOptimizer
    {
        public static readonly NoopLaunchMemoryOptimizer Instance = new();

        public Task<LaunchMemoryOptimizeResult> OptimizeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LaunchMemoryOptimizeResult(0));
        }
    }

    private async Task<FileCompletionLaunchResult> CompleteMissingFilesAsync(LaunchRequest request, IReadOnlyList<string> initialMissingFiles, CancellationToken cancellationToken)
    {
        var currentMissing = initialMissingFiles;
        var downloadedFiles = 0;
        for (var pass = 1; pass <= 3; pass++)
        {
            var completion = await _fileCompleter.BuildCompletionPlanAsync(request, currentMissing, cancellationToken).ConfigureAwait(false);
            if (completion.MissingFiles.Count == 0)
            {
                return FileCompletionLaunchResult.Ok(downloadedFiles == 0 ? "文件已完整" : $"已补全 {downloadedFiles} 个文件");
            }

            var missingSet = completion.MissingFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var downloads = completion.Downloads
                .Where(file => missingSet.Contains(file.LocalPath))
                .DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (downloads.Count == 0)
            {
                var issue = BuildMissingLocalFilesIssue(
                    "MissingLocalFiles",
                    $"缺少 {completion.MissingFiles.Count} 个本地文件，且未找到可用下载信息",
                    completion);
                SetStep(StepFileCompletion, LaunchStepStatus.Failed, issue.Message);
                return FileCompletionLaunchResult.Failed(issue);
            }

            SetStep(StepFileCompletion, LaunchStepStatus.Running, $"第 {pass} 轮补全：{downloads.Count} 个文件");
            var taskName = "启动补全 " + request.Instance!.Name;
            void HandleDownloadSnapshot(object? sender, DownloadTaskSnapshot snapshot)
            {
                if (!string.Equals(snapshot.Name, taskName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var status = snapshot.State switch
                {
                    DownloadTaskState.Succeeded => LaunchStepStatus.Succeeded,
                    DownloadTaskState.Failed or DownloadTaskState.Canceled => LaunchStepStatus.Failed,
                    _ => LaunchStepStatus.Running
                };
                var percent = Math.Clamp(snapshot.Progress * 100, 0, 100);
                SetStep(
                    StepFileCompletion,
                    status,
                    $"{snapshot.Message}（{snapshot.FinishedFiles}/{snapshot.TotalFiles}，{percent:0}%）");
            }

            _downloadManager.SnapshotChanged += HandleDownloadSnapshot;
            DownloadTaskSnapshot snapshot;
            try
            {
                snapshot = await _downloadManager.DownloadAsync(taskName, downloads, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _downloadManager.SnapshotChanged -= HandleDownloadSnapshot;
            }

            if (snapshot.State != DownloadTaskState.Succeeded)
            {
                var afterFailure = await _fileCompleter.BuildCompletionPlanAsync(request, completion.MissingFiles, cancellationToken).ConfigureAwait(false);
                var issue = BuildFileCompletionFailureIssue(snapshot, afterFailure);
                SetStep(StepFileCompletion, LaunchStepStatus.Failed, issue.Message);
                return FileCompletionLaunchResult.Failed(issue);
            }

            downloadedFiles += downloads.Count;
            currentMissing = completion.MissingFiles;
        }

        var finalCompletion = await _fileCompleter.BuildCompletionPlanAsync(request, currentMissing, cancellationToken).ConfigureAwait(false);
        if (finalCompletion.MissingFiles.Count == 0)
        {
            return FileCompletionLaunchResult.Ok($"已补全 {downloadedFiles} 个文件");
        }

        var finalIssue = BuildMissingLocalFilesIssue(
            "MissingLocalFiles",
            $"已尝试自动补全，但仍缺少 {finalCompletion.MissingFiles.Count} 个本地文件",
            finalCompletion);
        SetStep(StepFileCompletion, LaunchStepStatus.Failed, finalIssue.Message);
        return FileCompletionLaunchResult.Failed(finalIssue);
    }

    private static LaunchValidationIssue BuildFileCompletionFailureIssue(DownloadTaskSnapshot snapshot, LaunchFileCompletionResult completion)
    {
        var suffix = completion.MissingFiles.Count == 0
            ? ""
            : BuildMissingFileSuffix("；仍缺少", completion);
        return new LaunchValidationIssue("FileCompletionFailed", $"{snapshot.Message}{suffix}");
    }

    private static LaunchValidationIssue BuildMissingLocalFilesIssue(string code, string prefix, LaunchFileCompletionResult completion)
    {
        return new LaunchValidationIssue(code, prefix + BuildMissingFileSuffix("；示例", completion));
    }

    private static string BuildMissingFileSuffix(string samplePrefix, LaunchFileCompletionResult completion)
    {
        var source = completion.UnresolvableMissingFiles.Count > 0
            ? completion.UnresolvableMissingFiles
            : completion.MissingFiles;
        var sample = string.Join("、", source
            .Take(3)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name)));
        var unresolvable = completion.UnresolvableMissingFiles.Count > 0
            ? $"；其中 {completion.UnresolvableMissingFiles.Count} 个无法自动补全"
            : "";
        if (source.Count == 0)
        {
            return unresolvable;
        }

        return string.IsNullOrWhiteSpace(sample)
            ? $"{samplePrefix} {source.Count} 个文件{unresolvable}"
            : $"{samplePrefix} {source.Count} 个文件：{sample}{unresolvable}";
    }

    private static LaunchValidationIssue BuildEarlyGameExitIssue(GameProcessWatchResult watchResult)
    {
        var diagnosis = DiagnoseGameExit(watchResult);
        var message = $"游戏进程很快退出，退出码：{watchResult.ExitCode}";
        if (!string.IsNullOrWhiteSpace(diagnosis))
        {
            message += "；" + diagnosis;
        }

        var tail = watchResult.CombinedTail
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(3)
            .ToArray();
        if (tail.Length > 0)
        {
            message += "；最近日志：" + string.Join(" | ", tail);
        }

        return new LaunchValidationIssue("GameExitedEarly", message);
    }

    private static string DiagnoseGameExit(GameProcessWatchResult watchResult)
    {
        var text = string.Join('\n', watchResult.CombinedTail);
        if (string.IsNullOrWhiteSpace(text))
        {
            return "没有捕获到游戏输出，请查看最新日志文件";
        }

        if (text.Contains("Unsupported class file major version", StringComparison.OrdinalIgnoreCase))
        {
            return "Java 版本过新，Forge / Mixin 或部分 Mod 不兼容当前 Java，请切换到该版本推荐的 Java";
        }

        if (text.Contains("UnsupportedClassVersionError", StringComparison.OrdinalIgnoreCase)
            || text.Contains("has been compiled by a more recent version", StringComparison.OrdinalIgnoreCase))
        {
            return "Java 版本过旧，请切换到该 Minecraft 版本要求的 Java";
        }

        if (text.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Java heap space", StringComparison.OrdinalIgnoreCase))
        {
            return "游戏内存不足，请提高最大内存或减少 Mod";
        }

        if (text.Contains("Incompatible mod set", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ModResolutionException", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Missing or unsupported mandatory dependencies", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("requires", StringComparison.OrdinalIgnoreCase)
                && (text.Contains("mod", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("fabric", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("forge", StringComparison.OrdinalIgnoreCase)))
            || (text.Contains("depends", StringComparison.OrdinalIgnoreCase)
                && text.Contains("mod", StringComparison.OrdinalIgnoreCase)))
        {
            return "Mod 前置依赖缺失或版本不匹配，请补齐依赖并确认 Mod、加载器、Minecraft 版本一致";
        }

        if (text.Contains("Mixin apply", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ClassMetadataNotFoundException", StringComparison.OrdinalIgnoreCase)
            || text.Contains("NoClassDefFoundError", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ClassNotFoundException", StringComparison.OrdinalIgnoreCase))
        {
            return "Mod / 加载器依赖可能不匹配，请检查 Mod 版本、前置依赖和加载器版本";
        }

        if (text.Contains("GLFW error 65542", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Pixel format not accelerated", StringComparison.OrdinalIgnoreCase)
            || text.Contains("OpenGL", StringComparison.OrdinalIgnoreCase) && text.Contains("not supported", StringComparison.OrdinalIgnoreCase))
        {
            return "显卡驱动或 OpenGL 支持异常，请更新显卡驱动或切换可用显卡";
        }

        if (text.Contains("AccessDeniedException", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
            || text.Contains("另一个程序正在使用此文件", StringComparison.OrdinalIgnoreCase))
        {
            return "文件被占用或没有访问权限，请关闭占用程序并检查文件夹权限";
        }

        if (text.Contains("UnsatisfiedLinkError", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Failed to load library", StringComparison.OrdinalIgnoreCase)
            || text.Contains("lwjgl", StringComparison.OrdinalIgnoreCase) && text.Contains("native", StringComparison.OrdinalIgnoreCase))
        {
            return "natives 或 LWJGL 运行库加载失败，请尝试补全文件或重新安装该版本";
        }

        if (text.Contains("Could not find or load main class", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("main class", StringComparison.OrdinalIgnoreCase) && text.Contains("not found", StringComparison.OrdinalIgnoreCase)))
        {
            return "启动主类缺失，版本 JSON、客户端 jar 或加载器安装可能不完整";
        }

        return "游戏自行退出，建议查看下方最近日志或完整日志文件";
    }

    private async Task<IReadOnlyList<JavaEntry>> GetJavaCandidatesAsync(LaunchRequest request, MinecraftInstance instance, CancellationToken cancellationToken)
    {
        var candidates = new List<JavaEntry>();
        var preferredJavaPath = JavaEntry.ResolveSettingJavaPath(request.JavaPath);
        if (!string.IsNullOrWhiteSpace(preferredJavaPath))
        {
            var preferred = await _javaDiscovery.InspectJavaAsync(preferredJavaPath, isUserImport: true, cancellationToken).ConfigureAwait(false);
            if (preferred is not null)
            {
                candidates.Add(preferred);
            }
        }

        candidates.AddRange(await _javaDiscovery.DiscoverAsync(request.MinecraftRootPath, ResolveGameDirectory(request), cancellationToken).ConfigureAwait(false));
        return candidates;
    }

    private JavaEntry? SelectJava(LaunchRequest request, MinecraftInstance instance, IReadOnlyList<JavaEntry> candidates)
    {
        var preferredJavaPath = JavaEntry.ResolveSettingJavaPath(request.JavaPath);
        if (ShouldIgnoreJavaCompatibility(request) && !string.IsNullOrWhiteSpace(preferredJavaPath))
        {
            var preferred = candidates.FirstOrDefault(entry => string.Equals(entry.PathJava, preferredJavaPath, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return _javaSelector.SelectBest(instance, candidates, preferredJavaPath);
    }

    private bool ShouldIgnoreJavaCompatibility(LaunchRequest request)
    {
        return _settings is not null
            && request.Instance is not null
            && _settings.Get(GetInstanceSettingKey(request.Instance.Name, AppSettingKeys.VersionAdvanceJava), false);
    }

    private bool ShouldDisableFileCheck(LaunchRequest request)
    {
        return _settings is not null
            && request.Instance is not null
            && _settings.Get(GetInstanceSettingKey(request.Instance.Name, AppSettingKeys.VersionAdvanceAssetsV2), false);
    }

    private static LoginRequest CreateLoginRequest(LaunchRequest request)
    {
        return new LoginRequest(
            request.LoginType,
            request.LegacyName,
            request.LoginUserName,
            request.LoginPassword,
            request.LoginServer,
            request.RememberLogin);
    }

    private ProcessStartInfo CreateStartInfo(LaunchRequest request, JavaEntry java, string arguments)
    {
        var startInfo = new ProcessStartInfo(java.PathJava)
        {
            Arguments = arguments,
            WorkingDirectory = ResolveGameDirectory(request),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var path = startInfo.EnvironmentVariables["Path"] ?? "";
        var javaFolder = java.PathFolder;
        if (!path.Split(Path.PathSeparator).Contains(javaFolder, StringComparer.OrdinalIgnoreCase))
        {
            startInfo.EnvironmentVariables["Path"] = string.IsNullOrWhiteSpace(path)
                ? javaFolder
                : path + Path.PathSeparator + javaFolder;
        }

        startInfo.EnvironmentVariables["appdata"] = request.MinecraftRootPath;
        return startInfo;
    }

    private List<LaunchValidationIssue> ValidateRequest(LaunchRequest request)
    {
        var issues = new List<LaunchValidationIssue>();
        if (request.Instance is null)
        {
            issues.Add(new LaunchValidationIssue("InstanceMissing", "未选择 Minecraft 实例"));
            return issues;
        }

        if (request.Instance.State != MinecraftInstanceState.Ready)
        {
            issues.Add(new LaunchValidationIssue("InstanceInvalid", $"实例状态异常：{request.Instance.State}"));
        }

        var gameDirectory = ResolveGameDirectory(request);
        if (gameDirectory.Contains('!') || gameDirectory.Contains(';'))
        {
            issues.Add(new LaunchValidationIssue("InvalidPath", $"游戏路径中不可包含 ! 或 ;：{gameDirectory}"));
        }

        if (string.IsNullOrWhiteSpace(request.Instance.Version.MainClass))
        {
            issues.Add(new LaunchValidationIssue("MainClassMissing", "版本 JSON 缺少 mainClass"));
        }

        return issues;
    }

    private string ResolveGameDirectory(LaunchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.GameDirectory))
        {
            return Path.GetFullPath(request.GameDirectory);
        }

        if (request.Instance is null)
        {
            return Path.GetFullPath(request.MinecraftRootPath);
        }

        return _gameDirectories?.Resolve(request).Path ?? request.Instance.VersionPath;
    }

    private bool ShouldOptimizeMemory(LaunchRequest request)
    {
        if (_settings is null || request.Instance is null)
        {
            return false;
        }

        var instanceMode = _settings.Get(GetInstanceSettingKey(request.Instance.Name, AppSettingKeys.VersionRamOptimize), 0);
        return instanceMode switch
        {
            1 => true,
            2 => false,
            _ => _settings.Get(AppSettingKeys.LaunchArgumentRam, false)
        };
    }

    private static string GetInstanceSettingKey(string instanceName, string key)
    {
        return $"Instance.{instanceName}.{key}";
    }

    private void ResetSteps()
    {
        lock (_stepsSync)
        {
            _steps.Clear();
            _steps.AddRange([
                new LaunchStepState("预检测", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("获取 Java", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("登录", LaunchStepStatus.Waiting, ""),
                new LaunchStepState(StepFileCompletion, LaunchStepStatus.Waiting, ""),
                new LaunchStepState("本地缺失检查", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("获取启动参数", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("内存优化", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("解压文件", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("预启动处理", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("准备补丁文件", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("执行自定义命令", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("启动进程", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("等待游戏窗口", LaunchStepStatus.Waiting, ""),
                new LaunchStepState("结束处理", LaunchStepStatus.Waiting, "")
            ]);
        }

        NotifyStepsChanged();
    }

    private void SetStep(string name, LaunchStepStatus status, string message)
    {
        lock (_stepsSync)
        {
            var index = _steps.FindIndex(step => step.Name == name);
            if (index < 0)
            {
                return;
            }

            _steps[index] = new LaunchStepState(name, status, message);
        }

        NotifyStepsChanged();
    }

    private void NotifyStepsChanged()
    {
        LaunchStepState[] snapshot;
        lock (_stepsSync)
        {
            snapshot = _steps.ToArray();
        }

        var handlers = StepsChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<IReadOnlyList<LaunchStepState>> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "刷新启动步骤订阅者失败");
            }
        }
    }

    private sealed record FileCompletionLaunchResult(bool Success, IReadOnlyList<LaunchValidationIssue> Issues, string Message)
    {
        public static FileCompletionLaunchResult Ok(string message)
        {
            return new FileCompletionLaunchResult(true, [], message);
        }

        public static FileCompletionLaunchResult Failed(params LaunchValidationIssue[] issues)
        {
            return new FileCompletionLaunchResult(false, issues, issues.FirstOrDefault()?.Message ?? "文件补全失败");
        }
    }
}
