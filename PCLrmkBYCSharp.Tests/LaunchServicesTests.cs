using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.Tests;

public sealed class LaunchServicesTests
{
    [Fact]
    public async Task LaunchPipelineReadsLatestLogWhenGameExitsEarlyWithoutConsoleOutput()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var logsDirectory = Path.Combine(instance.VersionPath, "logs");
        Directory.CreateDirectory(logsDirectory);
        File.WriteAllLines(Path.Combine(logsDirectory, "latest.log"), [
            "[main/INFO]: Loading Minecraft",
            "net.fabricmc.loader.impl.FormattedException: Incompatible mod set! Mod sodium requires fabric-api"
        ]);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var watcher = new FakeGameProcessWatcher(GameProcessWatchResult.Exited(1, [], []));
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, gameProcessWatcher: watcher);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        var issue = Assert.Single(result.Issues, issue => issue.Code == "GameExitedEarly");
        Assert.False(result.Success);
        Assert.Contains("latest.log:", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Incompatible mod set", issue.Message, StringComparison.Ordinal);
        Assert.Contains("fabric-api", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchPipelineReadsLatestCrashReportWhenGameExitsEarlyWithoutConsoleOutput()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var crashReports = Path.Combine(instance.VersionPath, "crash-reports");
        Directory.CreateDirectory(crashReports);
        var oldReport = Path.Combine(crashReports, "crash-2026-01-01_00.00.00-client.txt");
        var newReport = Path.Combine(crashReports, "crash-2026-01-02_00.00.00-client.txt");
        File.WriteAllLines(oldReport, ["java.lang.OutOfMemoryError: stale report"]);
        File.WriteAllLines(newReport, [
            "---- Minecraft Crash Report ----",
            "Description: Initializing game",
            "java.lang.OutOfMemoryError: Java heap space"
        ]);
        File.SetLastWriteTimeUtc(oldReport, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(newReport, DateTime.UtcNow);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var watcher = new FakeGameProcessWatcher(GameProcessWatchResult.Exited(1, [], []));
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, gameProcessWatcher: watcher);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        var issue = Assert.Single(result.Issues, issue => issue.Code == "GameExitedEarly");
        Assert.False(result.Success);
        Assert.Contains("crash-report:", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Java heap space", issue.Message, StringComparison.Ordinal);
        Assert.Contains("游戏内存不足", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("stale report", issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("java version \"1.8.0_321\"\r\nJava(TM) 64-Bit Server VM", 8, true)]
    [InlineData("openjdk version \"17.0.8\" 2023-07-18\r\nOpenJDK 64-Bit Server VM", 17, true)]
    [InlineData("openjdk version \"21.0.2\" 2024-01-16\r\nOpenJDK Client VM", 21, false)]
    public void JavaVersionParserReadsCommonVersionFormats(string output, int majorVersion, bool is64Bit)
    {
        var parsed = JavaDiscoveryService.ParseVersionOutput(output);

        Assert.Equal(majorVersion, parsed.Version.Minor);
        Assert.Equal(is64Bit, parsed.Is64Bit);
    }

    [Theory]
    [InlineData("1.20.5", 21)]
    [InlineData("1.20.1", 17)]
    [InlineData("1.18.2", 17)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.12.2", 8)]
    public void JavaSelectorComputesMinimumJavaVersion(string minecraftVersion, int expectedMajor)
    {
        var selector = new JavaSelectorService();
        var instance = CreateInstance(minecraftVersion);

        var requirement = selector.GetRequirement(instance);

        Assert.Equal(expectedMajor, requirement.MinVersion.Minor);
    }

    [Theory]
    [InlineData("1.20.5", "Java 21")]
    [InlineData("1.20.1", "Java 17")]
    [InlineData("1.12.2", "Java 8")]
    public void JavaRequirementDisplaysUserFriendlyVersionRange(string minecraftVersion, string expectedText)
    {
        var selector = new JavaSelectorService();
        var instance = CreateInstance(minecraftVersion);

        var requirement = selector.GetRequirement(instance);

        Assert.Equal(expectedText, requirement.DisplayText);
    }

    [Fact]
    public void JavaSelectorIgnoresPreferredJavaAboveCompatibleRange()
    {
        var selector = new JavaSelectorService();
        var instance = CreateInstance("1.20.1");
        var java17 = CreateJava("C:\\Java17\\bin\\java.exe", 17);
        var java21 = CreateJava("C:\\Java21\\bin\\java.exe", 21);

        var selected = selector.SelectBest(instance, [java17, java21], java21.PathJava);

        Assert.Equal(java17, selected);
    }

    [Fact]
    public void JavaSelectorPrefersStableMinimumCompatibleJavaWhenNoManualSelection()
    {
        var selector = new JavaSelectorService();
        var instance = CreateInstance("1.20.1");
        var java17 = CreateJava("C:\\Java17\\bin\\java.exe", 17);
        var java21 = CreateJava("C:\\Java21\\bin\\java.exe", 21);
        var java25 = new JavaEntry("C:\\Java25\\bin\\java.exe", new Version(25, 0, 2), false, true, false, true);

        var selected = selector.SelectBest(instance, [java25, java21, java17], preferredJavaPath: null);

        Assert.Equal(java17, selected);
        Assert.Equal(25, java25.MajorVersion);
        Assert.Equal("JDK 25 (25.0.2)", java25.DisplayName);
    }

    [Fact]
    public void JavaSelectorPrefersJava21ForMinecraft1205AndNewer()
    {
        var selector = new JavaSelectorService();
        var instance = CreateInstance("1.20.5");
        var java17 = CreateJava("C:\\Java17\\bin\\java.exe", 17);
        var java21 = CreateJava("C:\\Java21\\bin\\java.exe", 21);
        var java25 = new JavaEntry("C:\\Java25\\bin\\java.exe", new Version(25, 0, 2), false, true, false, true);

        var selected = selector.SelectBest(instance, [java25, java17, java21], preferredJavaPath: null);

        Assert.Equal(java21, selected);
    }

    [Fact]
    public void JavaSelectorRejectsJavaAboveStableRangeForMinecraft1205AndNewer()
    {
        var selector = new JavaSelectorService();
        var instance = CreateInstance("1.20.5");
        var java21 = CreateJava("C:\\Java21\\bin\\java.exe", 21);
        var java25 = new JavaEntry("C:\\Java25\\bin\\java.exe", new Version(25, 0, 2), false, true, false, true);

        var selected = selector.SelectBest(instance, [java25, java21], java25.PathJava);

        Assert.Equal(java21, selected);
    }

    [Fact]
    public void JavaSelectorReadsOldPclJavaSettingJson()
    {
        var selector = new JavaSelectorService();
        var instance = CreateInstance("1.20.1");
        var java17 = CreateJava("C:\\Java17\\bin\\java.exe", 17);
        var java21 = CreateJava("C:\\Java21\\bin\\java.exe", 21);

        var selected = selector.SelectBest(instance, [java17, java21], java21.ToPclSettingJson());

        Assert.Equal(java17, selected);
        Assert.Equal(java21.PathJava, JavaEntry.ResolveSettingJavaPath(java21.ToPclSettingJson()));
    }

    [Fact]
    public void LegacyLoginCreatesStableOfflineUuid()
    {
        var login = new LegacyLoginService();

        var first = login.CreateSession("Steve");
        var second = login.CreateSession("Steve");

        Assert.Equal(LoginType.Legacy, first.Type);
        Assert.Equal(first.Uuid, second.Uuid);
        Assert.Equal("0", first.AccessToken);
    }

    [Theory]
    [InlineData(1, false, "Steve")]
    [InlineData(2, false, "Alex")]
    [InlineData(4, false, "Steve")]
    [InlineData(4, true, "Alex")]
    public void LegacyLoginAdjustsOfflineUuidForSkinModel(int skinType, bool slim, string expectedModel)
    {
        var login = new LegacyLoginService();

        var session = login.CreateSession("Steve", skinType, slim, "");

        Assert.Equal(expectedModel, GetSkinModel(session.Uuid));
    }

    [Fact]
    public async Task LoginServiceUsesLegacySkinSettingsForOfflineUuid()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchSkinType, 4);
        settings.Set(AppSettingKeys.LaunchSkinSlim, true);
        var service = new LoginService(
            new LegacyLoginService(),
            new ThrowingMicrosoftLoginService(),
            new ThrowingYggdrasilLoginService(),
            settings);

        var session = await service.LoginAsync(new LoginRequest(LoginType.Legacy, "Steve", "", "", "", true));

        Assert.Equal("Alex", GetSkinModel(session.Uuid));
        Assert.Equal(LoginType.Legacy, session.Type);
    }

    [Fact]
    public async Task MojangProfileServiceFetchesAndCachesUuid()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var http = new FakeLaunchHttpClient();
        http.Enqueue("""{"id":"0123456789abcdef0123456789abcdef","name":"Notch"}""");
        var service = new MojangProfileService(http, settings, new NullLoggerService());

        var first = await service.GetUuidAsync("Notch");
        var second = await service.GetUuidAsync("Notch");

        Assert.Equal("0123456789abcdef0123456789abcdef", first);
        Assert.Equal(first, second);
        Assert.Equal(1, http.SendCount);
        Assert.Equal("0123456789abcdef0123456789abcdef", settings.Get(AppSettingKeys.CacheMojangUuidPrefix + "Notch", ""));
    }

    [Fact]
    public async Task MojangProfileServiceReturnsNullWhenProfileIsMissing()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var http = new FakeLaunchHttpClient();
        http.EnqueueException(new HttpRequestException("HTTP 404: missing"));
        var service = new MojangProfileService(http, settings, new NullLoggerService());

        var uuid = await service.GetUuidAsync("MissingSkin");

        Assert.Null(uuid);
        Assert.Equal(1, http.SendCount);
    }

    [Fact]
    public async Task LoginServiceUsesMojangUuidForLegacySkinType3()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchSkinType, 3);
        settings.Set(AppSettingKeys.LaunchSkinID, "Notch");
        var service = new LoginService(
            new LegacyLoginService(),
            new ThrowingMicrosoftLoginService(),
            new ThrowingYggdrasilLoginService(),
            settings,
            new FakeMojangProfileService("0123456789abcdef0123456789abcdef"));

        var session = await service.LoginAsync(new LoginRequest(LoginType.Legacy, "Steve", "", "", "", true));

        Assert.Equal("0123456789abcdef0123456789abcdef", session.Uuid);
    }

    [Fact]
    public async Task LoginServiceFallsBackToSkinNameOfflineUuidWhenMojangProfileIsMissing()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchSkinType, 3);
        settings.Set(AppSettingKeys.LaunchSkinID, "Notch");
        var service = new LoginService(
            new LegacyLoginService(),
            new ThrowingMicrosoftLoginService(),
            new ThrowingYggdrasilLoginService(),
            settings,
            new FakeMojangProfileService(null));

        var session = await service.LoginAsync(new LoginRequest(LoginType.Legacy, "Steve", "", "", "", true));

        Assert.Equal(new LegacyLoginService().CreateSession("Notch").Uuid, session.Uuid);
    }

    [Fact]
    public void LegacyLoginRejectsEmptyName()
    {
        var login = new LegacyLoginService();

        Assert.Throws<ArgumentException>(() => login.CreateSession(""));
    }

    [Fact]
    public void LaunchArgumentBuilderReplacesModernArguments()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "arguments": {
            "jvm": ["-Ddemo=${version_name}"],
            "game": ["--username", "${auth_player_name}", "--uuid", "${auth_uuid}", "--accessToken", "${auth_access_token}"]
          },
          "libraries": [{ "name": "org.example:demo:1.0.0" }]
        }
        """);
        var request = CreateRequest(instance, temp.Path);
        var java = CreateJava("C:\\Java17\\bin\\java.exe", 17);
        var login = new LegacyLoginService().CreateSession("Alex");
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(request, java, login);

        Assert.Contains("-Ddemo=1.20.1", result.Arguments);
        Assert.Contains("--username Alex", result.Arguments);
        Assert.Contains("--uuid", result.Arguments);
        Assert.Contains("--accessToken", result.Arguments);
        Assert.Contains("***", result.SanitizedCommandLine);
    }

    [Fact]
    public void LaunchArgumentBuilderAddsVersionLoggingConfigArgument()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.12.2", """
        {
          "id": "1.12.2",
          "type": "release",
          "releaseTime": "2017-09-18T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "minecraftArguments": "--username ${auth_player_name}",
          "logging": {
            "client": {
              "argument": "-Dlog4j.configurationFile=${path}",
              "file": {
                "id": "client-1.12.xml",
                "sha1": "0123456789abcdef0123456789abcdef01234567",
                "size": 877,
                "url": "https://launcher.mojang.com/v1/objects/log/client-1.12.xml"
              },
              "type": "log4j2-xml"
            }
          },
          "libraries": []
        }
        """);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java8\\bin\\java.exe", 8), new LegacyLoginService().CreateSession("Alex"));

        var logConfigPath = Path.Combine(temp.Path, "assets", "log_configs", "client-1.12.xml");
        Assert.Contains("-Dlog4j.configurationFile=" + logConfigPath, result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderSupportsLegacyMinecraftArguments()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.12.2", """
        {
          "id": "1.12.2",
          "type": "release",
          "releaseTime": "2017-09-18T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "minecraftArguments": "--username ${auth_player_name} --gameDir ${game_directory}",
          "libraries": []
        }
        """);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java8\\bin\\java.exe", 8), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("--username Alex", result.Arguments);
        Assert.Contains("--gameDir", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderUsesOldCustomRamSliderWhenEnabled()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": { "game": ["--username", "${auth_player_name}"] },
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchRamType, 1);
        settings.Set(AppSettingKeys.LaunchRamCustom, 25);
        var builder = new LaunchArgumentBuilder(settings);

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("-Xmx8192m", result.Arguments);
        Assert.Equal(8192, LaunchArgumentBuilder.ConvertLegacyRamSliderToMb(25));
    }

    [Fact]
    public void LaunchArgumentBuilderUsesInstanceCustomRamBeforeGlobalRam()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": { "game": ["--username", "${auth_player_name}"] },
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchRamType, 1);
        settings.Set(AppSettingKeys.LaunchRamCustom, 25);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionRamType}", 1);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionRamCustom}", 13);
        var builder = new LaunchArgumentBuilder(settings);

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("-Xmx2048m", result.Arguments);
        Assert.DoesNotContain("-Xmx8192m", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderAddsAuthlibInjectorPrefetchLikeOldPcl()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": { "game": ["--username", "${auth_player_name}"] },
          "libraries": []
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();
        var metadata = """{"meta":{"serverName":"Example Auth"}}""";
        var login = new LoginSession(LoginType.Auth, "Alex", "uuid", "token", "client", AuthlibInjectorMetadata: metadata);

        var result = builder.Build(
            CreateRequest(instance, temp.Path) with { LoginType = LoginType.Auth, LoginServer = "https://auth.example.com/api/yggdrasil" },
            CreateJava("C:\\Java17\\bin\\java.exe", 17),
            login);

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata));
        Assert.Contains("-javaagent:", result.Arguments);
        Assert.Contains("authlib-injector.jar", result.Arguments);
        Assert.Contains("-Dauthlibinjector.side=client", result.Arguments);
        Assert.Contains("-Dauthlibinjector.yggdrasil.prefetched=" + encoded, result.Arguments);
    }

    [Theory]
    [InlineData(LoginType.Legacy)]
    [InlineData(LoginType.Nide)]
    [InlineData(LoginType.Auth)]
    [InlineData(LoginType.Ms)]
    public void LaunchArgumentBuilderAlwaysUsesMsaUserTypeLikeOldPcl(LoginType loginType)
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": { "game": ["--userType", "${user_type}"] },
          "libraries": []
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(
            CreateRequest(instance, temp.Path),
            CreateJava("C:\\Java17\\bin\\java.exe", 17),
            new LoginSession(loginType, "Alex", "uuid", "token", "client"));

        Assert.Contains("--userType msa", result.Arguments);
        Assert.DoesNotContain("--userType legacy", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderIgnoresCustomRamSliderInAutoModeAndCaps32BitJava()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.12.2", """
        {
          "id": "1.12.2",
          "releaseTime": "2017-09-18T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "minecraftArguments": "--username ${auth_player_name}",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchRamType, 0);
        settings.Set(AppSettingKeys.LaunchRamCustom, 49);
        var java32 = new JavaEntry("C:\\Java8\\bin\\java.exe", new Version(1, 8, 0, 0), false, false, false, false);
        var builder = new LaunchArgumentBuilder(settings, systemMemory: new FakeSystemMemoryService(16));

        var result = builder.Build(CreateRequest(instance, temp.Path) with { MaxMemoryMb = 4096 }, java32, new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("-Xmx1024m", result.Arguments);
        Assert.DoesNotContain("-Xmx49152m", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderUsesOldPclAutoRamAlgorithmForNormalVersions()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": { "game": ["--username", "${auth_player_name}"] },
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchRamType, 0);
        var builder = new LaunchArgumentBuilder(settings, systemMemory: new FakeSystemMemoryService(16));

        var result = builder.Build(CreateRequest(instance, temp.Path) with { MaxMemoryMb = 2048 }, CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("-Xmx5529m", result.Arguments);
        Assert.DoesNotContain("-Xmx2048m", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderCountsModsWhenAutoRamUsesOldPclModableBranch()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "fabric-1.20.1", """
        {
          "id": "fabric-1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": { "game": ["--username", "${auth_player_name}"] },
          "libraries": []
        }
        """, createJar: true);
        instance = instance with
        {
            Version = instance.Version with { HasFabric = true }
        };
        var modsDirectory = Path.Combine(instance.VersionPath, "mods");
        Directory.CreateDirectory(modsDirectory);
        for (var i = 0; i < 90; i++)
        {
            File.WriteAllText(Path.Combine(modsDirectory, $"mod-{i}.jar"), "");
        }

        File.WriteAllText(Path.Combine(modsDirectory, "readme.txt"), "");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionRamType}", 0);
        var builder = new LaunchArgumentBuilder(settings, systemMemory: new FakeSystemMemoryService(16));

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("-Xmx8499m", result.Arguments);
        Assert.DoesNotContain("-Xmx5529m", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderHonorsArgumentFeatureRules()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": [
              "--username", "${auth_player_name}",
              {
                "rules": [{ "action": "allow", "features": { "has_custom_resolution": true } }],
                "value": ["--width", "${resolution_width}", "--height", "${resolution_height}"]
              },
              {
                "rules": [{ "action": "allow", "features": { "has_quick_plays_support": true } }],
                "value": ["--quickPlayPath", "quick.json"]
              }
            ]
          },
          "libraries": []
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();
        var login = new LegacyLoginService().CreateSession("Alex");
        var java = CreateJava("C:\\Java17\\bin\\java.exe", 17);

        var windowed = builder.Build(CreateRequest(instance, temp.Path), java, login);
        var fullscreen = builder.Build(CreateRequest(instance, temp.Path) with { WindowType = 0 }, java, login);

        Assert.Contains("--width 854 --height 480", windowed.Arguments);
        Assert.Equal(1, CountOccurrences(windowed.Arguments, "--width"));
        Assert.Equal(1, CountOccurrences(windowed.Arguments, "--height"));
        Assert.DoesNotContain("--quickPlayPath", windowed.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--width 854 --height 480", fullscreen.Arguments);
        Assert.DoesNotContain("--quickPlayPath", fullscreen.Arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchArgumentBuilderUsesDefaultResolutionForMaximizedWindowMode()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": ["--username", "${auth_player_name}"]
          },
          "libraries": []
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(
            CreateRequest(instance, temp.Path) with { WindowType = 4, WindowWidth = 1600, WindowHeight = 900 },
            CreateJava("C:\\Java17\\bin\\java.exe", 17),
            new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("--width 854 --height 480", result.Arguments);
        Assert.DoesNotContain("--fullscreen", result.Arguments);
        Assert.DoesNotContain("--width 1600", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderUsesInheritedAssetIndex()
    {
        using var temp = new TempDirectory();
        WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "arguments": {
            "game": ["--assetIndex", "${assets_index_name}"]
          },
          "libraries": []
        }
        """, createJar: true);
        var child = WriteInstance(temp.Path, "Forge Child", """
        {
          "id": "Forge Child",
          "inheritsFrom": "1.20.1",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": []
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(CreateRequest(child, temp.Path), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("--assetIndex 5", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderRemovesEmptyVersionTypeOption()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": ["--username", "${auth_player_name}", "--versionType", "${version_type}"]
          },
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchArgumentInfo, "");
        var builder = new LaunchArgumentBuilder(settings);

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("--username Alex", result.Arguments);
        Assert.DoesNotContain("--versionType", result.Arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchArgumentBuilderUsesVersionArgumentInfoBeforeGlobalInfo()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": ["--username", "${auth_player_name}", "--versionType", "${version_type}"]
          },
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchArgumentInfo, "Global Info");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentInfo}", "Instance Info");
        var builder = new LaunchArgumentBuilder(settings);

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("--versionType \"Instance Info\"", result.Arguments);
        Assert.DoesNotContain("Global Info", result.Arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchArgumentBuilderLetsInstanceGcOverrideGlobal()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": ["--username", "${auth_player_name}"]
          },
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchAdvanceGC, 3);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceGC}", 1);
        var builder = new LaunchArgumentBuilder(settings);

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java21\\bin\\java.exe", 21), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("-XX:+UseZGC", result.Arguments);
        Assert.Contains("-XX:+ZGenerational", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderTreatsInstanceGcZeroAsGlobal()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": ["--username", "${auth_player_name}"]
          },
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchAdvanceGC, 3);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceGC}", 0);
        var builder = new LaunchArgumentBuilder(settings);

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java21\\bin\\java.exe", 21), new LegacyLoginService().CreateSession("Alex"));

        Assert.DoesNotContain("-XX:+UseZGC", result.Arguments);
        Assert.DoesNotContain("-XX:+UseG1GC", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderMapsInstanceGcLikeOldPclComboBox()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": ["--username", "${auth_player_name}"]
          },
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchAdvanceGC, 0);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceGC}", 4);
        var builder = new LaunchArgumentBuilder(settings);

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java21\\bin\\java.exe", 21), new LegacyLoginService().CreateSession("Alex"));

        Assert.DoesNotContain("-XX:+UseZGC", result.Arguments);
        Assert.DoesNotContain("-XX:+UseG1GC", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderAddsJavaLaunchWrapperForNonAsciiRootWhenEnabled()
    {
        using var temp = new TempDirectory();
        var oldCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var root = Path.Combine(temp.Path, "中文路径");
            var instance = WriteInstance(root, "1.20.1", """
            {
              "id": "1.20.1",
              "releaseTime": "2023-06-12T12:00:00+00:00",
              "mainClass": "net.minecraft.client.main.Main",
              "arguments": {
                "game": ["--username", "${auth_player_name}"]
              },
              "libraries": []
            }
            """, createJar: true);
            var settings = new AppSettingsService(new TestAppPathService(temp.Path));
            var builder = new LaunchArgumentBuilder(settings);

            var result = builder.Build(CreateRequest(instance, root), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Alex"));

            Assert.Contains("-Doolloo.jlw.tmpdir=", result.Arguments);
            Assert.Contains("-jar", result.Arguments);
            Assert.Contains("JavaWrapper.jar", result.Arguments);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
    }

    [Fact]
    public void LaunchArgumentBuilderHonorsInstanceJavaLaunchWrapperDisable()
    {
        using var temp = new TempDirectory();
        var oldCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var root = Path.Combine(temp.Path, "中文路径");
            var instance = WriteInstance(root, "1.20.1", """
            {
              "id": "1.20.1",
              "releaseTime": "2023-06-12T12:00:00+00:00",
              "mainClass": "net.minecraft.client.main.Main",
              "arguments": {
                "game": ["--username", "${auth_player_name}"]
              },
              "libraries": []
            }
            """, createJar: true);
            var settings = new AppSettingsService(new TestAppPathService(temp.Path));
            settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceDisableJLW}", true);
            var builder = new LaunchArgumentBuilder(settings);

            var result = builder.Build(CreateRequest(instance, root), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Alex"));

            Assert.DoesNotContain("JavaWrapper.jar", result.Arguments);
            Assert.DoesNotContain("-Doolloo.jlw.tmpdir=", result.Arguments);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
    }

    [Fact]
    public void LaunchArgumentBuilderAddsLwjglUnsafeAgentForLwjgl341()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.21.9", """
        {
          "id": "1.21.9",
          "releaseTime": "2026-01-01T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": ["--username", "${auth_player_name}"]
          },
          "libraries": [
            { "name": "org.lwjgl:lwjgl:3.4.1" }
          ]
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder(new AppSettingsService(new TestAppPathService(temp.Path)));

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java21\\bin\\java.exe", 21), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("LUA.jar", result.Arguments);
    }

    [Fact]
    public void LaunchArgumentBuilderHonorsInstanceLwjglUnsafeAgentDisable()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.21.9", """
        {
          "id": "1.21.9",
          "releaseTime": "2026-01-01T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": ["--username", "${auth_player_name}"]
          },
          "libraries": [
            { "name": "org.lwjgl:lwjgl:3.4.1" }
          ]
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceDisableLUA}", true);
        var builder = new LaunchArgumentBuilder(settings);

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java21\\bin\\java.exe", 21), new LegacyLoginService().CreateSession("Alex"));

        Assert.DoesNotContain("LUA.jar", result.Arguments);
    }

    [Fact]
    public async Task LaunchPatchServiceCopiesRequiredJlwAndLuaFiles()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "patch-source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "java-wrapper.jar"), "jlw");
        await File.WriteAllTextAsync(Path.Combine(source, "lwjgl-unsafe-agent.jar"), "lua");
        var instance = WriteInstance(temp.Path, "1.21.9", """
        {
          "id": "1.21.9",
          "releaseTime": "2026-01-01T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava("C:\\Java21\\bin\\java.exe", 21);
        var login = new LegacyLoginService().CreateSession("Alex");
        var profile = new LaunchProfile(
            instance,
            java,
            login,
            "-javaagent:\"LUA.jar\" -jar \"JavaWrapper.jar\"",
            "",
            new ProcessStartInfo(java.PathJava),
            []);

        var result = await new LaunchPatchService(new NullLoggerService(), source).PrepareAsync(profile);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(instance.VersionPath, "JavaWrapper.jar")));
        Assert.True(File.Exists(Path.Combine(instance.VersionPath, "LUA.jar")));
        Assert.Equal("jlw", await File.ReadAllTextAsync(Path.Combine(instance.VersionPath, "JavaWrapper.jar")));
        Assert.Equal("lua", await File.ReadAllTextAsync(Path.Combine(instance.VersionPath, "LUA.jar")));
    }

    [Fact]
    public async Task GameProcessWatcherCapturesStdoutAndStderr()
    {
        var logger = new CaptureLoggerService();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe", "/c \"echo hello-out --accessToken secret-out & echo hello-err --accessToken secret-err 1>&2\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();

        var result = await new GameProcessWatcher(logger, TimeSpan.FromSeconds(2)).WatchAsync(process);
        await process.WaitForExitAsync();
        await WaitUntilAsync(() =>
            logger.Messages.Any(message => message.Contains("游戏输出：hello-out --accessToken ***", StringComparison.Ordinal))
            && logger.Messages.Any(message => message.Contains("游戏错误：hello-err --accessToken ***", StringComparison.Ordinal)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.OutputTail, message => message.Contains("hello-out --accessToken ***", StringComparison.Ordinal));
        Assert.Contains(result.ErrorTail, message => message.Contains("hello-err --accessToken ***", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("secret-out", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("secret-err", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("游戏进程退出：0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GameProcessWatcherDrainsStderrBeforeReturningExitedResult()
    {
        var logger = new CaptureLoggerService();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe", "/c \"echo final-crash-line 1>&2 & exit /b 7\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();

        var result = await new GameProcessWatcher(logger, TimeSpan.FromSeconds(2)).WatchAsync(process);

        Assert.True(result.HasExited);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains(result.ErrorTail, message => message.Contains("final-crash-line", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("游戏错误：final-crash-line", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, ProcessPriorityClass.AboveNormal)]
    [InlineData(1, null)]
    [InlineData(2, ProcessPriorityClass.BelowNormal)]
    [InlineData(99, null)]
    public void LaunchProcessConfiguratorMapsOldPrioritySetting(int setting, ProcessPriorityClass? expected)
    {
        Assert.Equal(expected, LaunchProcessConfigurator.MapPriority(setting));
    }

    [Fact]
    public void LaunchProcessConfiguratorSetsHighPerformanceGpuBeforeStart()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchAdvanceGraphicCard, true);
        var gpu = new FakeGpuPreferenceService();
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var profile = new LaunchProfile(
            CreateInstance("1.20.1"),
            java,
            new LegacyLoginService().CreateSession("Alex"),
            "",
            "",
            new ProcessStartInfo(java.PathJava),
            []);
        var configurator = new LaunchProcessConfigurator(settings, new NullLoggerService(), gpu);

        configurator.PrepareStart(profile);

        Assert.Contains(Path.GetFullPath(java.PathJava), gpu.Paths);
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            Assert.Contains(Path.GetFullPath(Environment.ProcessPath), gpu.Paths);
        }
    }

    [Fact]
    public void LaunchProcessConfiguratorSkipsGpuPreferenceWhenDisabled()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchAdvanceGraphicCard, false);
        var gpu = new FakeGpuPreferenceService();
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var profile = new LaunchProfile(
            CreateInstance("1.20.1"),
            java,
            new LegacyLoginService().CreateSession("Alex"),
            "",
            "",
            new ProcessStartInfo(java.PathJava),
            []);
        var configurator = new LaunchProcessConfigurator(settings, new NullLoggerService(), gpu);

        configurator.PrepareStart(profile);

        Assert.Empty(gpu.Paths);
    }

    [Fact]
    public async Task LaunchPipelineConfiguresProcessAfterStart()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var configurator = new FakeLaunchProcessConfigurator();
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, configurator);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Equal(1, configurator.PrepareStartCount);
        Assert.Equal(1, configurator.ConfigureCount);
    }

    [Fact]
    public async Task LaunchPipelineRunsMemoryOptimizationWhenGlobalSettingIsEnabled()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchArgumentRam, true);
        var optimizer = new FakeLaunchMemoryOptimizer();
        var pipeline = CreatePipeline(
            new FakeJavaDiscoveryService([java]),
            new FakeProcessLauncher(),
            settings: settings,
            memoryOptimizer: optimizer);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Equal(1, optimizer.OptimizeCount);
        Assert.Contains(pipeline.Steps, step => step.Name == "内存优化" && step.Status == LaunchStepStatus.Succeeded);
    }

    [Fact]
    public async Task LaunchPipelineLetsInstanceMemoryOptimizationOverrideGlobalSetting()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchArgumentRam, true);
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionRamOptimize}", 2);
        var optimizer = new FakeLaunchMemoryOptimizer();
        var pipeline = CreatePipeline(
            new FakeJavaDiscoveryService([java]),
            new FakeProcessLauncher(),
            settings: settings,
            memoryOptimizer: optimizer);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Equal(0, optimizer.OptimizeCount);
        Assert.Contains(pipeline.Steps, step => step.Name == "内存优化" && step.Status == LaunchStepStatus.Skipped);
    }

    [Fact]
    public void LaunchArgumentBuilderAddsWindowArgumentsWhenVersionDoesNotProvideThem()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.12.2", """
        {
          "id": "1.12.2",
          "type": "release",
          "releaseTime": "2017-09-18T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "minecraftArguments": "--username ${auth_player_name}",
          "libraries": []
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java8\\bin\\java.exe", 8), new LegacyLoginService().CreateSession("Alex"));

        Assert.Contains("--width 854 --height 480", result.Arguments);
    }

    [Fact]
    public async Task LaunchPipelineGeneratesProfileWithFakeJava()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "arguments": { "game": ["--username", "${auth_player_name}"] },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), new FakeProcessLauncher());

        var result = await pipeline.GenerateProfileAsync(CreateRequest(instance, temp.Path));

        Assert.True(result.Success);
        Assert.NotNull(result.Profile);
        Assert.Empty(result.Profile.MissingFiles);
        Assert.Contains("--username Steve", result.Profile.Arguments);
    }

    [Fact]
    public async Task LaunchPipelineReportsUserFriendlyJavaRequirementWhenJavaIsMissing()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([]), new FakeProcessLauncher());

        var result = await pipeline.GenerateProfileAsync(CreateRequest(instance, temp.Path));

        var issue = Assert.Single(result.Issues, issue => issue.Code == "JavaNotFound");
        Assert.False(result.Success);
        Assert.Equal("未找到满足 Java 17 的 Java", issue.Message);
        Assert.Contains(pipeline.Steps, step => step.Name == "获取 Java" && step.Message == issue.Message);
    }

    [Fact]
    public async Task LaunchPipelineIgnoresStepSubscriberFailures()
    {
        using var temp = new TempDirectory();
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([]), new FakeProcessLauncher());
        pipeline.StepsChanged += (_, _) => throw new InvalidOperationException("CollectionView failed");

        var result = await pipeline.GenerateProfileAsync(new LaunchRequest(
            null,
            temp.Path,
            null,
            "Steve",
            512,
            2048,
            854,
            480,
            "",
            "",
            false));

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "InstanceMissing");
    }

    [Fact]
    public async Task LaunchPipelineStepsExposeStableSnapshots()
    {
        using var temp = new TempDirectory();
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([]), new FakeProcessLauncher());

        await pipeline.GenerateProfileAsync(new LaunchRequest(
            null,
            temp.Path,
            null,
            "Steve",
            512,
            2048,
            854,
            480,
            "",
            "",
            false));
        var firstSnapshot = pipeline.Steps;
        Assert.Equal(LaunchStepStatus.Failed, firstSnapshot[0].Status);

        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        await pipeline.GenerateProfileAsync(CreateRequest(instance, temp.Path));

        Assert.Equal(LaunchStepStatus.Failed, firstSnapshot[0].Status);
        Assert.Equal(LaunchStepStatus.Succeeded, pipeline.Steps[0].Status);
    }

    [Fact]
    public async Task LaunchPipelineReturnsMissingFilesBeforeStartingProcess()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "MissingLocalFiles");
        Assert.Contains(result.Issues, issue => issue.Message.Contains("无法自动补全", StringComparison.Ordinal));
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task LaunchPipelineSkipsMissingFileBlockWhenVersionFileCheckIsDisabled()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceAssetsV2}", true);
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, settings: settings);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Empty(result.Profile?.MissingFiles ?? []);
        Assert.Equal(1, launcher.StartCount);
    }

    [Fact]
    public async Task LaunchPipelineUsesManualJavaWhenVersionIgnoresCompatibilityWarning()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java8 = CreateJava(Path.Combine(temp.Path, "java8", "bin", "java.exe"), 8);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionAdvanceJava}", true);
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java8]), new FakeProcessLauncher(), settings: settings);

        var result = await pipeline.GenerateProfileAsync(CreateRequest(instance, temp.Path) with { JavaPath = java8.PathJava });

        Assert.True(result.Success);
        Assert.Equal(java8.PathJava, result.Profile?.Java.PathJava);
    }

    [Fact]
    public async Task LaunchPipelineReportsRemainingFilesAfterAutomaticCompletionAttempt()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "downloads": {
            "client": {
              "url": "https://piston-data.mojang.com/v1/objects/client/1.20.1.jar",
              "size": 2048
            }
          },
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        var issue = Assert.Single(result.Issues, issue => issue.Code == "MissingLocalFiles");
        Assert.False(result.Success);
        Assert.Equal(0, launcher.StartCount);
        Assert.Contains("已尝试自动补全", issue.Message, StringComparison.Ordinal);
        Assert.Contains("1.20.1.jar", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchPipelineReportsRemainingFilesWhenDownloadTaskFails()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "downloads": {
            "client": {
              "url": "https://piston-data.mojang.com/v1/objects/client/1.20.1.jar",
              "size": 2048
            }
          },
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var pipeline = CreatePipeline(
            new FakeJavaDiscoveryService([java]),
            launcher,
            downloadManager: new FailingDownloadManagerService("所有下载源均失败"));

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        var issue = Assert.Single(result.Issues, issue => issue.Code == "FileCompletionFailed");
        Assert.False(result.Success);
        Assert.Equal(0, launcher.StartCount);
        Assert.Contains("所有下载源均失败", issue.Message, StringComparison.Ordinal);
        Assert.Contains("1.20.1.jar", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchPipelineStartsProcessThroughAbstraction()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Equal(1, launcher.StartCount);
        Assert.Equal(instance.VersionPath, launcher.LastStartInfo?.WorkingDirectory);
        Assert.False(launcher.LastStartInfo?.UseShellExecute);
        Assert.True(launcher.LastStartInfo?.RedirectStandardError);
    }

    [Fact]
    public async Task LaunchPipelineReturnsDiagnosticWhenGameExitsEarly()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var watcher = new FakeGameProcessWatcher(GameProcessWatchResult.Exited(
            1,
            [],
            [
                "Error loading class: net/minecraft/client/Minecraft (java.lang.IllegalArgumentException: Unsupported class file major version 69)",
                "Mixin apply for mod fancymenu failed"
            ]));
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, gameProcessWatcher: watcher);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        var issue = Assert.Single(result.Issues, issue => issue.Code == "GameExitedEarly");
        Assert.False(result.Success);
        Assert.Equal(1, launcher.StartCount);
        Assert.Contains("Java 版本过新", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Unsupported class file major version 69", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchPipelineReturnsDiagnosticWhenGameExitsEarlyWithZeroExitCode()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var watcher = new FakeGameProcessWatcher(GameProcessWatchResult.Exited(
            0,
            [],
            ["Minecraft main thread stopped before window appeared"]));
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, gameProcessWatcher: watcher);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        var issue = Assert.Single(result.Issues, issue => issue.Code == "GameExitedEarly");
        Assert.False(result.Success);
        Assert.Equal(1, launcher.StartCount);
        Assert.Contains("退出码：0", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Minecraft main thread stopped", issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("net.fabricmc.loader.impl.FormattedException: Incompatible mod set! Mod sodium requires fabric-api", "Mod 前置依赖缺失或版本不匹配")]
    [InlineData("org.lwjgl.glfw.GLFWErrorCallbackI: GLFW error 65542: WGL: The driver does not appear to support OpenGL", "显卡驱动或 OpenGL 支持异常")]
    [InlineData("java.nio.file.AccessDeniedException: C:\\Games\\.minecraft\\versions\\test\\test.jar", "文件被占用或没有访问权限")]
    [InlineData("java.lang.UnsatisfiedLinkError: Failed to load library lwjgl.dll from natives directory", "natives 或 LWJGL 运行库加载失败")]
    public async Task LaunchPipelineDiagnosesCommonEarlyGameExitLogs(string logLine, string expectedMessage)
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var watcher = new FakeGameProcessWatcher(GameProcessWatchResult.Exited(1, [], [logLine]));
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, gameProcessWatcher: watcher);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        var issue = Assert.Single(result.Issues, issue => issue.Code == "GameExitedEarly");
        Assert.False(result.Success);
        Assert.Contains(expectedMessage, issue.Message, StringComparison.Ordinal);
        Assert.Contains(logLine, issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchPipelineUsesGlobalMinecraftRootWhenVersionIsolationIsDisabled()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "minecraftArguments": "--username ${auth_player_name} --gameDir ${game_directory}",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchArgumentIndieV2, 0);
        var gameDirectories = new MinecraftGameDirectoryService(settings);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var pipeline = CreatePipeline(
            new FakeJavaDiscoveryService([java]),
            launcher,
            settings: settings,
            gameDirectories: gameDirectories);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(temp.Path), launcher.LastStartInfo?.WorkingDirectory);
        Assert.Contains(Path.GetFullPath(temp.Path), result.Profile?.Arguments);
        Assert.False(settings.HasSaved($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentIndieV2}"));
    }

    [Fact]
    public void MinecraftGameDirectoryServiceAutoEnablesIsolationForVersionFolderUserData()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """);
        Directory.CreateDirectory(Path.Combine(instance.VersionPath, "mods"));
        File.WriteAllText(Path.Combine(instance.VersionPath, "mods", "example.jar"), "");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchArgumentIndieV2, 0);
        var service = new MinecraftGameDirectoryService(settings);

        var directory = service.Resolve(instance);

        Assert.True(directory.IsIsolated);
        Assert.Equal(Path.GetFullPath(instance.VersionPath), directory.Path);
        Assert.False(settings.HasSaved($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentIndieV2}"));
    }

    [Fact]
    public void LaunchVariableReplacerUsesResolvedGameDirectoryForVersionIndieTokens()
    {
        var instance = CreateInstance("1.20.1");
        var java = CreateJava("C:\\Java17\\bin\\java.exe", 17);
        var startInfo = new ProcessStartInfo(java.PathJava) { WorkingDirectory = "C:\\MC" };
        var profile = new LaunchProfile(
            instance,
            java,
            new LoginSession(LoginType.Legacy, "Steve", "uuid", "token", "client"),
            "",
            "",
            startInfo,
            []);

        var replaced = LaunchVariableReplacer.Replace("{version_indie}|{verindie}|{version_path}", profile, replaceTime: false);

        Assert.Equal("C:\\MC|C:\\MC|" + instance.VersionPath, replaced);
    }

    [Fact]
    public async Task LaunchPipelineSchedulesWindowMaximizeForMaximizedWindowMode()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var gameWindow = new FakeGameWindowService();
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, gameWindow: gameWindow);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true, WindowType = 4 });

        Assert.True(result.Success);
        Assert.Equal(1, gameWindow.ScheduleCount);
        Assert.Equal(TimeSpan.FromSeconds(2), gameWindow.LastDelay);
    }

    [Fact]
    public async Task LaunchPipelineSchedulesCustomWindowTitle()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var gameWindow = new FakeGameWindowService();
        var windowTitle = new FakeLaunchWindowTitleService("{name} - {date}");
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, gameWindow: gameWindow, windowTitle: windowTitle);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Equal(1, gameWindow.SetTitleCount);
        Assert.Equal("1.20.1 - {date}", gameWindow.LastTitleTemplate);
        Assert.Equal(TimeSpan.Zero, gameWindow.LastSetTitleDelay);
    }

    [Fact]
    public async Task LaunchPipelineAppliesLauncherVisibilityAfterStartingProcess()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var visibility = new FakeLauncherVisibilityService();
        var pipeline = CreatePipeline(new FakeJavaDiscoveryService([java]), launcher, launcherVisibility: visibility);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true, LauncherVisibility = 3 });

        Assert.True(result.Success);
        Assert.Equal(1, visibility.ApplyCount);
        Assert.Equal(3, visibility.LastLauncherVisibility);
    }

    [Fact]
    public void LaunchWindowTitleServicePrefersInstanceTitleAndLeavesTimeTokens()
    {
        using var temp = new TempDirectory();
        var instance = CreateInstance("1.20.1");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.LaunchArgumentTitle, "全局 {name} {date}");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentTitle}", "实例 {version} {time}");
        var profile = new LaunchProfile(
            instance,
            CreateJava("C:\\Java17\\bin\\java.exe", 17),
            new LegacyLoginService().CreateSession("Alex"),
            "",
            "",
            new ProcessStartInfo("java.exe"),
            []);
        var service = new LaunchWindowTitleService(settings);

        var title = service.ResolveTitle(profile);

        Assert.Equal("实例 1.20.1 {time}", title);
    }

    [Fact]
    public void LaunchWindowTitleServiceReplacesSetupVariablesLikeOldPcl()
    {
        using var temp = new TempDirectory();
        var instance = CreateInstance("1.20.1");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentInfo}", "InstanceInfo");
        settings.Set($"Instance.{instance.Name}.{AppSettingKeys.VersionArgumentTitle}", "Title {version} {setup:VersionArgumentInfo} {time}");
        var profile = new LaunchProfile(
            instance,
            CreateJava("C:\\Java17\\bin\\java.exe", 17),
            new LegacyLoginService().CreateSession("Alex"),
            "",
            "",
            new ProcessStartInfo("java.exe"),
            []);
        var service = new LaunchWindowTitleService(settings);

        var title = service.ResolveTitle(profile);

        Assert.Equal("Title 1.20.1 InstanceInfo {time}", title);
    }

    [Fact]
    public void LauncherVisibilityServiceAppliesOldPclVisibilityActions()
    {
        var host = new FakeLauncherWindowHost();
        var service = new LauncherVisibilityService(new NullLoggerService(), host);
        using var process = StartShortProcess();

        service.ApplyAfterLaunch(0, process);
        service.ApplyAfterLaunch(4, process);
        service.ApplyAfterLaunch(5, process);

        Assert.Equal(1, host.CloseCount);
        Assert.Equal(1, host.MinimizeCount);
        Assert.Equal(0, host.HideCount);
        Assert.Equal(0, host.ShowToTopCount);
    }

    [Fact]
    public async Task LauncherVisibilityServiceHandlesHiddenModesAfterGameExit()
    {
        var closeHost = new FakeLauncherWindowHost();
        var closeService = new LauncherVisibilityService(new NullLoggerService(), closeHost);
        using var closeProcess = StartShortProcess();

        closeService.ApplyAfterLaunch(2, closeProcess);
        await closeProcess.WaitForExitAsync();
        await WaitUntilAsync(() => closeHost.CloseCount == 1);

        Assert.Equal(1, closeHost.HideCount);
        Assert.Equal(1, closeHost.CloseCount);

        var restoreHost = new FakeLauncherWindowHost();
        var restoreService = new LauncherVisibilityService(new NullLoggerService(), restoreHost);
        using var restoreProcess = StartShortProcess();

        restoreService.ApplyAfterLaunch(3, restoreProcess);
        await restoreProcess.WaitForExitAsync();
        await WaitUntilAsync(() => restoreHost.ShowToTopCount == 1);

        Assert.Equal(1, restoreHost.HideCount);
        Assert.Equal(1, restoreHost.ShowToTopCount);
    }

    [Fact]
    public async Task LaunchFileCompleterPlansLibrariesAssetIndexAndAssets()
    {
        using var temp = new TempDirectory();
        var assetHash = "0123456789abcdef0123456789abcdef01234567";
        Directory.CreateDirectory(Path.Combine(temp.Path, "assets", "indexes"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "assets", "indexes", "5.json"), $$"""
        {
          "objects": {
            "minecraft/sounds/demo.ogg": {
              "hash": "{{assetHash}}",
              "size": 12
            }
          }
        }
        """);
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "downloads": {
            "client": {
              "url": "https://piston-data.mojang.com/v1/objects/client/1.20.1.jar",
              "size": 2048
            }
          },
          "assetIndex": {
            "id": "5",
            "url": "https://piston-meta.mojang.com/v1/packages/assets/5.json"
          },
          "libraries": [
            {
              "name": "org.example:demo:1.0.0",
              "downloads": {
                "artifact": {
                  "path": "org/example/demo/1.0.0/demo-1.0.0.jar",
                  "url": "https://libraries.minecraft.net/org/example/demo/1.0.0/demo-1.0.0.jar",
                  "size": 1024
                }
              }
            }
          ]
        }
        """);
        var completer = new LaunchFileCompleter(
            new DownloadSourceService(new AppSettingsService(new TestAppPathService(temp.Path))),
            new FileCheckService(new NullLoggerService()),
            new NullLoggerService());

        var result = await completer.BuildCompletionPlanAsync(CreateRequest(instance, temp.Path), []);

        Assert.Contains(result.Downloads, file => file.LocalPath.EndsWith(Path.Combine("versions", "1.20.1", "1.20.1.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Downloads, file => file.LocalPath.EndsWith(Path.Combine("libraries", "org", "example", "demo", "1.0.0", "demo-1.0.0.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Downloads, file => file.LocalPath.EndsWith(Path.Combine("assets", "indexes", "5.json"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Downloads, file => file.LocalPath.EndsWith(Path.Combine("assets", "objects", assetHash[..2], assetHash), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LaunchFileCompleterClassifiesMissingFilesWithoutDownloadSources()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
        var localOnlyMissing = Path.Combine(instance.VersionPath, "custom-local-only.jar");
        var completer = new LaunchFileCompleter(
            new DownloadSourceService(new AppSettingsService(new TestAppPathService(temp.Path))),
            new FileCheckService(new NullLoggerService()),
            new NullLoggerService());

        var result = await completer.BuildCompletionPlanAsync(CreateRequest(instance, temp.Path), [localOnlyMissing]);

        Assert.Contains(localOnlyMissing, result.MissingFiles);
        Assert.Contains(localOnlyMissing, result.UnresolvableMissingFiles);
        Assert.DoesNotContain(localOnlyMissing, result.DownloadableMissingFiles);
    }

    [Fact]
    public async Task LaunchFileCompleterPlansLoggingConfig()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.12.2", """
        {
          "id": "1.12.2",
          "releaseTime": "2017-09-18T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "logging": {
            "client": {
              "argument": "-Dlog4j.configurationFile=${path}",
              "file": {
                "id": "client-1.12.xml",
                "sha1": "0123456789abcdef0123456789abcdef01234567",
                "size": 877,
                "url": "https://launcher.mojang.com/v1/objects/log/client-1.12.xml"
              },
              "type": "log4j2-xml"
            }
          },
          "libraries": []
        }
        """);
        var completer = new LaunchFileCompleter(
            new DownloadSourceService(new AppSettingsService(new TestAppPathService(temp.Path))),
            new FileCheckService(new NullLoggerService()),
            new NullLoggerService());

        var result = await completer.BuildCompletionPlanAsync(CreateRequest(instance, temp.Path), []);

        var download = Assert.Single(result.Downloads, file => file.LocalPath.EndsWith(Path.Combine("assets", "log_configs", "client-1.12.xml"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains("client-1.12.xml", download.Sources[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", download.Check.Hash);
        Assert.Equal(877, download.Check.ActualSize);
        Assert.Contains(result.MissingFiles, file => file.EndsWith(Path.Combine("assets", "log_configs", "client-1.12.xml"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LaunchFileCompleterPlansNideAuthAgentLikeOldPcl()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
        var http = new FakeLaunchHttpClient();
        http.Enqueue("""{"jarHash":"d41d8cd98f00b204e9800998ecf8427e"}""");
        var completer = new LaunchFileCompleter(
            new DownloadSourceService(new AppSettingsService(new TestAppPathService(temp.Path))),
            new FileCheckService(new NullLoggerService()),
            new NullLoggerService(),
            http);

        var result = await completer.BuildCompletionPlanAsync(
            CreateRequest(instance, temp.Path) with { LoginType = LoginType.Nide, LoginServer = "00000000000000000000000000000000" },
            []);

        var download = Assert.Single(result.Downloads, file => file.LocalPath.EndsWith("nide8auth.jar", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("https://login.mc-user.com:233/index/jar", download.Sources);
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", download.Check.Hash);
        Assert.Contains(result.MissingFiles, file => file.EndsWith("nide8auth.jar", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, http.SendCount);
    }

    [Fact]
    public async Task LaunchFileCompleterPlansAuthlibInjectorAgentLikeOldPcl()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
        var http = new FakeLaunchHttpClient();
        var sha256 = new string('a', 64);
        http.Enqueue("{\"download_url\":\"https://authlib-injector.yushi.moe/artifact/1/authlib-injector.jar\",\"checksums\":{\"sha256\":\"" + sha256 + "\"}}");
        var completer = new LaunchFileCompleter(
            new DownloadSourceService(new AppSettingsService(new TestAppPathService(temp.Path))),
            new FileCheckService(new NullLoggerService()),
            new NullLoggerService(),
            http);

        var result = await completer.BuildCompletionPlanAsync(
            CreateRequest(instance, temp.Path) with { LoginType = LoginType.Auth, LoginServer = "https://auth.example.com/api/yggdrasil" },
            []);

        var download = Assert.Single(result.Downloads, file => file.LocalPath.EndsWith("authlib-injector.jar", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("https://authlib-injector.yushi.moe/artifact/1/authlib-injector.jar", download.Sources);
        Assert.Contains("https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/1/authlib-injector.jar", download.Sources);
        Assert.Equal(sha256, download.Check.Hash);
        Assert.Contains(result.MissingFiles, file => file.EndsWith("authlib-injector.jar", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, http.SendCount);
    }

    [Theory]
    [InlineData(LoginType.Nide, "nide8auth.jar", "00000000000000000000000000000000")]
    [InlineData(LoginType.Auth, "authlib-injector.jar", "https://auth.example.com/api/yggdrasil")]
    public async Task LaunchFileCompleterKeepsExistingThirdPartyAgentWhenMetadataFails(LoginType loginType, string fileName, string server)
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
        await File.WriteAllBytesAsync(Path.Combine(instance.VersionPath, fileName), new byte[2048]);
        var http = new FakeLaunchHttpClient();
        http.EnqueueException(new HttpRequestException("metadata offline"));
        var completer = new LaunchFileCompleter(
            new DownloadSourceService(new AppSettingsService(new TestAppPathService(temp.Path))),
            new FileCheckService(new NullLoggerService()),
            new NullLoggerService(),
            http);

        var result = await completer.BuildCompletionPlanAsync(
            CreateRequest(instance, temp.Path) with { LoginType = loginType, LoginServer = server },
            []);

        Assert.DoesNotContain(result.MissingFiles, file => file.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Downloads, file => file.LocalPath.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, http.SendCount);
    }

    [Fact]
    public async Task LaunchFileCompleterSkipsLibrariesDisallowedByRules()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": [
            {
              "name": "org.example:windows-lib:1.0.0",
              "rules": [{ "action": "allow", "os": { "name": "windows" } }],
              "downloads": {
                "artifact": {
                  "path": "org/example/windows-lib/1.0.0/windows-lib-1.0.0.jar",
                  "url": "https://libraries.minecraft.net/org/example/windows-lib/1.0.0/windows-lib-1.0.0.jar",
                  "size": 100
                }
              }
            },
            {
              "name": "org.example:linux-lib:1.0.0",
              "rules": [{ "action": "allow", "os": { "name": "linux" } }],
              "downloads": {
                "artifact": {
                  "path": "org/example/linux-lib/1.0.0/linux-lib-1.0.0.jar",
                  "url": "https://libraries.minecraft.net/org/example/linux-lib/1.0.0/linux-lib-1.0.0.jar",
                  "size": 100
                }
              }
            },
            {
              "name": "org.example:wrong-arch-lib:1.0.0",
              "rules": [{ "action": "allow", "os": { "name": "windows", "arch": "sparc" } }],
              "downloads": {
                "artifact": {
                  "path": "org/example/wrong-arch-lib/1.0.0/wrong-arch-lib-1.0.0.jar",
                  "url": "https://libraries.minecraft.net/org/example/wrong-arch-lib/1.0.0/wrong-arch-lib-1.0.0.jar",
                  "size": 100
                }
              }
            },
            {
              "name": "org.example:bad-version-rule-lib:1.0.0",
              "rules": [{ "action": "allow", "os": { "name": "windows", "version": "[" } }],
              "downloads": {
                "artifact": {
                  "path": "org/example/bad-version-rule-lib/1.0.0/bad-version-rule-lib-1.0.0.jar",
                  "url": "https://libraries.minecraft.net/org/example/bad-version-rule-lib/1.0.0/bad-version-rule-lib-1.0.0.jar",
                  "size": 100
                }
              }
            }
          ]
        }
        """);
        var completer = new LaunchFileCompleter();

        var result = await completer.BuildCompletionPlanAsync(CreateRequest(instance, temp.Path), []);

        Assert.Contains(result.Downloads, file => file.LocalPath.Contains("windows-lib", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Downloads, file => file.LocalPath.Contains("linux-lib", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Downloads, file => file.LocalPath.Contains("wrong-arch-lib", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Downloads, file => file.LocalPath.Contains("bad-version-rule-lib", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MissingFiles, file => file.Contains("windows-lib", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MissingFiles, file => file.Contains("linux-lib", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MissingFiles, file => file.Contains("wrong-arch-lib", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MissingFiles, file => file.Contains("bad-version-rule-lib", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LaunchArgumentBuilderSkipsLibrariesDisallowedByRules()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": [
            {
              "name": "org.example:windows-lib:1.0.0",
              "rules": [{ "action": "allow", "os": { "name": "windows" } }]
            },
            {
              "name": "org.example:linux-lib:1.0.0",
              "rules": [{ "action": "allow", "os": { "name": "linux" } }]
            },
            {
              "name": "org.example:wrong-arch-lib:1.0.0",
              "rules": [{ "action": "allow", "os": { "name": "windows", "arch": "sparc" } }]
            }
          ]
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Steve"));

        Assert.Contains(result.MissingFiles, file => file.Contains("windows-lib", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MissingFiles, file => file.Contains("linux-lib", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MissingFiles, file => file.Contains("wrong-arch-lib", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("windows-lib", result.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("linux-lib", result.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wrong-arch-lib", result.Arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchFileCompleterPlansArtifactAndNativeClassifierWhenBothExist()
    {
        using var temp = new TempDirectory();
        CreateEmptyAssetIndex(temp.Path, "5");
        var nativeClassifier = Environment.Is64BitOperatingSystem ? "natives-windows-64" : "natives-windows-32";
        var instance = WriteInstance(temp.Path, "1.20.1", $$"""
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "libraries": [
            {
              "name": "org.lwjgl:lwjgl:3.3.1",
              "natives": { "windows": "natives-windows-${arch}" },
              "extract": { "exclude": ["META-INF/", "skip/"] },
              "downloads": {
                "artifact": {
                  "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar",
                  "url": "https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar",
                  "size": 100
                },
                "classifiers": {
                  "{{nativeClassifier}}": {
                    "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-{{nativeClassifier}}.jar",
                    "url": "https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-{{nativeClassifier}}.jar",
                    "size": 200
                  }
                }
              }
            }
          ]
        }
        """);
        var completer = new LaunchFileCompleter();

        var result = await completer.BuildCompletionPlanAsync(CreateRequest(instance, temp.Path), []);

        Assert.Contains(result.Downloads, file => file.LocalPath.EndsWith(Path.Combine("org", "lwjgl", "lwjgl", "3.3.1", "lwjgl-3.3.1.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Downloads, file => file.LocalPath.EndsWith(Path.Combine("org", "lwjgl", "lwjgl", "3.3.1", $"lwjgl-3.3.1-{nativeClassifier}.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MissingFiles, file => file.EndsWith(Path.Combine("org", "lwjgl", "lwjgl", "3.3.1", "lwjgl-3.3.1.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MissingFiles, file => file.EndsWith(Path.Combine("org", "lwjgl", "lwjgl", "3.3.1", $"lwjgl-3.3.1-{nativeClassifier}.jar"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LaunchArgumentBuilderDoesNotPutNativeOnlyLibraryOnClasspath()
    {
        using var temp = new TempDirectory();
        var nativeClassifier = Environment.Is64BitOperatingSystem ? "natives-windows-64" : "natives-windows-32";
        var instance = WriteInstance(temp.Path, "1.20.1", $$"""
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": [
            {
              "name": "org.lwjgl:lwjgl-native-only:3.3.1",
              "natives": { "windows": "natives-windows-${arch}" },
              "downloads": {
                "classifiers": {
                  "{{nativeClassifier}}": {
                    "path": "org/lwjgl/lwjgl-native-only/3.3.1/lwjgl-native-only-3.3.1-{{nativeClassifier}}.jar",
                    "url": "https://libraries.minecraft.net/org/lwjgl/lwjgl-native-only/3.3.1/lwjgl-native-only-3.3.1-{{nativeClassifier}}.jar",
                    "size": 200
                  }
                }
              }
            }
          ]
        }
        """, createJar: true);
        var builder = new LaunchArgumentBuilder();

        var result = builder.Build(CreateRequest(instance, temp.Path), CreateJava("C:\\Java17\\bin\\java.exe", 17), new LegacyLoginService().CreateSession("Steve"));

        Assert.DoesNotContain(result.MissingFiles, file => file.Contains("lwjgl-native-only", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("lwjgl-native-only", result.Arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NativesExtractorPreservesDirectoriesAndSkipsUnsafeEntries()
    {
        using var temp = new TempDirectory();
        var nativeClassifier = Environment.Is64BitOperatingSystem ? "natives-windows-64" : "natives-windows-32";
        var wrongNativeClassifier = Environment.Is64BitOperatingSystem ? "natives-windows-32" : "natives-windows-64";
        var nativeJarPath = Path.Combine(temp.Path, "libraries", "org", "lwjgl", "lwjgl", "3.3.1", $"lwjgl-3.3.1-{nativeClassifier}.jar");
        var wrongNativeJarPath = Path.Combine(temp.Path, "libraries", "org", "lwjgl", "lwjgl", "3.3.1", $"lwjgl-3.3.1-{wrongNativeClassifier}.jar");
        CreateNativeJar(nativeJarPath);
        CreateNativeJar(wrongNativeJarPath, "wrong-arch.dll");
        var instance = WriteInstance(temp.Path, "1.20.1", $$"""
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": [
            {
              "name": "org.lwjgl:lwjgl:3.3.1",
              "natives": { "windows": "natives-windows-${arch}" },
              "extract": { "exclude": ["META-INF/", "skip/"] },
              "downloads": {
                "classifiers": {
                  "{{nativeClassifier}}": {
                    "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-{{nativeClassifier}}.jar"
                  },
                  "{{wrongNativeClassifier}}": {
                    "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-{{wrongNativeClassifier}}.jar"
                  }
                }
              }
            }
          ]
        }
        """, createJar: true);
        var extractor = new NativesExtractor(new NullLoggerService());

        var nativesDirectory = await extractor.ExtractAsync(instance);

        Assert.True(File.Exists(Path.Combine(nativesDirectory, "lwjgl.dll")));
        Assert.True(File.Exists(Path.Combine(nativesDirectory, "subdir", "helper.dll")));
        Assert.False(File.Exists(Path.Combine(nativesDirectory, "wrong-arch.dll")));
        Assert.False(File.Exists(Path.Combine(nativesDirectory, "skip", "ignored.dll")));
        Assert.False(File.Exists(Path.Combine(nativesDirectory, "MANIFEST.MF")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "versions", "escape.dll")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "escape.dll")));
    }

    [Fact]
    public async Task NativesExtractorReadsInheritedVersionLibraries()
    {
        using var temp = new TempDirectory();
        var nativeClassifier = Environment.Is64BitOperatingSystem ? "natives-windows-64" : "natives-windows-32";
        var nativeJarPath = Path.Combine(temp.Path, "libraries", "org", "lwjgl", "parent-native", "3.3.1", $"parent-native-3.3.1-{nativeClassifier}.jar");
        CreateNativeJar(nativeJarPath, "parent.dll");
        WriteInstance(temp.Path, "1.20.1", $$"""
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": [
            {
              "name": "org.lwjgl:parent-native:3.3.1",
              "natives": { "windows": "natives-windows-${arch}" },
              "downloads": {
                "classifiers": {
                  "{{nativeClassifier}}": {
                    "path": "org/lwjgl/parent-native/3.3.1/parent-native-3.3.1-{{nativeClassifier}}.jar"
                  }
                }
              }
            }
          ]
        }
        """, createJar: true);
        var child = WriteInstance(temp.Path, "Forge Child", """
        {
          "id": "Forge Child",
          "inheritsFrom": "1.20.1",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": []
        }
        """, createJar: true);
        var extractor = new NativesExtractor(new NullLoggerService());

        var nativesDirectory = await extractor.ExtractAsync(child);

        Assert.Equal(Path.Combine(child.VersionPath, "Forge Child-natives"), nativesDirectory);
        Assert.True(File.Exists(Path.Combine(nativesDirectory, "parent.dll")));
    }

    [Fact]
    public async Task LaunchPreRunWritesOptionsLanguageAndFullscreen()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.Language, "zh-CN");
        var service = new LaunchPreRunService(settings, new NullLoggerService());

        await service.PrepareAsync(
            CreateRequest(instance, temp.Path) with { WindowType = 0 },
            new LegacyLoginService().CreateSession("Steve"));

        var options = File.ReadAllLines(Path.Combine(instance.VersionPath, "options.txt"));
        Assert.Contains("lang:zh_cn", options);
        Assert.Contains("fullscreen:true", options);
    }

    [Fact]
    public async Task LaunchPreRunPreservesExistingLanguageAndDisablesFullscreenForWindowedMode()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        await File.WriteAllTextAsync(Path.Combine(instance.VersionPath, "options.txt"), "lang:en_us" + Environment.NewLine + "fullscreen:true");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.Language, "zh-CN");
        var service = new LaunchPreRunService(settings, new NullLoggerService());

        await service.PrepareAsync(
            CreateRequest(instance, temp.Path) with { WindowType = 3 },
            new LegacyLoginService().CreateSession("Steve"));

        var options = File.ReadAllLines(Path.Combine(instance.VersionPath, "options.txt"));
        Assert.Contains("lang:en_us", options);
        Assert.Contains("fullscreen:false", options);
    }

    [Fact]
    public async Task LaunchPreRunWritesLastServerForAutoJoin()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        await File.WriteAllTextAsync(Path.Combine(instance.VersionPath, "options.txt"), "lang:en_us");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var service = new LaunchPreRunService(settings, new NullLoggerService());

        await service.PrepareAsync(
            CreateRequest(instance, temp.Path) with { ServerIp = "play.example.com:25565" },
            new LoginSession(LoginType.Legacy, "Steve", "uuid", "token", "client"));

        var options = File.ReadAllLines(Path.Combine(instance.VersionPath, "options.txt"));
        Assert.Contains("lang:en_us", options);
        Assert.Contains("lastServer:play.example.com:25565", options);
    }

    [Fact]
    public async Task LaunchPreRunUpdatesYosbrOptionsWhenRootOptionsIsMissing()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var yosbrOptionsPath = Path.Combine(instance.VersionPath, "config", "yosbr", "options.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(yosbrOptionsPath)!);
        await File.WriteAllTextAsync(yosbrOptionsPath, "lang:en_us" + Environment.NewLine + "fullscreen:true");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.Language, "zh-CN");
        var service = new LaunchPreRunService(settings, new NullLoggerService());

        await service.PrepareAsync(
            CreateRequest(instance, temp.Path) with { WindowType = 3 },
            new LegacyLoginService().CreateSession("Steve"));

        Assert.False(File.Exists(Path.Combine(instance.VersionPath, "options.txt")));
        var options = File.ReadAllLines(yosbrOptionsPath);
        Assert.Contains("lang:zh_cn", options);
        Assert.Contains("fullscreen:false", options);
    }

    [Fact]
    public async Task LaunchPreRunUsesLegacyChineseLanguageCodeForOldVersions()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.10.2", """
        {
          "id": "1.10.2",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.Language, "zh-CN");
        var service = new LaunchPreRunService(settings, new NullLoggerService());

        await service.PrepareAsync(
            CreateRequest(instance, temp.Path),
            new LegacyLoginService().CreateSession("Steve"));

        var options = File.ReadAllLines(Path.Combine(instance.VersionPath, "options.txt"));
        Assert.Contains("lang:zh_CN", options);
    }

    [Fact]
    public async Task LaunchPreRunBuildsOfflineCustomSkinResourcePack()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var paths = new TestAppPathService(Path.Combine(temp.Path, "appdata"));
        paths.EnsureCreated();
        var skinPath = Path.Combine(paths.AppDataDirectory, "CustomSkin.png");
        await File.WriteAllBytesAsync(skinPath, [1, 2, 3, 4]);
        var settings = new AppSettingsService(paths);
        settings.Set(AppSettingKeys.LaunchSkinType, 4);
        settings.Set(AppSettingKeys.LaunchSkinSlim, true);
        var service = new LaunchPreRunService(settings, new NullLoggerService(), paths: paths);

        await service.PrepareAsync(
            CreateRequest(instance, temp.Path),
            new LegacyLoginService().CreateSession("Steve"));

        var packPath = Path.Combine(instance.VersionPath, "resourcepacks", "PCL2 Skin.zip");
        Assert.True(File.Exists(packPath));
        using var archive = ZipFile.OpenRead(packPath);
        Assert.NotNull(archive.GetEntry("pack.mcmeta"));
        Assert.NotNull(archive.GetEntry("pack.png"));
        Assert.NotNull(archive.GetEntry("assets/minecraft/textures/entity/player/slim/alex.png"));
        Assert.NotNull(archive.GetEntry("assets/minecraft/textures/entity/player/slim/steve.png"));
        using var reader = new StreamReader(archive.GetEntry("pack.mcmeta")!.Open());
        var meta = await reader.ReadToEndAsync();
        Assert.Contains("\"pack_format\":15", meta);

        var options = File.ReadAllLines(Path.Combine(instance.VersionPath, "options.txt"));
        Assert.Contains("resourcePacks:[\"vanilla\",\"file/PCL2 Skin.zip\"]", options);
    }

    [Fact]
    public async Task LaunchPreRunCropsLegacyDoubleLayerSkinForMinecraft16And17()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.7.10", """
        {
          "id": "1.7.10",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var paths = new TestAppPathService(Path.Combine(temp.Path, "appdata"));
        paths.EnsureCreated();
        var skinPath = Path.Combine(paths.AppDataDirectory, "CustomSkin.png");
        await File.WriteAllBytesAsync(skinPath, CreatePng(64, 64));
        var settings = new AppSettingsService(paths);
        settings.Set(AppSettingKeys.LaunchSkinType, 4);
        settings.Set(AppSettingKeys.LaunchSkinSlim, false);
        var service = new LaunchPreRunService(settings, new NullLoggerService(), paths: paths);

        await service.PrepareAsync(
            CreateRequest(instance, temp.Path),
            new LegacyLoginService().CreateSession("Steve"));

        var packPath = Path.Combine(instance.VersionPath, "resourcepacks", "PCL2 Skin.zip");
        using var archive = ZipFile.OpenRead(packPath);
        var entry = archive.GetEntry("assets/minecraft/textures/entity/steve.png");
        Assert.NotNull(entry);
        await using var stream = entry!.Open();
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        var skinBytes = output.ToArray();

        Assert.Equal(64, ReadPngWidth(skinBytes));
        Assert.Equal(32, ReadPngHeight(skinBytes));
        var options = File.ReadAllLines(Path.Combine(instance.VersionPath, "options.txt"));
        Assert.Contains("resourcePacks:[\"PCL2 Skin.zip\"]", options);
    }

    [Fact]
    public async Task LaunchPreRunRemovesOfflineSkinPackWhenSkinIsDisabled()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.12.2", """
        {
          "id": "1.12.2",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var packPath = Path.Combine(instance.VersionPath, "resourcepacks", "PCL2 Skin.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(packPath)!);
        await File.WriteAllBytesAsync(packPath, [1, 2, 3]);
        await File.WriteAllTextAsync(
            Path.Combine(instance.VersionPath, "options.txt"),
            "resourcePacks:[\"Faithful.zip\",\"PCL2 Skin.zip\"]");
        var paths = new TestAppPathService(Path.Combine(temp.Path, "appdata"));
        var settings = new AppSettingsService(paths);
        settings.Set(AppSettingKeys.LaunchSkinType, 0);
        var service = new LaunchPreRunService(settings, new NullLoggerService(), paths: paths);

        await service.PrepareAsync(
            CreateRequest(instance, temp.Path),
            new LegacyLoginService().CreateSession("Steve"));

        Assert.False(File.Exists(packPath));
        var options = File.ReadAllLines(Path.Combine(instance.VersionPath, "options.txt"));
        Assert.Contains("resourcePacks:[\"Faithful.zip\"]", options);
    }

    [Fact]
    public async Task LaunchPreRunWritesMicrosoftLauncherProfileWithLoginUuid()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var service = new LaunchPreRunService(settings, new NullLoggerService());
        var login = new LoginSession(LoginType.Ms, "Steve\"Name", "abcdef12-3456-7890-abcd-ef1234567890", "token", "client-token");

        await service.PrepareAsync(CreateRequest(instance, temp.Path), login);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(temp.Path, "launcher_profiles.json")))!.AsObject();
        var profileId = "abcdef1234567890abcdef1234567890";
        Assert.Equal("client-token", root["clientToken"]!.GetValue<string>());
        Assert.Equal(profileId, root["selectedUser"]!["account"]!.GetValue<string>());
        Assert.Equal(profileId, root["selectedUser"]!["profile"]!.GetValue<string>());
        Assert.Equal("Steve-Name", root["authenticationDatabase"]![profileId]!["username"]!.GetValue<string>());
        Assert.Equal("Steve\"Name", root["authenticationDatabase"]![profileId]!["profiles"]![profileId]!["displayName"]!.GetValue<string>());
    }

    [Fact]
    public async Task LaunchPreRunPreservesExistingLauncherProfileAccounts()
    {
        using var temp = new TempDirectory();
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """, createJar: true);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "launcher_profiles.json"), """
        {
          "clientToken": "old-token",
          "authenticationDatabase": {
            "existingprofile": {
              "username": "Existing",
              "profiles": {
                "existingprofile": {
                  "displayName": "Existing"
                }
              }
            }
          }
        }
        """);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var service = new LaunchPreRunService(settings, new NullLoggerService());
        var login = new LoginSession(LoginType.Ms, "Alex", "abcdef12-3456-7890-abcd-ef1234567890", "token", "new-token");

        await service.PrepareAsync(CreateRequest(instance, temp.Path), login);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(temp.Path, "launcher_profiles.json")))!.AsObject();
        var database = root["authenticationDatabase"]!.AsObject();
        Assert.Equal("new-token", root["clientToken"]!.GetValue<string>());
        Assert.Equal("Existing", database["existingprofile"]!["username"]!.GetValue<string>());
        Assert.Equal("Existing", database["existingprofile"]!["profiles"]!["existingprofile"]!["displayName"]!.GetValue<string>());
        Assert.Equal("Alex", database["abcdef1234567890abcdef1234567890"]!["profiles"]!["abcdef1234567890abcdef1234567890"]!["displayName"]!.GetValue<string>());
    }

    [Fact]
    public async Task LaunchPipelineDownloadsMissingClientJarBeforeStarting()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "assets", "indexes"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "assets", "indexes", "5.json"), "{\"objects\":{}}");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "downloads": {
            "client": {
              "url": "https://piston-data.mojang.com/v1/objects/client/1.20.1.jar",
              "size": 2048
            }
          },
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var bytes = new FakeDownloadByteClient();
        bytes.Map("https://piston-data.mojang.com/v1/objects/client/1.20.1.jar", Enumerable.Repeat((byte)7, 2048).ToArray());
        var logger = new NullLoggerService();
        var checker = new FileCheckService(logger);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var sources = new DownloadSourceService(settings);
        var pipeline = new LaunchPipelineService(
            new FakeJavaDiscoveryService([java]),
            new JavaSelectorService(),
            new FakeLoginService(),
            new LaunchArgumentBuilder(),
            new LaunchFileCompleter(sources, checker, logger),
            new DownloadManagerService(bytes, checker, logger),
            new FakeNativesExtractor(),
            new FakePreRunService(),
            new FakeLaunchPatchService(),
            new FakeCustomCommandService(),
            new LaunchScriptExporter(),
            launcher,
            new FakeLaunchProcessConfigurator(),
            new FakeGameProcessWatcher(),
            logger);
        var fileCompletionMessages = new List<string>();
        pipeline.StepsChanged += (_, steps) =>
        {
            var step = steps.FirstOrDefault(step => step.Name == "补全文件");
            if (step is not null)
            {
                fileCompletionMessages.Add(step.Message);
            }
        };

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Equal(1, launcher.StartCount);
        Assert.True(File.Exists(Path.Combine(instance.VersionPath, "1.20.1.jar")));
        Assert.Contains(fileCompletionMessages, message => message.Contains("0/1", StringComparison.Ordinal));
        Assert.Contains(fileCompletionMessages, message => message.Contains("1/1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaunchPipelineKeepsFileCompletionRunningWhenDownloadSnapshotSubscriberThrows()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "assets", "indexes"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "assets", "indexes", "5.json"), "{\"objects\":{}}");
        var instance = WriteInstance(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "downloads": {
            "client": {
              "url": "https://piston-data.mojang.com/v1/objects/client/1.20.1.jar",
              "size": 2048
            }
          },
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var bytes = new FakeDownloadByteClient();
        bytes.Map("https://piston-data.mojang.com/v1/objects/client/1.20.1.jar", Enumerable.Repeat((byte)7, 2048).ToArray());
        var logger = new NullLoggerService();
        var checker = new FileCheckService(logger);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var sources = new DownloadSourceService(settings);
        var downloadManager = new DownloadManagerService(bytes, checker, logger);
        downloadManager.SnapshotChanged += (_, _) => throw new InvalidOperationException("CollectionView failed");
        var pipeline = new LaunchPipelineService(
            new FakeJavaDiscoveryService([java]),
            new JavaSelectorService(),
            new FakeLoginService(),
            new LaunchArgumentBuilder(),
            new LaunchFileCompleter(sources, checker, logger),
            downloadManager,
            new FakeNativesExtractor(),
            new FakePreRunService(),
            new FakeLaunchPatchService(),
            new FakeCustomCommandService(),
            new LaunchScriptExporter(),
            launcher,
            new FakeLaunchProcessConfigurator(),
            new FakeGameProcessWatcher(),
            logger);

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Equal(1, launcher.StartCount);
        Assert.True(File.Exists(Path.Combine(instance.VersionPath, "1.20.1.jar")));
    }

    [Fact]
    public async Task LaunchPipelineDownloadsAssetIndexThenAssetObjectsBeforeStarting()
    {
        using var temp = new TempDirectory();
        var assetBytes = Encoding.UTF8.GetBytes("asset-content");
        var assetHash = ComputeSha1(assetBytes);
        var assetIndex = Encoding.UTF8.GetBytes($$"""
        {
          "objects": {
            "minecraft/lang/en_us.json": {
              "hash": "{{assetHash}}",
              "size": {{assetBytes.Length}}
            }
          }
        }
        """);
        var assetIndexHash = ComputeSha1(assetIndex);
        var instance = WriteInstance(temp.Path, "1.20.1", $$"""
        {
          "id": "1.20.1",
          "releaseTime": "2023-06-12T12:00:00+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": {
            "id": "5",
            "url": "https://launchermeta.mojang.com/v1/packages/index/5.json",
            "sha1": "{{assetIndexHash}}",
            "size": {{assetIndex.Length}}
          },
          "libraries": []
        }
        """, createJar: true);
        var java = CreateJava(Path.Combine(temp.Path, "java", "bin", "java.exe"), 17);
        var launcher = new FakeProcessLauncher();
        var bytes = new FakeDownloadByteClient();
        bytes.Map("https://launchermeta.mojang.com/v1/packages/index/5.json", assetIndex);
        bytes.Map($"https://resources.download.minecraft.net/{assetHash[..2]}/{assetHash}", assetBytes);
        var logger = new NullLoggerService();
        var checker = new FileCheckService(logger);
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var sources = new DownloadSourceService(settings);
        var pipeline = new LaunchPipelineService(
            new FakeJavaDiscoveryService([java]),
            new JavaSelectorService(),
            new FakeLoginService(),
            new LaunchArgumentBuilder(),
            new LaunchFileCompleter(sources, checker, logger),
            new DownloadManagerService(bytes, checker, logger),
            new FakeNativesExtractor(),
            new FakePreRunService(),
            new FakeLaunchPatchService(),
            new FakeCustomCommandService(),
            new LaunchScriptExporter(),
            launcher,
            new FakeLaunchProcessConfigurator(),
            new FakeGameProcessWatcher(),
            logger);
        var fileCompletionMessages = new List<string>();
        pipeline.StepsChanged += (_, steps) =>
        {
            var step = steps.FirstOrDefault(step => step.Name == "补全文件");
            if (step is not null)
            {
                fileCompletionMessages.Add(step.Message);
            }
        };

        var result = await pipeline.LaunchAsync(CreateRequest(instance, temp.Path) with { StartProcess = true });

        Assert.True(result.Success);
        Assert.Equal(1, launcher.StartCount);
        Assert.True(File.Exists(Path.Combine(temp.Path, "assets", "indexes", "5.json")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "assets", "objects", assetHash[..2], assetHash)));
        Assert.Contains(fileCompletionMessages, message => message.Contains("第 1 轮补全", StringComparison.Ordinal));
        Assert.Contains(fileCompletionMessages, message => message.Contains("第 2 轮补全", StringComparison.Ordinal));
    }

    private static LaunchPipelineService CreatePipeline(
        IJavaDiscoveryService javaDiscovery,
        IProcessLauncher processLauncher,
        ILaunchProcessConfigurator? processConfigurator = null,
        IGameWindowService? gameWindow = null,
        ILauncherVisibilityService? launcherVisibility = null,
        ILaunchWindowTitleService? windowTitle = null,
        IAppSettingsService? settings = null,
        IMinecraftGameDirectoryService? gameDirectories = null,
        ILaunchMemoryOptimizer? memoryOptimizer = null,
        IDownloadManagerService? downloadManager = null,
        IGameProcessWatcher? gameProcessWatcher = null)
    {
        return new LaunchPipelineService(
            javaDiscovery,
            new JavaSelectorService(),
            new FakeLoginService(),
            new LaunchArgumentBuilder(settings, gameDirectories),
            new LaunchFileCompleter(),
            downloadManager ?? new FakeDownloadManagerService(),
            new FakeNativesExtractor(),
            new FakePreRunService(),
            new FakeLaunchPatchService(),
            new FakeCustomCommandService(),
            new LaunchScriptExporter(),
            processLauncher,
            processConfigurator ?? new FakeLaunchProcessConfigurator(),
            gameProcessWatcher ?? new FakeGameProcessWatcher(),
            new NullLoggerService(),
            gameWindow,
            launcherVisibility,
            windowTitle,
            gameDirectories,
            settings,
            memoryOptimizer);
    }

    private static LaunchRequest CreateRequest(MinecraftInstance instance, string root)
    {
        return new LaunchRequest(instance, root, null, "Steve", 512, 2048, 854, 480, "", "", false);
    }

    private static JavaEntry CreateJava(string path, int majorVersion)
    {
        return new JavaEntry(path, new Version(1, majorVersion, 0, 0), false, true, false, false);
    }

    private static byte[] CreatePng(int width, int height)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 4 + 1);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                var pixel = row + 1 + x * 4;
                raw[pixel] = (byte)(x % 256);
                raw[pixel + 1] = (byte)(y % 256);
                raw[pixel + 2] = 128;
                raw[pixel + 3] = 255;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> ihdr = stackalloc byte[13];
        WriteInt32BigEndian(ihdr[..4], width);
        WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WritePngChunk(png, "IHDR", ihdr.ToArray());
        WritePngChunk(png, "IDAT", compressed.ToArray());
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static int ReadPngWidth(byte[] png)
    {
        return ReadInt32BigEndian(png.AsSpan(16, 4));
    }

    private static int ReadPngHeight(byte[] png)
    {
        return ReadInt32BigEndian(png.AsSpan(20, 4));
    }

    private static void WritePngChunk(Stream target, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteInt32BigEndian(length, data.Length);
        target.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        target.Write(typeBytes);
        target.Write(data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteInt32BigEndian(crcBytes, unchecked((int)ComputeCrc32(typeBytes, data)));
        target.Write(crcBytes);
    }

    private static uint ComputeCrc32(byte[] typeBytes, byte[] data)
    {
        var crc = 0xffffffffu;
        foreach (var b in typeBytes.Concat(data))
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static void WriteInt32BigEndian(Span<byte> target, int value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> source)
    {
        return source[0] << 24 | source[1] << 16 | source[2] << 8 | source[3];
    }

    private static string ComputeSha1(byte[] bytes)
    {
        return Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
    }

    private static string GetSkinModel(string uuid)
    {
        if (uuid.Length != 32)
        {
            return "Steve";
        }

        var a = Convert.ToInt32(uuid[7].ToString(), 16);
        var b = Convert.ToInt32(uuid[15].ToString(), 16);
        var c = Convert.ToInt32(uuid[23].ToString(), 16);
        var d = Convert.ToInt32(uuid[31].ToString(), 16);
        return ((a ^ b ^ c ^ d) % 2) == 1 ? "Alex" : "Steve";
    }

    private static Process StartShortProcess()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe", "/c exit 0")
            {
                UseShellExecute = false
            }
        };
        process.Start();
        return process;
    }

    private static MinecraftInstance CreateInstance(string version)
    {
        var info = new MinecraftVersionInfo(version, "release", null, null, "", "net.minecraft.client.main.Main", version, false, false, false, false);
        return new MinecraftInstance(version, "C:\\MC", "C:\\MC\\versions\\" + version, "C:\\MC\\versions\\" + version + "\\" + version + ".json", MinecraftInstanceState.Ready, info, "");
    }

    private static MinecraftInstance WriteInstance(string root, string name, string json, bool createJar = false)
    {
        var versionPath = Path.Combine(root, "versions", name);
        Directory.CreateDirectory(versionPath);
        var jsonPath = Path.Combine(versionPath, $"{name}.json");
        File.WriteAllText(jsonPath, json);
        if (createJar)
        {
            File.WriteAllText(Path.Combine(versionPath, $"{name}.jar"), "");
        }

        return new MinecraftDiscoveryService().InspectInstance(root, versionPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name });
    }

    private static void CreateEmptyAssetIndex(string root, string id)
    {
        Directory.CreateDirectory(Path.Combine(root, "assets", "indexes"));
        File.WriteAllText(Path.Combine(root, "assets", "indexes", id + ".json"), "{\"objects\":{}}");
    }

    private static void CreateNativeJar(string path, string rootDllName = "lwjgl.dll")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        WriteZipEntry(archive, rootDllName, "native");
        WriteZipEntry(archive, "subdir/helper.dll", "helper");
        WriteZipEntry(archive, "skip/ignored.dll", "ignored");
        WriteZipEntry(archive, "META-INF/MANIFEST.MF", "manifest");
        WriteZipEntry(archive, "../escape.dll", "escape");
    }

    private static void WriteZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 50; i++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private sealed class CaptureLoggerService : IAppLoggerService
    {
        public List<string> Messages { get; } = [];

        public void Initialize()
        {
        }

        public void Info(string message)
        {
            Messages.Add(message);
        }

        public void Warn(string message)
        {
            Messages.Add(message);
        }

        public void Error(Exception exception, string message)
        {
            Messages.Add(message + ": " + exception.Message);
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

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public int StartCount { get; private set; }

        public ProcessStartInfo? LastStartInfo { get; private set; }

        public Process Start(ProcessStartInfo startInfo)
        {
            StartCount++;
            LastStartInfo = startInfo;
            return Process.GetCurrentProcess();
        }

        public Task<int> WaitForExitAsync(Process process, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class FakeLoginService : ILoginService
    {
        public Task<LoginSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LegacyLoginService().CreateSession(request.LegacyName));
        }
    }

    private sealed class FakeNativesExtractor : INativesExtractor
    {
        public Task<string> ExtractAsync(MinecraftInstance instance, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(instance.VersionPath, $"{instance.Name}-natives");
            Directory.CreateDirectory(path);
            return Task.FromResult(path);
        }
    }

    private sealed class FakePreRunService : ILaunchPreRunService
    {
        public Task PrepareAsync(LaunchRequest request, LoginSession login, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLaunchPatchService : ILaunchPatchService
    {
        public Task<LaunchPatchPrepareResult> PrepareAsync(LaunchProfile profile, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LaunchPatchPrepareResult.Ok([]));
        }
    }

    private sealed class FakeLaunchProcessConfigurator : ILaunchProcessConfigurator
    {
        public int PrepareStartCount { get; private set; }

        public int ConfigureCount { get; private set; }

        public void PrepareStart(LaunchProfile profile)
        {
            PrepareStartCount++;
        }

        public void Configure(Process process)
        {
            ConfigureCount++;
        }
    }

    private sealed class FakeLaunchMemoryOptimizer : ILaunchMemoryOptimizer
    {
        public int OptimizeCount { get; private set; }

        public Task<LaunchMemoryOptimizeResult> OptimizeAsync(CancellationToken cancellationToken = default)
        {
            OptimizeCount++;
            return Task.FromResult(new LaunchMemoryOptimizeResult(7));
        }
    }

    private sealed class FakeSystemMemoryService(double availableGb) : ISystemMemoryService
    {
        public long AvailablePhysicalMemoryBytes => (long)(availableGb * 1024d * 1024d * 1024d);
    }

    private sealed class FakeCustomCommandService : ICustomCommandService
    {
        public Task RunAsync(LaunchRequest request, LaunchProfile profile, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGameProcessWatcher(GameProcessWatchResult? result = null) : IGameProcessWatcher
    {
        public Task<GameProcessWatchResult> WatchAsync(Process process, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result ?? GameProcessWatchResult.Running());
        }
    }

    private sealed class FakeGameWindowService : IGameWindowService
    {
        public int ScheduleCount { get; private set; }

        public TimeSpan LastDelay { get; private set; }

        public int SetTitleCount { get; private set; }

        public string LastTitleTemplate { get; private set; } = "";

        public TimeSpan LastSetTitleDelay { get; private set; }

        public void ScheduleMaximize(Process process, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            ScheduleCount++;
            LastDelay = delay;
        }

        public void ScheduleSetTitle(Process process, string titleTemplate, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            SetTitleCount++;
            LastTitleTemplate = titleTemplate;
            LastSetTitleDelay = delay;
        }
    }

    private sealed class FakeLaunchWindowTitleService(string template) : ILaunchWindowTitleService
    {
        public string ResolveTitle(LaunchProfile profile)
        {
            return LaunchVariableReplacer.Replace(template, profile, replaceTime: false);
        }
    }

    private sealed class FakeGpuPreferenceService : IGpuPreferenceService
    {
        public List<string> Paths { get; } = [];

        public void SetHighPerformance(string executablePath)
        {
            Paths.Add(Path.GetFullPath(executablePath));
        }
    }

    private sealed class FakeMojangProfileService(string? uuid) : IMojangProfileService
    {
        public Task<string?> GetUuidAsync(string userName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(uuid);
        }
    }

    private sealed class FakeLaunchHttpClient : ILaunchHttpClient
    {
        private readonly Queue<Func<Task<string>>> _responses = [];

        public int SendCount { get; private set; }

        public void Enqueue(string content)
        {
            _responses.Enqueue(() => Task.FromResult(content));
        }

        public void EnqueueException(Exception exception)
        {
            _responses.Enqueue(() => Task.FromException<string>(exception));
        }

        public Task<string> SendAsync(LaunchHttpRequest request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No fake HTTP response queued.");
            }

            return _responses.Dequeue()();
        }
    }

    private sealed class ThrowingMicrosoftLoginService : IMicrosoftLoginService
    {
        public Task<LoginSession> LoginAsync(CancellationToken cancellationToken = default, bool forceNewLogin = false)
        {
            throw new InvalidOperationException("Microsoft login should not be called.");
        }
    }

    private sealed class ThrowingYggdrasilLoginService : IYggdrasilLoginService
    {
        public Task<LoginSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Yggdrasil login should not be called.");
        }
    }

    private sealed class FakeLauncherVisibilityService : ILauncherVisibilityService
    {
        public int ApplyCount { get; private set; }

        public int LastLauncherVisibility { get; private set; }

        public void ApplyAfterLaunch(int launcherVisibility, Process gameProcess, CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            LastLauncherVisibility = launcherVisibility;
        }
    }

    private sealed class FakeLauncherWindowHost : ILauncherWindowHost
    {
        public int CloseCount { get; private set; }

        public int HideCount { get; private set; }

        public int MinimizeCount { get; private set; }

        public int ShowToTopCount { get; private set; }

        public void Close()
        {
            CloseCount++;
        }

        public void Hide()
        {
            HideCount++;
        }

        public void Minimize()
        {
            MinimizeCount++;
        }

        public void ShowToTop()
        {
            ShowToTopCount++;
        }
    }

    private sealed class FakeDownloadManagerService : IDownloadManagerService
    {
        public event EventHandler<DownloadTaskSnapshot>? SnapshotChanged;

        public IReadOnlyList<DownloadTaskSnapshot> Tasks { get; } = [];

        public Task<DownloadTaskSnapshot> DownloadAsync(string name, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken = default)
        {
            var snapshot = new DownloadTaskSnapshot(name, DownloadTaskState.Succeeded, files.Count, files.Count, 0, 1, "下载完成");
            SnapshotChanged?.Invoke(this, snapshot);
            return Task.FromResult(snapshot);
        }

        public bool Cancel(string name)
        {
            return false;
        }

        public int CancelAllRunning()
        {
            return 0;
        }

        public Task<DownloadTaskSnapshot?> RetryAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DownloadTaskSnapshot?>(null);
        }

        public int ClearFinished()
        {
            return 0;
        }
    }

    private sealed class FailingDownloadManagerService(string message) : IDownloadManagerService
    {
        public event EventHandler<DownloadTaskSnapshot>? SnapshotChanged;

        public IReadOnlyList<DownloadTaskSnapshot> Tasks { get; } = [];

        public Task<DownloadTaskSnapshot> DownloadAsync(string name, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken = default)
        {
            var snapshot = new DownloadTaskSnapshot(name, DownloadTaskState.Failed, files.Count, 0, 0, 0, message);
            SnapshotChanged?.Invoke(this, snapshot);
            return Task.FromResult(snapshot);
        }

        public bool Cancel(string name)
        {
            return false;
        }

        public int CancelAllRunning()
        {
            return 0;
        }

        public Task<DownloadTaskSnapshot?> RetryAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DownloadTaskSnapshot?>(null);
        }

        public int ClearFinished()
        {
            return 0;
        }
    }

    private sealed class FakeDownloadByteClient : IDownloadByteClient
    {
        private readonly Dictionary<string, byte[]> _responses = new(StringComparer.OrdinalIgnoreCase);

        public void Map(string url, byte[] bytes)
        {
            _responses[url] = bytes;
        }

        public Task<byte[]> GetBytesAsync(string url, bool simulateBrowserHeaders = false, CancellationToken cancellationToken = default)
        {
            if (_responses.TryGetValue(url, out var bytes))
            {
                return Task.FromResult(bytes);
            }

            throw new HttpRequestException("not mapped: " + url);
        }
    }
}
