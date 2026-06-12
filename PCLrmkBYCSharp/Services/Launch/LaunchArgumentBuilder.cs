using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed partial class LaunchArgumentBuilder(
    IAppSettingsService? settings = null,
    IMinecraftGameDirectoryService? gameDirectories = null,
    ISystemMemoryService? systemMemory = null) : ILaunchArgumentBuilder
{
    public LaunchArgumentBuildResult Build(LaunchRequest request, JavaEntry java, LoginSession login)
    {
        if (request.Instance is null)
        {
            throw new InvalidOperationException("未选择 Minecraft 实例");
        }

        var roots = LoadVersionChain(request.Instance);
        var root = roots[0];
        var missingFiles = new List<string>();
        var nativesDirectory = GetNativesDirectory(request.Instance);
        var classpath = BuildClasspath(request.Instance, roots, missingFiles);
        var replacements = BuildReplacements(request, java, login, nativesDirectory, classpath, roots);
        var args = new List<string>();

        args.AddRange(BuildJvmArguments(request, java, login, roots));
        args.Add(GetString(root, "mainClass"));
        args.AddRange(BuildGameArguments(request, roots));

        var replaced = DeduplicateArguments(args)
            .Where(arg => !string.IsNullOrWhiteSpace(arg))
            .Select(arg => ReplaceTokens(arg, replacements))
            .ToList();

        var cleaned = RemoveEmptyValueOptions(replaced)
            .Where(arg => !string.IsNullOrWhiteSpace(arg))
            .ToList();

        var argumentString = string.Join(" ", cleaned.Select(QuoteArgument));
        var sanitized = Sanitize($"\"{java.PathJava}\" {argumentString}");
        return new LaunchArgumentBuildResult(argumentString, sanitized, missingFiles);
    }

    private List<JsonElement> LoadVersionChain(MinecraftInstance instance)
    {
        var result = new List<JsonElement>();
        var current = instance;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            if (!visited.Add(current.Name))
            {
                break;
            }

            var document = JsonDocument.Parse(File.ReadAllText(current.JsonPath));
            result.Add(document.RootElement.Clone());
            var inheritsFrom = GetString(document.RootElement, "inheritsFrom");
            if (string.IsNullOrWhiteSpace(inheritsFrom))
            {
                break;
            }

            var inheritedPath = Path.Combine(instance.RootPath, "versions", inheritsFrom, inheritsFrom + ".json");
            if (!File.Exists(inheritedPath))
            {
                break;
            }

            current = new MinecraftDiscoveryService().InspectInstance(
                instance.RootPath,
                Path.GetDirectoryName(inheritedPath)!,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { inheritsFrom });
        }

        return result;
    }

    private IEnumerable<string> BuildJvmArguments(LaunchRequest request, JavaEntry java, LoginSession login, IReadOnlyList<JsonElement> roots)
    {
        var args = new List<string>();
        var features = LaunchRuleFeatures.FromRequest(request);
        var hasModernJvm = roots.Any(root => root.TryGetProperty("arguments", out var arguments) && arguments.TryGetProperty("jvm", out _));
        if (hasModernJvm)
        {
            foreach (var root in roots)
            {
                if (root.TryGetProperty("arguments", out var arguments)
                    && arguments.TryGetProperty("jvm", out var jvm)
                    && jvm.ValueKind == JsonValueKind.Array)
                {
                    args.AddRange(ReadArgumentArray(jvm, features));
                }
            }
        }
        else
        {
            args.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
            args.Add("-Djava.library.path=${natives_directory}");
            args.Add("-cp");
            args.Add("${classpath}");
        }

        args.AddRange(BuildLoggingArguments(request.Instance!.RootPath, roots));
        var customJvmArgs = GetInstanceSetting(request.Instance!.Name, AppSettingKeys.VersionAdvanceJvm, SettingsGet(AppSettingKeys.LaunchAdvanceJvm, ""));
        var combinedCustomJvmArgs = string.Join(" ", [customJvmArgs, request.ExtraJvmArgs]);
        args.AddRange(SplitArguments(customJvmArgs));
        args.AddRange(SplitArguments(request.ExtraJvmArgs));
        if (login.Type == LoginType.Nide)
        {
            args.Add("-javaagent:\"${pure_directory}\\nide8auth.jar\"=\"" + request.LoginServer + "\"");
        }
        else if (login.Type == LoginType.Auth)
        {
            args.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT");
            args.Add("-javaagent:\"${pure_directory}\\authlib-injector.jar\"=\"" + request.LoginServer + "\"");
            args.Add("-Dauthlibinjector.side=client");
            if (!string.IsNullOrWhiteSpace(login.AuthlibInjectorMetadata))
            {
                args.Add("-Dauthlibinjector.yggdrasil.prefetched=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(login.AuthlibInjectorMetadata)));
            }
        }

        var useJlw = ShouldUseJavaLaunchWrapper(request, java, combinedCustomJvmArgs);
        if (useJlw)
        {
            if (java.MajorVersion >= 9)
            {
                args.Add("--add-exports");
                args.Add("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED");
            }

            args.Add("-Doolloo.jlw.tmpdir=${pure_directory}");
        }

        if (ShouldUseLwjglUnsafeAgent(request, roots))
        {
            args.Add("-javaagent:\"${pure_directory}\\LUA.jar\"");
        }

        args.Add($"-Xmx{GetMemoryMb(request, java)}m");
        args.AddRange(BuildGcArguments(request, java));
        args.Add("-Dlog4j2.formatMsgNoLookups=true");
        if (java.MajorVersion > 8)
        {
            args.Add("-Dstdout.encoding=UTF-8");
            args.Add("-Dstderr.encoding=UTF-8");
        }

        if (java.MajorVersion >= 18)
        {
            args.Add("-Dfile.encoding=COMPAT");
        }

        if (useJlw)
        {
            args.Add("-jar");
            args.Add("${pure_directory}\\JavaWrapper.jar");
        }

        return DeduplicateArguments(args);
    }

    private static IEnumerable<string> BuildLoggingArguments(string rootPath, IReadOnlyList<JsonElement> roots)
    {
        foreach (var root in roots)
        {
            if (!root.TryGetProperty("logging", out var logging)
                || !logging.TryGetProperty("client", out var client)
                || !client.TryGetProperty("file", out var file)
                || file.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = GetString(file, "id");
            var argument = GetString(client, "argument");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            var path = Path.Combine(rootPath, "assets", "log_configs", id);
            yield return argument.Replace("${path}", path, StringComparison.Ordinal);
            yield break;
        }
    }

    private IEnumerable<string> BuildGameArguments(LaunchRequest request, IReadOnlyList<JsonElement> roots)
    {
        var args = new List<string>();
        var features = LaunchRuleFeatures.FromRequest(request);
        foreach (var root in roots)
        {
            if (root.TryGetProperty("minecraftArguments", out var legacy) && legacy.ValueKind == JsonValueKind.String)
            {
                args.AddRange(SplitArguments(legacy.GetString() ?? ""));
            }

            if (root.TryGetProperty("arguments", out var arguments)
                && arguments.TryGetProperty("game", out var game)
                && game.ValueKind == JsonValueKind.Array)
            {
                args.AddRange(ReadArgumentArray(game, features));
            }
        }

        if (args.Count == 0)
        {
            args.AddRange([
                "--username", "${auth_player_name}",
                "--version", "${version_name}",
                "--gameDir", "${game_directory}",
                "--assetsDir", "${assets_root}",
                "--assetIndex", "${assets_index_name}",
                "--uuid", "${auth_uuid}",
                "--accessToken", "${auth_access_token}",
                "--userType", "${user_type}",
                "--versionType", "${version_type}"
            ]);
        }

        args.AddRange(SplitArguments(GetInstanceSetting(request.Instance!.Name, AppSettingKeys.VersionAdvanceGame, SettingsGet(AppSettingKeys.LaunchAdvanceGame, ""))));
        args.AddRange(SplitArguments(request.ExtraGameArgs));
        if (request.WindowType == 0)
        {
            AddOptionIfMissing(args, "--fullscreen");
        }
        else
        {
            var windowSize = ResolveWindowSize(request);
            if (windowSize.Width > 0
            && windowSize.Height > 0
            && !ContainsOption(args, "--width")
            && !ContainsOption(args, "--height"))
            {
                args.Add("--width");
                args.Add("${resolution_width}");
                args.Add("--height");
                args.Add("${resolution_height}");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.ServerIp))
        {
            if (request.ServerIp.Contains(':', StringComparison.Ordinal))
            {
                var parts = request.ServerIp.Split(':', 2);
                args.Add("--server");
                args.Add(parts[0]);
                args.Add("--port");
                args.Add(parts[1]);
            }
            else
            {
                args.Add("--server");
                args.Add(request.ServerIp);
                args.Add("--port");
                args.Add("25565");
            }
        }

        return DeduplicateArguments(FixOptiFineTweaker(args));
    }

    private bool ShouldUseJavaLaunchWrapper(LaunchRequest request, JavaEntry java, string customJvmArgs)
    {
        if (SettingsGet(AppSettingKeys.LaunchAdvanceDisableJLW, false)
            || GetInstanceSettingOnly(request.Instance!.Name, AppSettingKeys.VersionAdvanceDisableJLW, false)
            || IsGbkSystemEncoding()
            || IsAsciiOnly(request.MinecraftRootPath)
            || customJvmArgs.Contains("-javaagent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return java.MajorVersion >= 8;
    }

    private bool ShouldUseLwjglUnsafeAgent(LaunchRequest request, IReadOnlyList<JsonElement> roots)
    {
        if (SettingsGet(AppSettingKeys.LaunchAdvanceDisableLUA, false)
            || GetInstanceSettingOnly(request.Instance!.Name, AppSettingKeys.VersionAdvanceDisableLUA, false))
        {
            return false;
        }

        return roots.Any(root =>
            root.TryGetProperty("libraries", out var libraries)
            && libraries.ValueKind == JsonValueKind.Array
            && libraries.EnumerateArray().Any(library =>
                CheckLibraryRules(library)
                && string.Equals(GetString(library, "name"), "org.lwjgl:lwjgl:3.4.1", StringComparison.OrdinalIgnoreCase)));
    }

    private static void AddOptionIfMissing(List<string> args, string option)
    {
        if (!ContainsOption(args, option))
        {
            args.Add(option);
        }
    }

    private static bool ContainsOption(IEnumerable<string> args, string option)
    {
        return args.Any(arg => string.Equals(arg, option, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ReadArgumentArray(JsonElement array, LaunchRuleFeatures features)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                yield return item.GetString() ?? "";
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("rules", out var rules) && !CheckRules(rules, features))
                {
                    continue;
                }

                if (!item.TryGetProperty("value", out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    yield return value.GetString() ?? "";
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var nested in value.EnumerateArray())
                    {
                        if (nested.ValueKind == JsonValueKind.String)
                        {
                            yield return nested.GetString() ?? "";
                        }
                    }
                }
            }
        }
    }

    private static bool CheckRules(JsonElement rules)
    {
        return CheckRules(rules, LaunchRuleFeatures.Empty);
    }

    private static bool CheckRules(JsonElement rules, LaunchRuleFeatures features)
    {
        if (rules.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = GetString(rule, "action");
            var applies = true;
            if (rule.TryGetProperty("os", out var os))
            {
                applies = OsRuleMatches(os);
            }

            if (applies && rule.TryGetProperty("features", out var featureRules))
            {
                applies = features.Matches(featureRules);
            }

            if (applies)
            {
                allowed = string.Equals(action, "allow", StringComparison.OrdinalIgnoreCase);
            }
        }

        return allowed;
    }

    private static bool OsRuleMatches(JsonElement os)
    {
        if (os.TryGetProperty("name", out var name)
            && !string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (os.TryGetProperty("arch", out var arch) && arch.ValueKind == JsonValueKind.String)
        {
            var expected = arch.GetString() ?? "";
            if (!CurrentArchitectureAliases().Contains(expected, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var versionPattern = GetString(os, "version");
        if (!string.IsNullOrWhiteSpace(versionPattern) && !OsVersionMatches(versionPattern))
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> CurrentArchitectureAliases()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => ["x64", "amd64"],
            Architecture.X86 => ["x86", "i386"],
            Architecture.Arm64 => ["arm64", "aarch64"],
            Architecture.Arm => ["arm"],
            _ => [RuntimeInformation.OSArchitecture.ToString()]
        };
    }

    private static bool OsVersionMatches(string pattern)
    {
        try
        {
            var version = Environment.OSVersion.Version.ToString();
            var versionString = Environment.OSVersion.VersionString;
            return Regex.IsMatch(version, pattern, RegexOptions.IgnoreCase)
                || Regex.IsMatch(versionString, pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> BuildClasspath(MinecraftInstance instance, IReadOnlyList<JsonElement> roots, List<string> missingFiles)
    {
        var classpath = new List<string>();
        foreach (var root in roots.Reverse())
        {
            if (!root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var library in libraries.EnumerateArray())
            {
                if (!CheckLibraryRules(library))
                {
                    continue;
                }

                var libraryPath = GetLibraryPath(instance.RootPath, library);
                if (string.IsNullOrWhiteSpace(libraryPath) || classpath.Contains(libraryPath, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                classpath.Add(libraryPath);
                if (!File.Exists(libraryPath))
                {
                    missingFiles.Add(libraryPath);
                }
            }
        }

        var jarPath = Path.Combine(instance.VersionPath, $"{instance.Name}.jar");
        classpath.Add(jarPath);
        if (!File.Exists(jarPath))
        {
            missingFiles.Add(jarPath);
        }

        return classpath;
    }

    private static bool CheckLibraryRules(JsonElement library)
    {
        return !library.TryGetProperty("rules", out var rules) || CheckRules(rules);
    }

    private static string GetLibraryPath(string rootPath, JsonElement library)
    {
        if (library.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        if (library.TryGetProperty("downloads", out var downloads)
            && downloads.TryGetProperty("artifact", out var artifact)
            && artifact.TryGetProperty("path", out var path)
            && path.ValueKind == JsonValueKind.String)
        {
            return Path.Combine(rootPath, "libraries", path.GetString()!.Replace('/', Path.DirectorySeparatorChar));
        }

        if (library.TryGetProperty("natives", out _))
        {
            return "";
        }

        var name = GetString(library, "name");
        var parts = name.Split(':');
        if (parts.Length < 3)
        {
            return "";
        }

        var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifactName = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";
        return Path.Combine(rootPath, "libraries", group, artifactName, version, $"{artifactName}-{version}{classifier}.jar");
    }

    private Dictionary<string, string> BuildReplacements(
        LaunchRequest request,
        JavaEntry java,
        LoginSession login,
        string nativesDirectory,
        IReadOnlyList<string> classpath,
        IReadOnlyList<JsonElement> roots)
    {
        var instance = request.Instance!;
        var gameDirectory = ResolveGameDirectory(request);
        var versionType = GetInstanceSettingOnly(instance.Name, AppSettingKeys.VersionArgumentInfo, "");
        if (string.IsNullOrWhiteSpace(versionType))
        {
            versionType = string.IsNullOrWhiteSpace(instance.CustomInfo)
                ? SettingsGet(AppSettingKeys.LaunchArgumentInfo, "PCL")
                : instance.CustomInfo;
        }
        var root = roots[0];
        var windowSize = ResolveWindowSize(request);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${natives_directory}"] = nativesDirectory,
            ["${library_directory}"] = Path.Combine(instance.RootPath, "libraries"),
            ["${libraries_directory}"] = Path.Combine(instance.RootPath, "libraries"),
            ["${pure_directory}"] = instance.VersionPath.TrimEnd(Path.DirectorySeparatorChar),
            ["${launcher_name}"] = "PCL",
            ["${launcher_version}"] = "0",
            ["${classpath_separator}"] = ";",
            ["${version_name}"] = instance.Name,
            ["${game_directory}"] = gameDirectory.TrimEnd(Path.DirectorySeparatorChar),
            ["${assets_root}"] = Path.Combine(instance.RootPath, "assets"),
            ["${assets_index_name}"] = GetAssetsIndex(roots),
            ["${game_assets}"] = Path.Combine(instance.RootPath, "assets", "virtual", "legacy"),
            ["${primary_jar}"] = Path.Combine(instance.VersionPath, instance.Name + ".jar"),
            ["${auth_player_name}"] = login.UserName,
            ["${auth_uuid}"] = login.Uuid,
            ["${auth_access_token}"] = login.AccessToken,
            ["${access_token}"] = login.AccessToken,
            ["${auth_session}"] = login.AccessToken,
            ["${clientid}"] = login.ClientToken,
            ["${auth_xuid}"] = "",
            ["${user_type}"] = "msa",
            ["${version_type}"] = versionType,
            ["${resolution_width}"] = windowSize.Width.ToString(),
            ["${resolution_height}"] = windowSize.Height.ToString(),
            ["${classpath}"] = string.Join(";", classpath)
        };
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

        return gameDirectories?.Resolve(request).Path ?? request.Instance.VersionPath;
    }

    private static (int Width, int Height) ResolveWindowSize(LaunchRequest request)
    {
        return request.WindowType switch
        {
            2 or 3 => (Math.Max(100, request.WindowWidth), Math.Max(100, request.WindowHeight)),
            _ => (854, 480)
        };
    }

    private int GetMemoryMb(LaunchRequest request, JavaEntry java)
    {
        var memoryMb = GetGlobalMemoryMb(request);
        if (settings is not null && request.Instance is not null)
        {
            var instanceMode = settings.Get($"Instance.{request.Instance.Name}.{AppSettingKeys.VersionRamType}", 2);
            memoryMb = instanceMode switch
            {
                1 => ConvertLegacyRamSliderToMb(settings.Get($"Instance.{request.Instance.Name}.{AppSettingKeys.VersionRamCustom}", 15)),
                0 => GetAutoMemoryMb(request),
                _ => memoryMb
            };
        }

        if (!java.Is64Bit)
        {
            memoryMb = Math.Min(memoryMb, 1024);
        }

        return Math.Max(512, memoryMb);
    }

    private int GetGlobalMemoryMb(LaunchRequest request)
    {
        var memoryMb = GetAutoMemoryMb(request);
        if (settings is not null && SettingsGet(AppSettingKeys.LaunchRamType, 0) == 1)
        {
            memoryMb = ConvertLegacyRamSliderToMb(SettingsGet(AppSettingKeys.LaunchRamCustom, 15));
        }

        return memoryMb;
    }

    private int GetAutoMemoryMb(LaunchRequest request)
    {
        var availableGb = Math.Round(GetAvailableMemoryBytes() / 1024d / 1024d / 1024d * 10d) / 10d;
        var modCount = CountLocalMods(request);
        var isModable = request.Instance?.Version.HasForge == true
            || request.Instance?.Version.HasFabric == true
            || request.Instance?.Version.HasNeoForge == true;

        double minimum;
        double target1;
        double target2;
        double target3;
        if (isModable)
        {
            minimum = 0.5d + modCount / 150d;
            target1 = 1.5d + modCount / 90d;
            target2 = 2.7d + modCount / 50d;
            target3 = 4.5d + modCount / 25d;
        }
        else if (request.Instance?.Version.HasOptiFine == true)
        {
            minimum = 0.5d;
            target1 = 1.5d;
            target2 = 3d;
            target3 = 5d;
        }
        else
        {
            minimum = 0.5d;
            target1 = 1.5d;
            target2 = 2.5d;
            target3 = 4d;
        }

        var memoryGb = AllocateAutoMemoryGb(availableGb, minimum, target1, target2, target3);
        return (int)Math.Floor(memoryGb * 1024d);
    }

    private long GetAvailableMemoryBytes()
    {
        return systemMemory?.AvailablePhysicalMemoryBytes ?? new SystemMemoryService().AvailablePhysicalMemoryBytes;
    }

    private int CountLocalMods(LaunchRequest request)
    {
        if (request.Instance is null)
        {
            return 0;
        }

        var modsDirectory = Path.Combine(ResolveGameDirectory(request), "mods");
        if (!Directory.Exists(modsDirectory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(modsDirectory)
            .Count(path =>
            {
                var extension = Path.GetExtension(path);
                return extension.Equals(".jar", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".litemod", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static double AllocateAutoMemoryGb(double availableGb, double minimum, double target1, double target2, double target3)
    {
        var memoryGb = 0d;

        var delta = target1;
        memoryGb += Math.Min(availableGb, delta);
        availableGb -= delta;
        if (availableGb < 0.1d)
        {
            return Math.Round(Math.Max(memoryGb, minimum), 1);
        }

        delta = target2 - target1;
        memoryGb += Math.Min(availableGb * 0.7d, delta);
        availableGb -= delta / 0.7d;
        if (availableGb < 0.1d)
        {
            return Math.Round(Math.Max(memoryGb, minimum), 1);
        }

        delta = target3 - target2;
        memoryGb += Math.Min(availableGb * 0.4d, delta);
        availableGb -= delta / 0.4d;
        if (availableGb < 0.1d)
        {
            return Math.Round(Math.Max(memoryGb, minimum), 1);
        }

        delta = target3;
        memoryGb += Math.Min(availableGb * 0.15d, delta);
        return Math.Round(Math.Max(memoryGb, minimum), 1);
    }

    public static int ConvertLegacyRamSliderToMb(int value)
    {
        var clamped = Math.Clamp(value, 0, 49);
        double gb = clamped switch
        {
            <= 12 => clamped * 0.1 + 0.3,
            <= 25 => (clamped - 12) * 0.5 + 1.5,
            <= 33 => (clamped - 25) + 8,
            _ => (clamped - 33) * 2 + 16
        };
        return (int)Math.Round(gb * 1024, MidpointRounding.AwayFromZero);
    }

    private IEnumerable<string> BuildGcArguments(LaunchRequest request, JavaEntry java)
    {
        var setupType = GetInstanceGcSetting(request.Instance!.Name);
        if (setupType == 3)
        {
            return [];
        }

        if (java.MajorVersion >= 15 && setupType is 0 or 1)
        {
            return java.MajorVersion is 21 or 22
                ? ["-XX:+UseZGC", "-XX:+ZGenerational"]
                : ["-XX:+UseZGC"];
        }

        return [
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+UseG1GC",
            "-XX:G1NewSizePercent=20",
            "-XX:G1ReservePercent=20",
            "-XX:G1HeapRegionSize=32M",
            "-XX:MaxGCPauseMillis=50"
        ];
    }

    private int GetInstanceGcSetting(string instanceName)
    {
        var global = SettingsGet(AppSettingKeys.LaunchAdvanceGC, 4);
        if (settings is null)
        {
            return global;
        }

        var instanceKey = $"Instance.{instanceName}.{AppSettingKeys.VersionAdvanceGC}";
        if (!settings.HasSaved(instanceKey))
        {
            return global;
        }

        var instanceValue = settings.Get(instanceKey, 0);
        return instanceValue == 0 ? global : instanceValue - 1;
    }

    private string GetInstanceSetting(string instanceName, string key, string defaultValue)
    {
        if (settings is null)
        {
            return defaultValue;
        }

        return settings.Get($"Instance.{instanceName}.{key}", settings.Get(key, defaultValue));
    }

    private T GetInstanceSettingOnly<T>(string instanceName, string key, T defaultValue)
    {
        return settings is null
            ? defaultValue
            : settings.Get($"Instance.{instanceName}.{key}", defaultValue);
    }

    private T SettingsGet<T>(string key, T defaultValue)
    {
        return settings is null ? defaultValue : settings.Get(key, defaultValue);
    }

    private static string GetAssetsIndex(JsonElement root)
    {
        if (root.TryGetProperty("assetIndex", out var index) && index.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
        {
            return id.GetString() ?? "";
        }

        return GetString(root, "assets");
    }

    private static string GetAssetsIndex(IReadOnlyList<JsonElement> roots)
    {
        foreach (var root in roots)
        {
            var index = GetAssetsIndex(root);
            if (!string.IsNullOrWhiteSpace(index))
            {
                return index;
            }
        }

        return "";
    }

    private static string GetNativesDirectory(MinecraftInstance instance)
    {
        return Path.Combine(instance.VersionPath, $"{instance.Name}-natives");
    }

    private static string ReplaceTokens(string value, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var replacement in replacements)
        {
            value = value.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
        }

        return value;
    }

    private static bool IsAsciiOnly(string value)
    {
        return value.All(ch => ch <= 0x7F);
    }

    private static bool IsGbkSystemEncoding()
    {
        try
        {
            return CultureInfo.CurrentCulture.TextInfo.ANSICodePage == 936;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> SplitArguments(string arguments)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < arguments.Length; i++)
        {
            var ch = arguments[i];
            if (ch == '"' && (i == 0 || arguments[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static List<string> DeduplicateArguments(IEnumerable<string> args)
    {
        var result = new List<string>();
        var seenSingle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args.Where(arg => !string.IsNullOrWhiteSpace(arg)))
        {
            if (arg.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase))
            {
                result.RemoveAll(item => item.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase));
            }

            if (arg.StartsWith("-D", StringComparison.Ordinal) || arg.StartsWith("-XX:", StringComparison.Ordinal))
            {
                var key = arg.Split('=', 2)[0];
                result.RemoveAll(item => item.StartsWith(key, StringComparison.OrdinalIgnoreCase));
            }

            if (!arg.Contains('=') && (arg.StartsWith("-D", StringComparison.Ordinal) || arg.StartsWith("-XX:", StringComparison.Ordinal)))
            {
                if (!seenSingle.Add(arg))
                {
                    continue;
                }
            }

            result.Add(arg);
        }

        return result;
    }

    private static IEnumerable<string> RemoveEmptyValueOptions(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var current = args[i];
            if (OptionCanLoseEmptyValue(current) && i + 1 < args.Count && string.IsNullOrWhiteSpace(args[i + 1]))
            {
                i++;
                continue;
            }

            yield return current;
        }
    }

    private static bool OptionCanLoseEmptyValue(string option)
    {
        return string.Equals(option, "--versionType", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FixOptiFineTweaker(IEnumerable<string> args)
    {
        return args.Select(arg => string.Equals(arg, "optifine.OptiFineTweaker", StringComparison.Ordinal)
            ? "optifine.OptiFineForgeTweaker"
            : arg);
    }

    private static string QuoteArgument(string argument)
    {
        return argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
    }

    private static string Sanitize(string arguments)
    {
        return AccessTokenRegex().Replace(arguments, "$1***");
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private sealed record LaunchRuleFeatures(
        bool HasCustomResolution,
        bool IsDemoUser,
        bool HasQuickPlaysSupport,
        bool IsQuickPlaySingleplayer,
        bool IsQuickPlayMultiplayer,
        bool IsQuickPlayRealms)
    {
        public static LaunchRuleFeatures Empty { get; } = new(false, false, false, false, false, false);

        public static LaunchRuleFeatures FromRequest(LaunchRequest request)
        {
            return new LaunchRuleFeatures(
                request.WindowType != 0 && request.WindowWidth > 0 && request.WindowHeight > 0,
                false,
                false,
                false,
                false,
                false);
        }

        public bool Matches(JsonElement featureRules)
        {
            if (featureRules.ValueKind != JsonValueKind.Object)
            {
                return true;
            }

            foreach (var feature in featureRules.EnumerateObject())
            {
                if (feature.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    continue;
                }

                if (GetFeatureValue(feature.Name) != feature.Value.GetBoolean())
                {
                    return false;
                }
            }

            return true;
        }

        private bool GetFeatureValue(string name)
        {
            return name switch
            {
                "has_custom_resolution" => HasCustomResolution,
                "is_demo_user" => IsDemoUser,
                "has_quick_plays_support" => HasQuickPlaysSupport,
                "is_quick_play_singleplayer" => IsQuickPlaySingleplayer,
                "is_quick_play_multiplayer" => IsQuickPlayMultiplayer,
                "is_quick_play_realms" => IsQuickPlayRealms,
                _ => false
            };
        }
    }

    [GeneratedRegex(@"(--(?:accessToken|access_token|auth_access_token)\s+)(""[^""]*""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenRegex();
}
