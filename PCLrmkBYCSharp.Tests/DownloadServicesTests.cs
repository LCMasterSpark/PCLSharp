using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.Launch;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Tests;

public sealed class DownloadServicesTests
{
    [Fact]
    public async Task FileCheckValidatesSizeHashAndJson()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "version.json");
        await File.WriteAllTextAsync(path, "{\"id\":\"1.20.1\"}");
        var hash = Convert.ToHexString(SHA1.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
        var checker = new FileCheckService(new NullLoggerService());

        var result = checker.Check(path, new DownloadFileCheck(ActualSize: new FileInfo(path).Length, Hash: hash, IsJson: true));

        Assert.Null(result);
    }

    [Fact]
    public async Task FileCheckReportsInvalidJson()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "bad.json");
        await File.WriteAllTextAsync(path, "not-json");
        var checker = new FileCheckService(new NullLoggerService());

        var result = checker.Check(path, new DownloadFileCheck(IsJson: true));

        Assert.NotNull(result);
    }

    [Fact]
    public void DownloadSourcesPreferMirrorByDefaultAndOfficialWhenForced()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var service = new DownloadSourceService(settings);

        var defaultOrder = service.GetLauncherOrMetaSources("https://launchermeta.mojang.com/mc/game/version.json");
        settings.Set(AppSettingKeys.ToolDownloadSource, 2);
        var officialOrder = service.GetLauncherOrMetaSources("https://launchermeta.mojang.com/mc/game/version.json");

        Assert.StartsWith("https://bmclapi2.bangbang93.com", defaultOrder[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://launchermeta.mojang.com", officialOrder[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DownloadSourcesAutoModeCanPreferOfficialAfterFastMojangManifest()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var service = new DownloadSourceService(settings);

        service.ReportOfficialVersionListLatency(TimeSpan.FromMilliseconds(350));
        var autoOrder = service.GetLauncherOrMetaSources("https://launchermeta.mojang.com/mc/game/version.json");
        settings.Set(AppSettingKeys.ToolDownloadSource, 0);
        var forcedMirrorOrder = service.GetLauncherOrMetaSources("https://launchermeta.mojang.com/mc/game/version.json");

        Assert.True(service.PreferOfficialDownloadsWhenAuto);
        Assert.StartsWith("https://launchermeta.mojang.com", autoOrder[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://bmclapi2.bangbang93.com", forcedMirrorOrder[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoaderVersionServiceReadsFabricAndForgeVersions()
    {
        var client = new FakeDownloadByteClient();
        client.Map("https://meta.fabricmc.net/v2/versions/loader/1.20.1", Encoding.UTF8.GetBytes("""
            [
              { "loader": { "version": "0.15.11", "stable": true } },
              { "loader": { "version": "0.15.10", "stable": false } }
            ]
            """));
        client.Map("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", Encoding.UTF8.GetBytes("""
            <metadata>
              <versioning>
                <versions>
                  <version>1.20.1-47.2.20</version>
                  <version>1.20.1-47.2.0</version>
                  <version>1.19.4-45.2.0</version>
                </versions>
              </versioning>
            </metadata>
            """));
        var service = new LoaderVersionService(client, new NullLoggerService());

        var fabric = await service.GetVersionsAsync("Fabric", "1.20.1");
        var forge = await service.GetVersionsAsync("Forge", "1.20.1");

        Assert.Equal(["0.15.11", "0.15.10"], fabric.Select(item => item.Version).ToArray());
        Assert.Equal(["47.2.20", "47.2.0"], forge.Select(item => item.Version).ToArray());
        Assert.All(forge, item => Assert.Equal("Forge Maven", item.SourceName));
    }

    [Fact]
    public async Task MinecraftManifestOfficialSuccessUpdatesAutoDownloadSourcePreference()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.ToolDownloadVersion, 2);
        settings.Set(AppSettingKeys.ToolDownloadSource, 1);
        var sources = new DownloadSourceService(settings);
        var client = new FakeDownloadByteClient();
        client.Map("https://launchermeta.mojang.com/mc/game/version_manifest.json", Encoding.UTF8.GetBytes("""
        {
          "versions": [
            {
              "id": "1.20.1",
              "type": "release",
              "releaseTime": "2023-06-12T12:00:00+00:00",
              "url": "https://piston-meta.mojang.com/v1/packages/1.20.1.json"
            }
          ]
        }
        """));
        var service = new MinecraftClientDownloadService(client, sources, settings);

        await service.GetVersionManifestAsync();
        var order = sources.GetLauncherOrMetaSources("https://piston-meta.mojang.com/v1/packages/1.20.1.json");

        Assert.True(sources.PreferOfficialDownloadsWhenAuto);
        Assert.StartsWith("https://piston-meta.mojang.com", order[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MinecraftClientInstallPlanCanReuseValidJsonFiles()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.ToolDownloadVersion, 2);
        settings.Set(AppSettingKeys.ToolDownloadSource, 2);
        var sources = new DownloadSourceService(settings);
        var client = new FakeDownloadByteClient();
        client.Map("https://launchermeta.mojang.com/mc/game/version_manifest.json", Encoding.UTF8.GetBytes("""
        {
          "versions": [
            {
              "id": "1.20.1",
              "type": "release",
              "releaseTime": "2023-06-12T12:00:00+00:00",
              "url": "https://piston-meta.mojang.com/v1/packages/1.20.1.json"
            }
          ]
        }
        """));
        client.Map("https://piston-meta.mojang.com/v1/packages/1.20.1.json", Encoding.UTF8.GetBytes("""
        {
          "id": "1.20.1",
          "downloads": {
            "client": {
              "url": "https://piston-data.mojang.com/v1/objects/client.jar",
              "sha1": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "size": 2048
            }
          },
          "assetIndex": {
            "id": "5",
            "url": "https://piston-meta.mojang.com/v1/packages/asset-index.json",
            "sha1": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "size": 128
          },
          "libraries": []
        }
        """));
        client.Map("https://piston-meta.mojang.com/v1/packages/asset-index.json", Encoding.UTF8.GetBytes("""
        {
          "objects": {
            "minecraft/sounds/test.ogg": {
              "hash": "cccccccccccccccccccccccccccccccccccccccc",
              "size": 16
            }
          }
        }
        """));
        var service = new MinecraftClientDownloadService(client, sources, settings);

        var plan = await service.CreateInstallPlanAsync(temp.Path, "1.20.1", "1.20.1");

        var versionJson = Assert.Single(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("versions", "1.20.1", "1.20.1.json"), StringComparison.OrdinalIgnoreCase));
        var assetIndex = Assert.Single(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("assets", "indexes", "5.json"), StringComparison.OrdinalIgnoreCase));
        Assert.True(versionJson.Check.IsJson);
        Assert.True(versionJson.Check.CanUseExistingFile);
        Assert.True(assetIndex.Check.IsJson);
        Assert.True(assetIndex.Check.CanUseExistingFile);
    }

    [Fact]
    public void DownloadSourcesMapLibrariesAssetsAndModMirrors()
    {
        using var temp = new TempDirectory();
        var service = new DownloadSourceService(new AppSettingsService(new TestAppPathService(temp.Path)));

        var libraries = service.GetLibrarySources("https://libraries.minecraft.net/com/mojang/authlib/1/authlib-1.jar");
        var assets = service.GetAssetSources("http://resources.download.minecraft.net/ab/hash");
        var mod = service.GetModMirrorSource("https://api.modrinth.com/v2/project/test");

        Assert.Contains(libraries, url => url.Contains("bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("https://bmclapi2.bangbang93.com/assets", assets[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mod.mcimirror.top/modrinth", mod, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DownloadSourcesMapFabricMavenToBmclMirror()
    {
        using var temp = new TempDirectory();
        var service = new DownloadSourceService(new AppSettingsService(new TestAppPathService(temp.Path)));

        var libraries = service.GetLibrarySources("https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.15.11/fabric-loader-0.15.11.jar");
        var quilt = service.GetLibrarySources("https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-loader/0.23.1/quilt-loader-0.23.1.jar");
        var forge = service.GetLibrarySources("https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar");
        var neoForge = service.GetLibrarySources("https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.237/neoforge-20.4.237-installer.jar");

        Assert.StartsWith("https://bmclapi2.bangbang93.com/maven", libraries[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://bmclapi2.bangbang93.com/maven", quilt[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://bmclapi2.bangbang93.com/maven", forge[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://bmclapi2.bangbang93.com/maven", neoForge[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadManagerSkipsExistingValidFile()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "cached.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var client = new FakeDownloadByteClient();
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());

        var result = await manager.DownloadAsync("skip", [
            new DownloadFile(["https://example.invalid/cached.bin"], path, new DownloadFileCheck(ActualSize: 3))
        ]);

        Assert.Equal(DownloadTaskState.Succeeded, result.State);
        Assert.Empty(client.RequestedUrls);
        Assert.Equal(1, result.FinishedFiles);
    }

    [Fact]
    public async Task DownloadManagerSkipsFilesAlreadyCoveredByRunningTasks()
    {
        using var temp = new TempDirectory();
        var sharedPath = Path.Combine(temp.Path, "shared.bin");
        var uniquePath = Path.Combine(temp.Path, "unique.bin");
        var client = new DelayedDownloadByteClient(TimeSpan.FromSeconds(10), releaseAfterConcurrentRequests: 2);
        client.Map("https://example.invalid/shared.bin", [1, 2, 3]);
        client.Map("https://example.invalid/unique.bin", [4, 5, 6, 7]);
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());

        var firstTask = manager.DownloadAsync("first", [
            new DownloadFile(["https://example.invalid/shared.bin"], sharedPath, new DownloadFileCheck(ActualSize: 3))
        ]);
        await client.WaitForFirstRequestAsync();
        var second = await manager.DownloadAsync("second", [
            new DownloadFile(["https://example.invalid/shared.bin"], sharedPath, new DownloadFileCheck(ActualSize: 3)),
            new DownloadFile(["https://example.invalid/unique.bin"], uniquePath, new DownloadFileCheck(ActualSize: 4))
        ]);
        var first = await firstTask;

        Assert.Equal(DownloadTaskState.Succeeded, first.State);
        Assert.Equal(DownloadTaskState.Succeeded, second.State);
        Assert.Equal(1, second.TotalFiles);
        Assert.Equal(uniquePath, second.PrimaryLocalPath);
        Assert.Equal([4, 5, 6, 7], await File.ReadAllBytesAsync(uniquePath));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(sharedPath));
    }

    [Fact]
    public async Task DownloadManagerTriesNextSourceAfterFailure()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "file.bin");
        var client = new FakeDownloadByteClient();
        client.Fail("https://bad.example/file.bin");
        client.Map("https://good.example/file.bin", [4, 5, 6, 7]);
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());

        var result = await manager.DownloadAsync("retry", [
            new DownloadFile(["https://bad.example/file.bin", "https://good.example/file.bin"], path, new DownloadFileCheck(ActualSize: 4))
        ]);

        Assert.Equal(DownloadTaskState.Succeeded, result.State);
        Assert.Equal(["https://bad.example/file.bin", "https://good.example/file.bin"], client.RequestedUrls);
        Assert.Equal([4, 5, 6, 7], await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task DownloadManagerDoesNotFailWhenSnapshotSubscriberThrows()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "file.bin");
        var client = new FakeDownloadByteClient();
        client.Map("https://good.example/file.bin", [4, 5, 6, 7]);
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());
        var snapshots = new List<DownloadTaskSnapshot>();
        manager.SnapshotChanged += (_, _) => throw new InvalidOperationException("CollectionView failed");
        manager.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);

        var result = await manager.DownloadAsync("subscriber", [
            new DownloadFile(["https://good.example/file.bin"], path, new DownloadFileCheck(ActualSize: 4))
        ]);

        Assert.Equal(DownloadTaskState.Succeeded, result.State);
        Assert.Equal([4, 5, 6, 7], await File.ReadAllBytesAsync(path));
        Assert.Contains(snapshots, snapshot => snapshot.State == DownloadTaskState.Succeeded);
    }

    [Fact]
    public async Task DownloadManagerKeepsExistingFileWhenAllSourcesFail()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "file.bin");
        await File.WriteAllBytesAsync(path, [9, 9]);
        var client = new FakeDownloadByteClient();
        client.Fail("https://bad.example/file.bin");
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());

        var result = await manager.DownloadAsync("preserve", [
            new DownloadFile(["https://bad.example/file.bin"], path, new DownloadFileCheck(ActualSize: 4, CanUseExistingFile: false))
        ]);

        Assert.Equal(DownloadTaskState.Failed, result.State);
        Assert.Equal([9, 9], await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.pcldownload"));
    }

    [Fact]
    public async Task DownloadManagerRetriesInvalidTempFileWithoutReplacingExistingFile()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "file.bin");
        await File.WriteAllBytesAsync(path, [9, 9]);
        var client = new FakeDownloadByteClient();
        client.Map("https://bad.example/file.bin", [1]);
        client.Map("https://good.example/file.bin", [4, 5, 6, 7]);
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());

        var result = await manager.DownloadAsync("retry-invalid", [
            new DownloadFile(["https://bad.example/file.bin", "https://good.example/file.bin"], path, new DownloadFileCheck(ActualSize: 4, CanUseExistingFile: false))
        ]);

        Assert.Equal(DownloadTaskState.Succeeded, result.State);
        Assert.Equal(["https://bad.example/file.bin", "https://good.example/file.bin"], client.RequestedUrls);
        Assert.Equal([4, 5, 6, 7], await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.pcldownload"));
    }

    [Fact]
    public async Task DownloadManagerUsesStreamingDownloadPath()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "file.bin");
        var client = new StreamingOnlyDownloadByteClient([4, 5, 6, 7]);
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());
        var snapshots = new List<DownloadTaskSnapshot>();
        manager.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);

        var result = await manager.DownloadAsync("stream", [
            new DownloadFile(["https://example/file.bin"], path, new DownloadFileCheck(ActualSize: 4))
        ]);

        Assert.Equal(DownloadTaskState.Succeeded, result.State);
        Assert.Equal(1, client.StreamingCalls);
        Assert.Equal(0, client.ByteArrayCalls);
        Assert.Equal([4, 5, 6, 7], await File.ReadAllBytesAsync(path));
        Assert.Contains(snapshots, snapshot => snapshot.State == DownloadTaskState.Running && snapshot.FinishedFiles == 0 && snapshot.BytesReceived == 4);
    }

    [Fact]
    public async Task DownloadManagerResumesFromPartialTempFileOnRetry()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "large.bin");
        var client = new ResumableDownloadByteClient([1, 2, 3, 4, 5, 6], firstChunkLength: 2);
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());
        var file = new DownloadFile(["https://example/large.bin"], path, new DownloadFileCheck(ActualSize: 6, CanUseExistingFile: false));

        var first = await manager.DownloadAsync("resume", [file]);

        Assert.Equal(DownloadTaskState.Failed, first.State);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".pcldownload"));
        Assert.Equal([1, 2], await File.ReadAllBytesAsync(path + ".pcldownload"));

        var second = await manager.DownloadAsync("resume", [file]);

        Assert.Equal(DownloadTaskState.Succeeded, second.State);
        Assert.Equal([1, 2, 3, 4, 5, 6], await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".pcldownload"));
        Assert.Equal(2, client.StreamingCalls);
        Assert.Equal(2, client.ResumeOffset);
    }

    [Fact]
    public async Task DownloadManagerHonorsOldPclThreadLimitSetting()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.ToolDownloadThread, 1);
        var client = new DelayedDownloadByteClient(TimeSpan.FromMilliseconds(500), releaseAfterConcurrentRequests: 2);
        client.Map("https://example/a.bin", [1]);
        client.Map("https://example/b.bin", [2]);
        client.Map("https://example/c.bin", [3]);
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService(), settings);

        var result = await manager.DownloadAsync("parallel", [
            new DownloadFile(["https://example/a.bin"], Path.Combine(temp.Path, "a.bin"), new DownloadFileCheck(ActualSize: 1)),
            new DownloadFile(["https://example/b.bin"], Path.Combine(temp.Path, "b.bin"), new DownloadFileCheck(ActualSize: 1)),
            new DownloadFile(["https://example/c.bin"], Path.Combine(temp.Path, "c.bin"), new DownloadFileCheck(ActualSize: 1))
        ]);

        Assert.Equal(DownloadTaskState.Succeeded, result.State);
        Assert.Equal(2, client.MaxConcurrentRequests);
    }

    [Theory]
    [InlineData(0, 104857)]
    [InlineData(14, 1572864)]
    [InlineData(15, 2097152)]
    [InlineData(31, 10485760)]
    [InlineData(32, 11534336)]
    [InlineData(41, 20971520)]
    [InlineData(42, -1)]
    public void DownloadManagerMapsOldPclSpeedLimitSlider(int value, long expectedBytesPerSecond)
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.ToolDownloadSpeed, value);
        var manager = new DownloadManagerService(new FakeDownloadByteClient(), new FileCheckService(new NullLoggerService()), new NullLoggerService(), settings);

        Assert.Equal(expectedBytesPerSecond, manager.GetSpeedLimitBytesPerSecond());
    }

    [Fact]
    public async Task DownloadManagerCancelsAllRunningAndClearsFinishedTasks()
    {
        using var temp = new TempDirectory();
        var client = new DelayedDownloadByteClient(TimeSpan.FromSeconds(20), releaseAfterConcurrentRequests: 2);
        client.Map("https://example/a.bin", [1]);
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());
        using var cancellation = new CancellationTokenSource();
        var download = manager.DownloadAsync("running", [
            new DownloadFile(["https://example/a.bin"], Path.Combine(temp.Path, "a.bin"), new DownloadFileCheck(ActualSize: 1))
        ], cancellation.Token);
        await client.WaitForFirstRequestAsync();

        var canceled = manager.CancelAllRunning();
        var result = await download;
        var cleared = manager.ClearFinished();

        Assert.Equal(1, canceled);
        Assert.Equal(DownloadTaskState.Canceled, result.State);
        Assert.Equal(1, cleared);
        Assert.Empty(manager.Tasks);
    }

    [Fact]
    public async Task DownloadManagerClearsFailedFinishedAndCanceledTasks()
    {
        using var temp = new TempDirectory();
        var client = new FakeDownloadByteClient();
        client.Map("https://example/ok.bin", [1]);
        var manager = new DownloadManagerService(client, new FileCheckService(new NullLoggerService()), new NullLoggerService());
        var failed = await manager.DownloadAsync("failed", [
            new DownloadFile(["https://example/missing.bin"], Path.Combine(temp.Path, "missing.bin"), new DownloadFileCheck())
        ]);
        var succeeded = await manager.DownloadAsync("succeeded", [
            new DownloadFile(["https://example/ok.bin"], Path.Combine(temp.Path, "ok.bin"), new DownloadFileCheck())
        ]);

        var cleared = manager.ClearFinished();

        Assert.Equal(DownloadTaskState.Failed, failed.State);
        Assert.Equal(DownloadTaskState.Succeeded, succeeded.State);
        Assert.Equal(2, cleared);
        Assert.Empty(manager.Tasks);
    }

    [Fact]
    public void AppExitGuardAllowsExitWhenNoDownloadTaskIsRunning()
    {
        var manager = new FakeDownloadManagerService();
        var prompts = new CapturePromptService(confirmationResult: false);
        var guard = new AppExitGuardService(manager, prompts, new NullLoggerService());

        var canExit = guard.CanExit();

        Assert.True(canExit);
        Assert.Equal(0, prompts.ConfirmCount);
    }

    [Fact]
    public void AppExitGuardBlocksExitWhenUserKeepsRunningDownloads()
    {
        var manager = new FakeDownloadManagerService();
        manager.AddSnapshot(new DownloadTaskSnapshot("资源下载", DownloadTaskState.Running, 2, 1, 1024, 0.5, "下载中")
        {
            CanCancel = true
        });
        var prompts = new CapturePromptService(confirmationResult: false);
        var guard = new AppExitGuardService(manager, prompts, new NullLoggerService());

        var canExit = guard.CanExit();

        Assert.False(canExit);
        Assert.Equal(1, prompts.ConfirmCount);
        Assert.Null(manager.CanceledName);
    }

    [Fact]
    public void AppExitGuardCancelsRunningDownloadsWhenUserConfirmsExit()
    {
        var manager = new FakeDownloadManagerService();
        manager.AddSnapshot(new DownloadTaskSnapshot("资源下载", DownloadTaskState.Running, 2, 1, 1024, 0.5, "下载中")
        {
            CanCancel = true
        });
        manager.AddSnapshot(new DownloadTaskSnapshot("已完成任务", DownloadTaskState.Succeeded, 1, 1, 1024, 1, "完成"));
        var prompts = new CapturePromptService(confirmationResult: true);
        var guard = new AppExitGuardService(manager, prompts, new NullLoggerService());

        var canExit = guard.CanExit();

        Assert.True(canExit);
        Assert.Equal(1, prompts.ConfirmCount);
        Assert.Equal("资源下载", manager.CanceledName);
    }

    [Fact]
    public async Task MinecraftClientDownloadReadsManifestAndCreatesInstallPlan()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var client = new FakeDownloadByteClient();
        client.Map("https://bmclapi2.bangbang93.com/mc/game/version_manifest.json", Encoding.UTF8.GetBytes("""
            {
              "versions": [
                { "id": "1.19.4", "type": "release", "releaseTime": "2023-03-14T12:00:00+00:00", "url": "https://piston-meta.mojang.com/v1/packages/old/1.19.4.json" },
                { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "url": "https://piston-meta.mojang.com/v1/packages/new/1.20.1.json" }
              ]
            }
            """));
        client.Map("https://piston-meta.mojang.com/v1/packages/new/1.20.1.json", Encoding.UTF8.GetBytes("""
            {
              "id": "1.20.1",
              "downloads": {
                "client": {
                  "url": "https://piston-data.mojang.com/v1/objects/client/1.20.1.jar",
                  "size": 2048,
                  "sha1": "clienthash"
                }
              },
              "assetIndex": {
                "id": "5",
                "url": "https://piston-meta.mojang.com/v1/packages/assets/5.json",
                "size": 128,
                "sha1": "indexhash"
              },
              "libraries": [
                {
                  "name": "com.mojang:authlib:5.0.47",
                  "downloads": {
                    "artifact": {
                      "path": "com/mojang/authlib/5.0.47/authlib-5.0.47.jar",
                      "url": "https://libraries.minecraft.net/com/mojang/authlib/5.0.47/authlib-5.0.47.jar",
                      "size": 4096,
                      "sha1": "authlibhash"
                    }
                  }
                },
                {
                  "name": "org.lwjgl:lwjgl:3.3.1",
                  "natives": {
                    "windows": "natives-windows-${arch}"
                  },
                  "downloads": {
                    "classifiers": {
                      "natives-windows-64": {
                        "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows-64.jar",
                        "url": "https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows-64.jar",
                        "size": 8192,
                        "sha1": "lwjglnative64"
                      },
                      "natives-windows-32": {
                        "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows-32.jar",
                        "url": "https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows-32.jar",
                        "size": 8192,
                        "sha1": "lwjglnative32"
                      }
                    }
                  }
                }
              ]
            }
            """));
        client.Map("https://piston-meta.mojang.com/v1/packages/assets/5.json", Encoding.UTF8.GetBytes("""
            {
              "objects": {
                "minecraft/sounds/random/click.ogg": {
                  "hash": "abcdef0123456789abcdef0123456789abcdef01",
                  "size": 42
                }
              }
            }
            """));
        var service = new MinecraftClientDownloadService(client, new DownloadSourceService(settings), settings);

        var versions = await service.GetVersionManifestAsync();
        var plan = await service.CreateInstallPlanAsync(temp.Path, "1.20.1", "MyInstance");

        Assert.Equal("1.20.1", versions[0].Id);
        Assert.Equal("BMCLAPI", versions[0].SourceName);
        Assert.Equal("MyInstance", plan.InstanceName);
        Assert.Equal(6, plan.Files.Count);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("versions", "MyInstance", "MyInstance.json"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("versions", "MyInstance", "MyInstance.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("assets", "indexes", "5.json"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("assets", "objects", "ab", "abcdef0123456789abcdef0123456789abcdef01"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "com", "mojang", "authlib", "5.0.47", "authlib-5.0.47.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.Contains(Path.Combine("libraries", "org", "lwjgl", "lwjgl", "3.3.1"), StringComparison.OrdinalIgnoreCase)
            && file.LocalPath.Contains("natives-windows", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CommunityResourceSearchParsesModrinthResults()
    {
        var client = new FakeDownloadByteClient();
        client.Map("https://api.modrinth.com/v2/search?limit=40&offset=0&index=relevance&query=sodium&facets=%5B%5B%22project_type%3Amod%22%5D%2C%5B%22versions%3A1.20.1%22%5D%2C%5B%22categories%3Afabric%22%5D%5D", Encoding.UTF8.GetBytes("""
        {
          "hits": [
            {
              "project_id": "AANobbMI",
              "slug": "sodium",
              "title": "Sodium",
              "description": "Modern rendering engine",
              "project_type": "mod",
              "icon_url": "https://example/icon.png",
              "downloads": 1000,
              "date_modified": "2024-01-02T03:04:05+00:00",
              "versions": ["1.20.1"],
              "categories": ["fabric", "optimization"],
              "loaders": ["fabric"]
            }
          ],
          "total_hits": 1
        }
        """));
        var service = new CommunityResourceSearchService(client, new NullLoggerService());

        var result = await service.SearchAsync(new CommunityResourceSearchQuery(CommunityResourceType.Mod, "sodium", "1.20.1", "fabric"));

        var project = Assert.Single(result.Projects);
        Assert.Equal("Sodium", project.Name);
        Assert.Equal(CommunityResourcePlatform.Modrinth, project.Platform);
        Assert.Equal(CommunityResourceType.Mod, project.Type);
        Assert.Equal("fabric", Assert.Single(project.Loaders));
        Assert.Equal(1, result.TotalHits);
    }

    [Fact]
    public async Task CommunityResourceSearchCombinesCurseForgeAndModrinthLikeOldPcl()
    {
        var client = new FakeDownloadByteClient();
        client.Map("https://api.curseforge.com/v1/mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize=40&index=0&classId=6&searchFilter=sodium&gameVersion=1.20.1&modLoaderType=4", Encoding.UTF8.GetBytes("""
        {
          "data": [
            {
              "id": 394468,
              "slug": "sodium",
              "name": "Sodium",
              "summary": "Modern rendering engine",
              "classId": 6,
              "downloadCount": 2000,
              "dateModified": "2024-02-02T03:04:05+00:00",
              "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/sodium" },
              "logo": { "thumbnailUrl": "https://example/cf.png" },
              "categories": [{ "slug": "performance", "name": "Performance" }],
              "latestFilesIndexes": [
                { "gameVersion": "1.20.1", "modLoader": "Fabric" }
              ]
            }
          ],
          "pagination": { "totalCount": 1 }
        }
        """));
        client.Map("https://api.modrinth.com/v2/search?limit=40&offset=0&index=relevance&query=sodium&facets=%5B%5B%22project_type%3Amod%22%5D%2C%5B%22versions%3A1.20.1%22%5D%2C%5B%22categories%3Afabric%22%5D%5D", Encoding.UTF8.GetBytes("""
        {
          "hits": [
            {
              "project_id": "AANobbMI",
              "slug": "sodium",
              "title": "Sodium",
              "description": "Modern rendering engine",
              "project_type": "mod",
              "downloads": 1000,
              "date_modified": "2024-01-02T03:04:05+00:00",
              "versions": ["1.20.1"],
              "categories": ["fabric"],
              "loaders": ["fabric"]
            }
          ],
          "total_hits": 1
        }
        """));
        var service = new CommunityResourceSearchService(client, new NullLoggerService());

        var result = await service.SearchAsync(new CommunityResourceSearchQuery(CommunityResourceType.Mod, "sodium", "1.20.1", "fabric"));

        Assert.Equal(2, result.Projects.Count);
        Assert.Equal(2, result.TotalHits);
        Assert.Equal("CurseForge + Modrinth", result.SourceMessage);
        Assert.Equal(CommunityResourcePlatform.CurseForge, result.Projects[0].Platform);
        Assert.Equal(CommunityResourcePlatform.Modrinth, result.Projects[1].Platform);
        Assert.Equal("fabric", Assert.Single(result.Projects[0].Loaders));
        Assert.Equal("1.20.1", Assert.Single(result.Projects[0].GameVersions));
    }

    [Fact]
    public async Task CommunityResourceSearchKeepsCurseForgeResultsWhenModrinthFails()
    {
        var client = new FakeDownloadByteClient();
        client.Map("https://api.curseforge.com/v1/mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize=40&index=0&classId=6&searchFilter=jei&gameVersion=1.20.1&modLoaderType=1", Encoding.UTF8.GetBytes("""
        {
          "data": [
            {
              "id": 238222,
              "slug": "jei",
              "name": "Just Enough Items",
              "summary": "Recipe viewer",
              "classId": 6,
              "downloadCount": 3000,
              "dateModified": "2024-02-02T03:04:05+00:00",
              "categories": [],
              "latestFilesIndexes": [
                { "gameVersion": "1.20.1", "modLoader": "Forge" }
              ]
            }
          ],
          "pagination": { "totalCount": 1 }
        }
        """));
        var service = new CommunityResourceSearchService(client, new NullLoggerService());

        var result = await service.SearchAsync(new CommunityResourceSearchQuery(CommunityResourceType.Mod, "jei", "1.20.1", "forge"));

        var project = Assert.Single(result.Projects);
        Assert.Equal(CommunityResourcePlatform.CurseForge, project.Platform);
        Assert.Contains("Modrinth 失败", result.SourceMessage);
        Assert.Equal(1, result.TotalHits);
    }

    [Fact]
    public async Task CommunityResourceSearchHidesQuiltLoaderWhenOldPclSettingIsEnabled()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.ToolDownloadIgnoreQuilt, true);
        var client = new FakeDownloadByteClient();
        client.Map("https://api.modrinth.com/v2/search?limit=40&offset=0&index=relevance&query=sodium&facets=%5B%5B%22project_type%3Amod%22%5D%2C%5B%22versions%3A1.20.1%22%5D%2C%5B%22categories%3Afabric%22%5D%5D", Encoding.UTF8.GetBytes("""
        {
          "hits": [
            {
              "project_id": "AANobbMI",
              "slug": "sodium",
              "title": "Sodium",
              "description": "Modern rendering engine",
              "project_type": "mod",
              "downloads": 1000,
              "date_modified": "2024-01-02T03:04:05+00:00",
              "versions": ["1.20.1"],
              "categories": ["fabric", "quilt", "optimization"],
              "loaders": ["fabric", "quilt"]
            }
          ],
          "total_hits": 1
        }
        """));
        var service = new CommunityResourceSearchService(client, new NullLoggerService(), settings);

        var result = await service.SearchAsync(new CommunityResourceSearchQuery(CommunityResourceType.Mod, "sodium", "1.20.1", "fabric"));

        var project = Assert.Single(result.Projects);
        Assert.Equal("fabric", Assert.Single(project.Loaders));
        Assert.DoesNotContain("quilt", project.LoaderSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommunityResourceVersionsParseModrinthFilesAndCreateDownloadFile()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var client = new FakeDownloadByteClient();
        var project = CreateSodiumProject();
        var url = CommunityResourceVersionService.BuildModrinthVersionsUrl(project.Id, project.Type, "1.20.1", "fabric");
        client.Map(url, Encoding.UTF8.GetBytes("""
        [
          {
            "id": "ver1",
            "name": "Sodium 0.5.8",
            "version_number": "mc1.20.1-0.5.8",
            "date_published": "2024-02-01T00:00:00+00:00",
            "game_versions": ["1.20.1"],
            "loaders": ["fabric"],
            "dependencies": [
              {
                "version_id": "fabric-api-ver",
                "project_id": "fabric-api",
                "dependency_type": "required"
              },
              {
                "project_id": "mod-menu",
                "dependency_type": "optional"
              }
            ],
            "files": [
              {
                "filename": "sodium.jar",
                "url": "https://cdn.modrinth.com/data/AANobbMI/versions/ver1/sodium.jar",
                "size": 12345,
                "primary": true,
                "hashes": {
                  "sha1": "0123456789abcdef0123456789abcdef01234567",
                  "sha512": "ignored"
                }
              }
            ]
          }
        ]
        """));
        var service = new CommunityResourceVersionService(client, new DownloadSourceService(settings), new NullLoggerService());

        var versions = await service.GetVersionsAsync(project, "1.20.1", "fabric");
        var version = Assert.Single(versions);
        Assert.NotNull(version.PrimaryFile);
        var file = version.PrimaryFile!;
        var download = service.CreateDownloadFile(project, version, file, temp.Path);

        Assert.Equal("Sodium 0.5.8", version.Name);
        Assert.Equal(1, version.RequiredDependencyCount);
        Assert.Equal("sodium.jar", file.FileName);
        Assert.EndsWith(Path.Combine("mods", "sodium.jar"), download.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", download.Check.Hash);
        Assert.StartsWith("https://cdn.modrinth.com", download.Sources[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(download.Sources, source => source.Contains("mod.mcimirror.top", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CommunityResourceVersionsHideQuiltLoaderWhenOldPclSettingIsEnabled()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.ToolDownloadIgnoreQuilt, true);
        var client = new FakeDownloadByteClient();
        var project = CreateSodiumProject();
        var url = CommunityResourceVersionService.BuildModrinthVersionsUrl(project.Id, project.Type, "1.20.1", "fabric");
        client.Map(url, Encoding.UTF8.GetBytes("""
        [
          {
            "id": "ver1",
            "name": "Sodium",
            "version_number": "1",
            "date_published": "2024-02-01T00:00:00+00:00",
            "game_versions": ["1.20.1"],
            "loaders": ["fabric", "quilt"],
            "dependencies": [],
            "files": [
              {
                "filename": "sodium.jar",
                "url": "https://cdn.modrinth.com/sodium.jar",
                "size": 100,
                "primary": true,
                "hashes": {
                  "sha1": "0123456789abcdef0123456789abcdef01234567"
                }
              }
            ]
          }
        ]
        """));
        var service = new CommunityResourceVersionService(client, new DownloadSourceService(settings), new NullLoggerService(), settings);

        var version = Assert.Single(await service.GetVersionsAsync(project, "1.20.1", "fabric"));

        Assert.Equal("fabric", Assert.Single(version.Loaders));
        Assert.DoesNotContain("quilt", version.VersionSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommunityResourceVersionsParseCurseForgeFilesAndCreateDownloadFile()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var client = new FakeDownloadByteClient();
        var project = CreateCurseForgeProject();
        var url = CommunityResourceVersionService.BuildCurseForgeVersionsUrl(project.Id, project.Type, "1.20.1", "forge");
        client.Map(url, Encoding.UTF8.GetBytes("""
        {
          "data": [
            {
              "id": 4578786,
              "modId": 322385,
              "displayName": "JEI 15.3.0",
              "fileName": "jei-1.20.1-forge.jar",
              "downloadUrl": "",
              "fileLength": 23456,
              "fileDate": "2024-03-01T00:00:00+00:00",
              "gameVersions": ["1.20.1", "Forge"],
              "hashes": [
                { "value": "0123456789abcdef0123456789abcdef01234567", "algo": 1 }
              ],
              "dependencies": [
                { "modId": 306612, "fileId": 4012345, "relationType": 3 }
              ]
            }
          ]
        }
        """));
        var service = new CommunityResourceVersionService(client, new DownloadSourceService(settings), new NullLoggerService());

        var version = Assert.Single(await service.GetVersionsAsync(project, "1.20.1", "forge"));
        var file = version.PrimaryFile!;
        var download = service.CreateDownloadFile(project, version, file, temp.Path);

        Assert.Equal(CommunityResourcePlatform.CurseForge, version.Platform);
        Assert.Equal("4578786", version.VersionId);
        Assert.Equal("1.20.1", Assert.Single(version.GameVersions));
        Assert.Equal("forge", Assert.Single(version.Loaders));
        Assert.Equal(1, version.RequiredDependencyCount);
        Assert.Equal("jei-1.20.1-forge.jar", file.FileName);
        Assert.Contains("edge.forgecdn.net/files/4578/786/jei-1.20.1-forge.jar", file.Url);
        Assert.EndsWith(Path.Combine("mods", "jei-1.20.1-forge.jar"), download.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", download.Check.Hash);
        Assert.Contains(download.Sources, source => source.Contains("mod.mcimirror.top", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CommunityResourceVersionsAddCurseForgeRequiredDependenciesToDownloadFiles()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var client = new FakeDownloadByteClient();
        var project = CreateCurseForgeProject();
        var rootUrl = CommunityResourceVersionService.BuildCurseForgeVersionsUrl(project.Id, project.Type, "1.20.1", "forge");
        client.Map(rootUrl, Encoding.UTF8.GetBytes("""
        {
          "data": [
            {
              "id": 4578786,
              "modId": 322385,
              "displayName": "JEI",
              "fileName": "jei.jar",
              "downloadUrl": "https://edge.forgecdn.net/files/4578/786/jei.jar",
              "fileLength": 100,
              "fileDate": "2024-03-01T00:00:00+00:00",
              "gameVersions": ["1.20.1", "Forge"],
              "hashes": [],
              "dependencies": [
                { "modId": 306612, "fileId": 4012345, "relationType": 3 },
                { "modId": 999999, "fileId": 4012999, "relationType": 2 }
              ]
            }
          ]
        }
        """));
        client.Map(CommunityResourceVersionService.BuildCurseForgeFileUrl("306612", "4012345"), Encoding.UTF8.GetBytes("""
        {
          "data": {
            "id": 4012345,
            "modId": 306612,
            "displayName": "Required Lib",
            "fileName": "required-lib.jar",
            "downloadUrl": "https://edge.forgecdn.net/files/4012/345/required-lib.jar",
            "fileLength": 200,
            "fileDate": "2024-02-01T00:00:00+00:00",
            "gameVersions": ["1.20.1", "Forge"],
            "hashes": [],
            "dependencies": []
          }
        }
        """));
        var service = new CommunityResourceVersionService(client, new DownloadSourceService(settings), new NullLoggerService());
        var version = Assert.Single(await service.GetVersionsAsync(project, "1.20.1", "forge"));

        var files = await service.CreateDownloadFilesWithDependenciesAsync(project, version, version.PrimaryFile!, temp.Path, "1.20.1", "forge");

        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.LocalPath.EndsWith(Path.Combine("mods", "jei.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, file => file.LocalPath.EndsWith(Path.Combine("mods", "required-lib.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(CommunityResourceVersionService.BuildCurseForgeFileUrl("306612", "4012345"), client.RequestedUrls);
        Assert.DoesNotContain(CommunityResourceVersionService.BuildCurseForgeFileUrl("999999", "4012999"), client.RequestedUrls);
    }

    [Fact]
    public async Task CommunityResourceVersionsAddRequiredDependenciesToDownloadFiles()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var client = new FakeDownloadByteClient();
        var project = CreateSodiumProject();
        var rootUrl = CommunityResourceVersionService.BuildModrinthVersionsUrl(project.Id, project.Type, "1.20.1", "fabric");
        client.Map(rootUrl, Encoding.UTF8.GetBytes("""
        [
          {
            "id": "sodium-ver",
            "project_id": "AANobbMI",
            "name": "Sodium",
            "version_number": "1",
            "date_published": "2024-02-01T00:00:00+00:00",
            "game_versions": ["1.20.1"],
            "loaders": ["fabric"],
            "dependencies": [
              { "version_id": "fabric-api-ver", "project_id": "fabric-api", "dependency_type": "required" },
              { "version_id": "optional-ver", "project_id": "optional", "dependency_type": "optional" }
            ],
            "files": [
              { "filename": "sodium.jar", "url": "https://cdn.modrinth.com/sodium.jar", "size": 100, "primary": true, "hashes": { "sha1": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } }
            ]
          }
        ]
        """));
        client.Map(CommunityResourceVersionService.BuildModrinthVersionUrl("fabric-api-ver"), Encoding.UTF8.GetBytes("""
        {
          "id": "fabric-api-ver",
          "project_id": "fabric-api",
          "name": "Fabric API",
          "version_number": "1",
          "date_published": "2024-01-01T00:00:00+00:00",
          "game_versions": ["1.20.1"],
          "loaders": ["fabric"],
          "dependencies": [],
          "files": [
            { "filename": "fabric-api.jar", "url": "https://cdn.modrinth.com/fabric-api.jar", "size": 200, "primary": true, "hashes": { "sha1": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } }
          ]
        }
        """));
        var service = new CommunityResourceVersionService(client, new DownloadSourceService(settings), new NullLoggerService());
        var version = (await service.GetVersionsAsync(project, "1.20.1", "fabric")).Single();

        var files = await service.CreateDownloadFilesWithDependenciesAsync(project, version, version.PrimaryFile!, temp.Path, "1.20.1", "fabric");

        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.LocalPath.EndsWith(Path.Combine("mods", "sodium.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, file => file.LocalPath.EndsWith(Path.Combine("mods", "fabric-api.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(client.RequestedUrls, url => url.Contains("optional-ver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CommunityResourceDownloadTargetsMatchResourceTypeAndSanitizeFileNames()
    {
        using var temp = new TempDirectory();
        var service = new CommunityResourceVersionService(
            new FakeDownloadByteClient(),
            new DownloadSourceService(new AppSettingsService(new TestAppPathService(temp.Path))),
            new NullLoggerService());
        var version = new CommunityResourceVersion(
            CommunityResourcePlatform.Modrinth,
            CommunityResourceType.ResourcePack,
            "project",
            "version",
            "Version",
            "1.0.0",
            DateTimeOffset.UtcNow,
            ["1.20.1"],
            [],
            [],
            []);

        var mod = service.CreateDownloadFile(CreateSodiumProject(), version, CreateResourceFile("mods/sodium.jar"), temp.Path);
        var resourcePack = service.CreateDownloadFile(CreateSodiumProject() with { Type = CommunityResourceType.ResourcePack }, version, CreateResourceFile("../Fancy.zip"), temp.Path);
        var shader = service.CreateDownloadFile(CreateSodiumProject() with { Type = CommunityResourceType.Shader }, version, CreateResourceFile("shader.zip"), temp.Path);
        var dataPack = service.CreateDownloadFile(CreateSodiumProject() with { Type = CommunityResourceType.DataPack }, version, CreateResourceFile("data.zip"), temp.Path);
        var modpack = service.CreateDownloadFile(CreateSodiumProject() with { Type = CommunityResourceType.ModPack }, version, CreateResourceFile("pack.mrpack"), temp.Path);

        Assert.EndsWith(Path.Combine("mods", "sodium.jar"), mod.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("resourcepacks", "Fancy.zip"), resourcePack.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("shaderpacks", "shader.zip"), shader.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("datapacks", "data.zip"), dataPack.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("PCL", "Downloads", "ModPacks", "pack.mrpack"), modpack.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", resourcePack.LocalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void CommunityResourceDownloadUsesOldPclModSourceAndTranslatedFileNameSettings()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.ToolDownloadMod, 0);
        settings.Set(AppSettingKeys.ToolDownloadTranslateV2, 2);
        var service = new CommunityResourceVersionService(
            new FakeDownloadByteClient(),
            new DownloadSourceService(settings),
            new NullLoggerService(),
            settings);
        var project = CreateSodiumProject() with { Name = "机械动力" };
        var version = new CommunityResourceVersion(
            CommunityResourcePlatform.Modrinth,
            CommunityResourceType.Mod,
            "project",
            "version",
            "Version",
            "1.0.0",
            DateTimeOffset.UtcNow,
            ["1.20.1"],
            ["fabric"],
            [],
            []);

        var file = service.CreateDownloadFile(project, version, CreateResourceFile("create~fabric.jar"), temp.Path);

        Assert.StartsWith("https://mod.mcimirror.top", file.Sources[0], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("mods", "机械动力-create-fabric.jar"), file.LocalPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FabricLoaderInstallWritesVersionJsonAndLibraryDownloads()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var client = new FakeDownloadByteClient();
        client.Map(FabricLoaderInstallService.BuildProfileUrl("1.20.1", "0.15.11"), Encoding.UTF8.GetBytes("""
        {
          "id": "fabric-loader-0.15.11-1.20.1",
          "inheritsFrom": "1.20.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "libraries": [
            { "name": "net.fabricmc:fabric-loader:0.15.11", "url": "https://maven.fabricmc.net/" },
            {
              "name": "net.fabricmc:intermediary:1.20.1",
              "downloads": {
                "artifact": {
                  "path": "net/fabricmc/intermediary/1.20.1/intermediary-1.20.1.jar",
                  "url": "https://maven.fabricmc.net/net/fabricmc/intermediary/1.20.1/intermediary-1.20.1.jar",
                  "sha1": "cccccccccccccccccccccccccccccccccccccccc",
                  "size": 321
                }
              }
            }
          ]
        }
        """));
        var service = new FabricLoaderInstallService(client, new DownloadSourceService(settings), new NullLoggerService());
        var instancePath = Path.Combine(temp.Path, "versions", "Test Pack");

        var plan = await service.CreateInstallPlanAsync(temp.Path, "Test Pack", instancePath, "1.20.1", "0.15.11");

        Assert.True(File.Exists(Path.Combine(instancePath, "Test Pack.json")));
        var json = await File.ReadAllTextAsync(Path.Combine(instancePath, "Test Pack.json"));
        Assert.Contains("\"id\": \"Test Pack\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"inheritsFrom\": \"1.20.1\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, plan.Files.Count);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "fabricmc", "fabric-loader", "0.15.11", "fabric-loader-0.15.11.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.Check.Hash == "cccccccccccccccccccccccccccccccccccccccc");
    }

    [Fact]
    public async Task QuiltLoaderInstallWritesVersionJsonAndLibraryDownloads()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var client = new FakeDownloadByteClient();
        client.Map(QuiltLoaderInstallService.BuildProfileUrl("1.20.1", "0.23.1"), Encoding.UTF8.GetBytes("""
        {
          "id": "quilt-loader-0.23.1-1.20.1",
          "inheritsFrom": "1.20.1",
          "mainClass": "org.quiltmc.loader.impl.launch.knot.KnotClient",
          "libraries": [
            { "name": "org.quiltmc:quilt-loader:0.23.1", "url": "https://maven.quiltmc.org/repository/release/" },
            { "name": "net.fabricmc:intermediary:1.20.1", "url": "https://maven.fabricmc.net/" }
          ]
        }
        """));
        var service = new QuiltLoaderInstallService(client, new DownloadSourceService(settings), new NullLoggerService());
        var instancePath = Path.Combine(temp.Path, "versions", "Quilt Pack");

        var plan = await service.CreateInstallPlanAsync(temp.Path, "Quilt Pack", instancePath, "1.20.1", "0.23.1");

        Assert.True(File.Exists(Path.Combine(instancePath, "Quilt Pack.json")));
        var json = await File.ReadAllTextAsync(Path.Combine(instancePath, "Quilt Pack.json"));
        Assert.Contains("\"id\": \"Quilt Pack\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"inheritsFrom\": \"1.20.1\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, plan.Files.Count);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "org", "quiltmc", "quilt-loader", "0.23.1", "quilt-loader-0.23.1.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "fabricmc", "intermediary", "1.20.1", "intermediary-1.20.1.jar"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ForgeLoaderInstallExtractsInstallerVersionJsonAndLibraries()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var client = new FakeDownloadByteClient();
        client.Map(ForgeLoaderInstallService.BuildInstallerUrl("1.20.1", "47.2.0"), CreateInstallerJarBytes("""
        {
          "id": "forge-1.20.1-47.2.0",
          "inheritsFrom": "1.20.1",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": [
            { "name": "net.minecraftforge:forge:1.20.1-47.2.0", "url": "https://maven.minecraftforge.net/" },
            {
              "name": "cpw.mods:bootstraplauncher:1.1.2",
              "downloads": {
                "artifact": {
                  "path": "cpw/mods/bootstraplauncher/1.1.2/bootstraplauncher-1.1.2.jar",
                  "url": "https://maven.minecraftforge.net/cpw/mods/bootstraplauncher/1.1.2/bootstraplauncher-1.1.2.jar",
                  "sha1": "dddddddddddddddddddddddddddddddddddddddd",
                  "size": 100
                }
              }
            }
          ]
        }
        """, """
        {
          "data": {
            "BINPATCH": { "client": "[net.minecraftforge:forge:1.20.1-47.2.0:clientdata@lzma]" },
            "MAPPINGS": { "client": "[de.oceanlabs.mcp:mcp_config:1.20.1-20230612.114412@zip]" },
            "ROOT": { "client": "{ROOT}" }
          },
          "libraries": [
            { "name": "net.minecraftforge:installertools:1.2.10", "url": "https://maven.minecraftforge.net/" }
          ],
          "processors": [
            {
              "sides": ["client"],
              "jar": "net.minecraftforge:installertools:1.2.10",
              "classpath": ["net.minecraftforge:installertools:1.2.10"],
              "args": ["--task", "BINPATCH", "--input", "{BINPATCH}", "--mappings", "{MAPPINGS}", "--root", "{ROOT}"],
              "outputs": { "client": "libraries/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0.jar" }
            },
            {
              "sides": ["server"],
              "jar": "server:only:1",
              "args": ["--server"]
            }
          ]
        }
        """));
        var service = new ForgeLoaderInstallService(client, new DownloadSourceService(settings), new NullLoggerService());
        var instancePath = Path.Combine(temp.Path, "versions", "Forge Pack");

        var plan = await service.CreateInstallPlanAsync(temp.Path, "Forge Pack", instancePath, "1.20.1", "47.2.0");

        Assert.True(File.Exists(Path.Combine(instancePath, "Forge Pack.json")));
        Assert.Contains("\"id\": \"Forge Pack\"", await File.ReadAllTextAsync(Path.Combine(instancePath, "Forge Pack.json")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "minecraftforge", "forge", "1.20.1-47.2.0", "forge-1.20.1-47.2.0-installer.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "minecraftforge", "forge", "1.20.1-47.2.0", "forge-1.20.1-47.2.0.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "minecraftforge", "installertools", "1.2.10", "installertools-1.2.10.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "minecraftforge", "forge", "1.20.1-47.2.0", "forge-1.20.1-47.2.0-clientdata.lzma"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "de", "oceanlabs", "mcp", "mcp_config", "1.20.1-20230612.114412", "mcp_config-1.20.1-20230612.114412.zip"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.Check.Hash == "dddddddddddddddddddddddddddddddddddddddd");
        var processor = Assert.Single(plan.Processors);
        Assert.Equal("net.minecraftforge:installertools:1.2.10", processor.JarCoordinate);
        Assert.Contains(Path.Combine(temp.Path, "libraries", "net", "minecraftforge", "forge", "1.20.1-47.2.0", "forge-1.20.1-47.2.0-clientdata.lzma"), processor.Arguments);
        Assert.Contains(Path.Combine(temp.Path, "libraries", "de", "oceanlabs", "mcp", "mcp_config", "1.20.1-20230612.114412", "mcp_config-1.20.1-20230612.114412.zip"), processor.Arguments);
        Assert.Contains(temp.Path, processor.Arguments);
        Assert.Equal(Path.Combine(temp.Path, "libraries", "net", "minecraftforge", "forge", "1.20.1-47.2.0", "forge-1.20.1-47.2.0.jar"), processor.Outputs["client"]);
    }

    [Fact]
    public async Task NeoForgeLoaderInstallExtractsInstallerVersionJsonAndLibraries()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var client = new FakeDownloadByteClient();
        client.Map(NeoForgeLoaderInstallService.BuildInstallerUrl("20.4.237"), CreateInstallerJarBytes("""
        {
          "id": "neoforge-20.4.237",
          "inheritsFrom": "1.20.4",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": [
            { "name": "net.neoforged:neoforge:20.4.237", "url": "https://maven.neoforged.net/releases/" }
          ]
        }
        """, """
        {
          "processors": [
            {
              "jar": "net.neoforged.installertools:binarypatcher:2.1.0",
              "classpath": ["net.neoforged.installertools:binarypatcher:2.1.0"],
              "args": ["--mc", "{MINECRAFT_VERSION}"]
            }
          ]
        }
        """));
        var service = new NeoForgeLoaderInstallService(client, new DownloadSourceService(settings), new NullLoggerService());
        var instancePath = Path.Combine(temp.Path, "versions", "NeoForge Pack");

        var plan = await service.CreateInstallPlanAsync(temp.Path, "NeoForge Pack", instancePath, "1.20.4", "20.4.237");

        Assert.True(File.Exists(Path.Combine(instancePath, "NeoForge Pack.json")));
        Assert.Contains("\"id\": \"NeoForge Pack\"", await File.ReadAllTextAsync(Path.Combine(instancePath, "NeoForge Pack.json")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "neoforged", "neoforge", "20.4.237", "neoforge-20.4.237-installer.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "neoforged", "neoforge", "20.4.237", "neoforge-20.4.237.jar"), StringComparison.OrdinalIgnoreCase));
        var processor = Assert.Single(plan.Processors);
        Assert.Equal("net.neoforged.installertools:binarypatcher:2.1.0", processor.JarCoordinate);
        Assert.Contains("1.20.4", processor.Arguments);
    }

    [Fact]
    public async Task ModpackInstallParsesMrpackAndCreatesInstallPlan()
    {
        using var temp = new TempDirectory();
        var packPath = Path.Combine(temp.Path, "test.mrpack");
        CreateTestMrpack(packPath);
        var minecraftDownload = new FakeMinecraftClientDownloadService
        {
            UseDefaultPlanFiles = true
        };
        var client = new FakeDownloadByteClient();
        client.Map(FabricLoaderInstallService.BuildProfileUrl("1.20.1", "0.15.11"), Encoding.UTF8.GetBytes("""
        {
          "id": "fabric-loader-0.15.11-1.20.1",
          "inheritsFrom": "1.20.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "libraries": [
            { "name": "net.fabricmc:fabric-loader:0.15.11", "url": "https://maven.fabricmc.net/" }
          ]
        }
        """));
        var downloadSources = new DownloadSourceService(new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata"))));
        var service = new ModpackInstallService(
            minecraftDownload,
            new FabricLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new QuiltLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new ForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new NeoForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            downloadSources,
            new NullLoggerService());

        var plan = await service.CreateModrinthInstallPlanAsync(packPath, temp.Path);

        Assert.Equal("Test Pack", plan.InstanceName);
        Assert.Equal("1.20.1", plan.MinecraftVersion);
        Assert.Equal("fabric-loader", plan.LoaderName);
        Assert.Equal("0.15.11", plan.LoaderVersion);
        Assert.Equal(1, plan.OverrideFileCount);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("mods", "example.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("versions", "1.20.1", "1.20.1.json"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "fabricmc", "fabric-loader", "0.15.11", "fabric-loader-0.15.11.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("server", "only.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(plan.InstancePath, "Test Pack.json")));
        Assert.Contains("\"inheritsFrom\": \"1.20.1\"", await File.ReadAllTextAsync(Path.Combine(plan.InstancePath, "Test Pack.json")), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(plan.InstancePath, "config", "example.cfg")));
        Assert.True(File.Exists(Path.Combine(plan.InstancePath, "modrinth.index.json")));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task ModpackInstallReportsUnsupportedLoaderWithoutMigrationPlaceholder()
    {
        using var temp = new TempDirectory();
        var packPath = Path.Combine(temp.Path, "rift.mrpack");
        CreateTestMrpack(packPath, "Rift Pack", "rift", "1.0.0");
        var minecraftDownload = new FakeMinecraftClientDownloadService
        {
            UseDefaultPlanFiles = true
        };
        var client = new FakeDownloadByteClient();
        var downloadSources = new DownloadSourceService(new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata"))));
        var service = new ModpackInstallService(
            minecraftDownload,
            new FabricLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new QuiltLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new ForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new NeoForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            downloadSources,
            new NullLoggerService());

        var plan = await service.CreateModrinthInstallPlanAsync(packPath, temp.Path);

        var warning = Assert.Single(plan.Warnings);
        Assert.Equal("rift", plan.LoaderName);
        Assert.Contains("暂不支持自动安装", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("后续迁移", warning, StringComparison.Ordinal);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("versions", "Rift Pack", "Rift Pack.json"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ModpackInstallParsesCurseForgeZipAndCreatesInstallPlan()
    {
        using var temp = new TempDirectory();
        var packPath = Path.Combine(temp.Path, "curseforge.zip");
        using (var archive = ZipFile.Open(packPath, ZipArchiveMode.Create))
        {
            AddZipText(archive, "manifest.json", """
            {
              "minecraft": {
                "version": "1.20.1",
                "modLoaders": [
                  { "id": "fabric-0.15.11", "primary": true }
                ]
              },
              "manifestType": "minecraftModpack",
              "manifestVersion": 1,
              "name": "Curse Pack",
              "version": "1.0.0",
              "author": "PCL",
              "files": [
                { "projectID": 306612, "fileID": 4012345, "required": true },
                { "projectID": 999999, "fileID": 4012999, "required": false }
              ],
              "overrides": "overrides"
            }
            """);
            AddZipText(archive, "overrides/config/example.cfg", "enabled=true");
        }

        var minecraftDownload = new FakeMinecraftClientDownloadService
        {
            UseDefaultPlanFiles = true
        };
        var client = new FakeDownloadByteClient();
        client.Map(FabricLoaderInstallService.BuildProfileUrl("1.20.1", "0.15.11"), Encoding.UTF8.GetBytes("""
        {
          "id": "fabric-loader-0.15.11-1.20.1",
          "inheritsFrom": "1.20.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "libraries": [
            { "name": "net.fabricmc:fabric-loader:0.15.11", "url": "https://maven.fabricmc.net/" }
          ]
        }
        """));
        client.Map(CommunityResourceVersionService.BuildCurseForgeFileUrl("306612", "4012345"), Encoding.UTF8.GetBytes("""
        {
          "data": {
            "id": 4012345,
            "modId": 306612,
            "displayName": "Required Lib",
            "fileName": "required-lib.jar",
            "downloadUrl": "",
            "fileLength": 200,
            "fileDate": "2024-02-01T00:00:00+00:00",
            "gameVersions": ["1.20.1", "Fabric"],
            "hashes": [
              { "value": "0123456789abcdef0123456789abcdef01234567", "algo": 1 }
            ],
            "dependencies": []
          }
        }
        """));
        var downloadSources = new DownloadSourceService(new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata"))));
        var service = new ModpackInstallService(
            minecraftDownload,
            new FabricLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new QuiltLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new ForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new NeoForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            downloadSources,
            new NullLoggerService(),
            client);

        var plan = await service.CreateModrinthInstallPlanAsync(packPath, temp.Path);

        Assert.Equal("Curse Pack", plan.InstanceName);
        Assert.Equal("1.20.1", plan.MinecraftVersion);
        Assert.Equal("fabric-loader", plan.LoaderName);
        Assert.Equal("0.15.11", plan.LoaderVersion);
        Assert.Equal(1, plan.OverrideFileCount);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("mods", "required-lib.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "fabricmc", "fabric-loader", "0.15.11", "fabric-loader-0.15.11.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(CommunityResourceVersionService.BuildCurseForgeFileUrl("999999", "4012999"), client.RequestedUrls);
        var modFile = Assert.Single(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("mods", "required-lib.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains("edge.forgecdn.net/files/4012/345/required-lib.jar", modFile.Sources[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", modFile.Check.Hash);
        Assert.True(File.Exists(Path.Combine(plan.InstancePath, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(plan.InstancePath, "config", "example.cfg")));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task ModpackInstallInstallsQuiltLoaderProfile()
    {
        using var temp = new TempDirectory();
        var packPath = Path.Combine(temp.Path, "quilt.mrpack");
        CreateTestMrpack(packPath, "Quilt Pack", "quilt-loader", "0.23.1");
        var minecraftDownload = new FakeMinecraftClientDownloadService
        {
            UseDefaultPlanFiles = true
        };
        var client = new FakeDownloadByteClient();
        client.Map(QuiltLoaderInstallService.BuildProfileUrl("1.20.1", "0.23.1"), Encoding.UTF8.GetBytes("""
        {
          "id": "quilt-loader-0.23.1-1.20.1",
          "inheritsFrom": "1.20.1",
          "mainClass": "org.quiltmc.loader.impl.launch.knot.KnotClient",
          "libraries": [
            { "name": "org.quiltmc:quilt-loader:0.23.1", "url": "https://maven.quiltmc.org/repository/release/" }
          ]
        }
        """));
        var downloadSources = new DownloadSourceService(new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata"))));
        var service = new ModpackInstallService(
            minecraftDownload,
            new FabricLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new QuiltLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new ForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new NeoForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            downloadSources,
            new NullLoggerService());

        var plan = await service.CreateModrinthInstallPlanAsync(packPath, temp.Path);

        Assert.Equal("quilt-loader", plan.LoaderName);
        Assert.Equal("0.23.1", plan.LoaderVersion);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "org", "quiltmc", "quilt-loader", "0.23.1", "quilt-loader-0.23.1.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("versions", "1.20.1", "1.20.1.json"), StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(plan.InstancePath, "Quilt Pack.json")));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task ModpackInstallInstallsForgeInstallerProfile()
    {
        using var temp = new TempDirectory();
        var packPath = Path.Combine(temp.Path, "forge.mrpack");
        CreateTestMrpack(packPath, "Forge Pack", "forge", "47.2.0");
        var minecraftDownload = new FakeMinecraftClientDownloadService
        {
            UseDefaultPlanFiles = true
        };
        var client = new FakeDownloadByteClient();
        client.Map(ForgeLoaderInstallService.BuildInstallerUrl("1.20.1", "47.2.0"), CreateInstallerJarBytes("""
        {
          "id": "forge-1.20.1-47.2.0",
          "inheritsFrom": "1.20.1",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": [
            { "name": "net.minecraftforge:forge:1.20.1-47.2.0", "url": "https://maven.minecraftforge.net/" }
          ]
        }
        """, """
        {
          "processors": [
            {
              "jar": "net.minecraftforge:installertools:1.2.10",
              "args": ["--mc", "{MINECRAFT_VERSION}"]
            }
          ]
        }
        """));
        var downloadSources = new DownloadSourceService(new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata"))));
        var service = new ModpackInstallService(
            minecraftDownload,
            new FabricLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new QuiltLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new ForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new NeoForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            downloadSources,
            new NullLoggerService());

        var plan = await service.CreateModrinthInstallPlanAsync(packPath, temp.Path);

        Assert.Equal("forge", plan.LoaderName);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "minecraftforge", "forge", "1.20.1-47.2.0", "forge-1.20.1-47.2.0.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(plan.InstancePath, "Forge Pack.json")));
        Assert.Single(plan.ProcessorSteps);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task ModpackInstallInstallsNeoForgeInstallerProfile()
    {
        using var temp = new TempDirectory();
        var packPath = Path.Combine(temp.Path, "neoforge.mrpack");
        CreateTestMrpack(packPath, "NeoForge Pack", "neoforge", "20.4.237");
        var minecraftDownload = new FakeMinecraftClientDownloadService
        {
            UseDefaultPlanFiles = true
        };
        var client = new FakeDownloadByteClient();
        client.Map(NeoForgeLoaderInstallService.BuildInstallerUrl("20.4.237"), CreateInstallerJarBytes("""
        {
          "id": "neoforge-20.4.237",
          "inheritsFrom": "1.20.1",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": [
            { "name": "net.neoforged:neoforge:20.4.237", "url": "https://maven.neoforged.net/releases/" }
          ]
        }
        """, """
        {
          "processors": [
            {
              "jar": "net.neoforged.installertools:binarypatcher:2.1.0",
              "args": ["--mc", "{MINECRAFT_VERSION}"]
            }
          ]
        }
        """));
        var downloadSources = new DownloadSourceService(new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata"))));
        var service = new ModpackInstallService(
            minecraftDownload,
            new FabricLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new QuiltLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new ForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            new NeoForgeLoaderInstallService(client, downloadSources, new NullLoggerService()),
            downloadSources,
            new NullLoggerService());

        var plan = await service.CreateModrinthInstallPlanAsync(packPath, temp.Path);

        Assert.Equal("neoforge", plan.LoaderName);
        Assert.Contains(plan.Files, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "neoforged", "neoforge", "20.4.237", "neoforge-20.4.237.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(plan.InstancePath, "NeoForge Pack.json")));
        Assert.Single(plan.ProcessorSteps);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task LoaderProcessorRunnerBuildsClasspathAndRunsManifestMainClass()
    {
        using var temp = new TempDirectory();
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        WriteSmallFile(javaPath);
        var processorCoordinate = "net.minecraftforge:installertools:1.2.10";
        var dependencyCoordinate = "org.ow2.asm:asm:9.6";
        var processorPath = LoaderProcessorRunner.ResolveLibraryPath(temp.Path, processorCoordinate);
        var dependencyPath = LoaderProcessorRunner.ResolveLibraryPath(temp.Path, dependencyCoordinate);
        CreateProcessorJar(processorPath, "com.example.Processor");
        WriteSmallFile(dependencyPath);
        var outputPath = Path.Combine(temp.Path, "libraries", "net", "minecraftforge", "forge", "out.jar");
        WriteSmallFile(outputPath);
        var launcher = new FakeProcessLauncher();
        var runner = new LoaderProcessorRunner(launcher, new NullLoggerService());

        var result = await runner.RunAsync(temp.Path, javaPath, [
            new LoaderProcessorStep(
                processorCoordinate,
                [dependencyCoordinate],
                ["--input", "client.lzma"],
                new Dictionary<string, string> { ["client"] = outputPath },
                true)
        ]);

        Assert.True(result.Success);
        Assert.Equal(1, launcher.StartCount);
        Assert.Equal(javaPath, launcher.LastStartInfo?.FileName);
        Assert.Equal(temp.Path, launcher.LastStartInfo?.WorkingDirectory);
        var args = launcher.LastStartInfo!.ArgumentList.ToArray();
        Assert.Contains("-cp", args);
        var classpath = args[Array.IndexOf(args, "-cp") + 1];
        Assert.Contains(processorPath, classpath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dependencyPath, classpath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("com.example.Processor", args);
        Assert.Contains("--input", args);
        Assert.Equal([processorCoordinate], result.ExecutedProcessors);
    }

    [Fact]
    public void LoaderProcessorRunnerResolvesBracketedMavenCoordinate()
    {
        using var temp = new TempDirectory();

        var path = LoaderProcessorRunner.ResolveLibraryPath(temp.Path, "[net.minecraftforge:forge:1.20.1-47.2.0:clientdata@lzma]");

        Assert.EndsWith(Path.Combine("libraries", "net", "minecraftforge", "forge", "1.20.1-47.2.0", "forge-1.20.1-47.2.0-clientdata.lzma"), path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoaderProcessorRunnerReportsMissingInputsWithoutStarting()
    {
        using var temp = new TempDirectory();
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        WriteSmallFile(javaPath);
        var launcher = new FakeProcessLauncher();
        var runner = new LoaderProcessorRunner(launcher, new NullLoggerService());

        var result = await runner.RunAsync(temp.Path, javaPath, [
            new LoaderProcessorStep(
                "net.minecraftforge:missing:1.0.0",
                ["org.example:also-missing:1.0.0"],
                [],
                new Dictionary<string, string>(),
                true)
        ]);

        Assert.False(result.Success);
        Assert.Equal(0, launcher.StartCount);
        Assert.Equal(["net.minecraftforge:missing:1.0.0"], result.SkippedProcessors);
        Assert.Contains(result.MissingInputs, path => path.EndsWith(Path.Combine("net", "minecraftforge", "missing", "1.0.0", "missing-1.0.0.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MissingInputs, path => path.EndsWith(Path.Combine("org", "example", "also-missing", "1.0.0", "also-missing-1.0.0.jar"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoaderProcessorRunnerFailsWhenDeclaredOutputIsMissing()
    {
        using var temp = new TempDirectory();
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        WriteSmallFile(javaPath);
        var processorCoordinate = "net.minecraftforge:installertools:1.2.10";
        CreateProcessorJar(LoaderProcessorRunner.ResolveLibraryPath(temp.Path, processorCoordinate), "com.example.Processor");
        var missingOutput = Path.Combine(temp.Path, "versions", "1.20.1", "patched.jar");
        var runner = new LoaderProcessorRunner(new FakeProcessLauncher(), new NullLoggerService());

        var result = await runner.RunAsync(temp.Path, javaPath, [
            new LoaderProcessorStep(
                processorCoordinate,
                [],
                ["--patch"],
                new Dictionary<string, string> { ["client"] = missingOutput },
                true)
        ]);

        Assert.False(result.Success);
        Assert.Contains(missingOutput, result.MissingOutputs);
        Assert.Contains(result.FailedProcessors, item => item.Contains("missing outputs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DownloadPageViewModelExposesOldStyleSections()
    {
        using var temp = new TempDirectory();
        var viewModel = CreateDownloadPageViewModel(temp.Path, new FakeMinecraftClientDownloadService(), new FakeDownloadManagerService());

        Assert.Equal(["原版游戏", "Mod", "整合包", "数据包", "资源包", "光影包", "下载管理"], viewModel.Sections.Select(section => section.Title).ToArray());
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);

        Assert.True(viewModel.IsResourceSection);
        Assert.False(viewModel.IsInstallSection);
        Assert.Contains("Mod", viewModel.ResourcePanelTitle);
        Assert.Contains("必需依赖解析", viewModel.ResourcePanelMessage);

        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.ModPack);
        Assert.Contains(".mrpack", viewModel.ResourcePanelMessage);
        Assert.Contains("processors", viewModel.ResourcePanelMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadPageViewModelRefreshesVanillaVersions()
    {
        using var temp = new TempDirectory();
        var download = new FakeMinecraftClientDownloadService
        {
            Versions =
            [
                new MinecraftRemoteVersion("1.20.1", "release", DateTimeOffset.Parse("2023-06-12T12:00:00+00:00"), "https://example/1.20.1.json", "test")
            ]
        };
        var viewModel = CreateDownloadPageViewModel(temp.Path, download, new FakeDownloadManagerService());

        await viewModel.RefreshVersionsAsync();

        Assert.Equal(1, viewModel.VersionCount);
        Assert.Equal("1.20.1", viewModel.SelectedVersion?.Id);
        Assert.Equal("1.20.1", viewModel.InstanceName);
    }

    [Fact]
    public async Task DownloadPageViewModelLoadsVanillaVersionsOnceWhenOpened()
    {
        using var temp = new TempDirectory();
        var download = new FakeMinecraftClientDownloadService
        {
            Versions =
            [
                new MinecraftRemoteVersion("1.20.4", "release", DateTimeOffset.Parse("2023-12-07T12:00:00+00:00"), "https://example/1.20.4.json", "test")
            ]
        };
        var viewModel = CreateDownloadPageViewModel(temp.Path, download, new FakeDownloadManagerService());

        await viewModel.OnNavigatedToAsync();
        await viewModel.OnNavigatedToAsync();

        Assert.Equal(1, download.GetVersionManifestCount);
        Assert.Equal("1.20.4", viewModel.SelectedVersion?.Id);
    }

    [Fact]
    public async Task DownloadPageViewModelFiltersVanillaVersionsByOldStyleCategory()
    {
        using var temp = new TempDirectory();
        var download = new FakeMinecraftClientDownloadService
        {
            Versions =
            [
                new MinecraftRemoteVersion("1.20.4", "release", DateTimeOffset.Parse("2023-12-07T12:00:00+00:00"), "https://example/1.20.4.json", "test"),
                new MinecraftRemoteVersion("24w01a", "snapshot", DateTimeOffset.Parse("2024-01-03T12:00:00+00:00"), "https://example/24w01a.json", "test"),
                new MinecraftRemoteVersion("a1.2.6", "old_alpha", DateTimeOffset.Parse("2010-12-03T12:00:00+00:00"), "https://example/a1.2.6.json", "test")
            ]
        };
        var viewModel = CreateDownloadPageViewModel(temp.Path, download, new FakeDownloadManagerService());

        await viewModel.RefreshVersionsAsync();

        Assert.Equal(
            ["全部版本:3", "正式版:1", "快照版:1", "远古版:1"],
            viewModel.VersionCategoryItems.Select(item => $"{item.Title}:{item.Count}").ToArray());
        viewModel.SelectedVersionCategory = "正式版";

        Assert.Equal(["1.20.4"], viewModel.Versions.Select(version => version.Id).ToArray());

        viewModel.SelectedVersionCategory = "快照版";
        Assert.Equal(["24w01a"], viewModel.Versions.Select(version => version.Id).ToArray());

        viewModel.SelectedVersionCategory = "远古版";
        Assert.Equal(["a1.2.6"], viewModel.Versions.Select(version => version.Id).ToArray());
    }

    [Fact]
    public async Task DownloadPageViewModelInstallsVanillaIntoSelectedMinecraftFolder()
    {
        using var temp = new TempDirectory();
        var firstRoot = Path.Combine(temp.Path, "First");
        var secondRoot = Path.Combine(temp.Path, "Second");
        Directory.CreateDirectory(Path.Combine(firstRoot, "versions"));
        Directory.CreateDirectory(Path.Combine(secondRoot, "versions"));
        var download = new FakeMinecraftClientDownloadService
        {
            Versions =
            [
                new MinecraftRemoteVersion("1.20.1", "release", DateTimeOffset.Parse("2023-06-12T12:00:00+00:00"), "https://example/1.20.1.json", "test")
            ],
            UseDefaultPlanFiles = true
        };
        var manager = new FakeDownloadManagerService();
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, firstRoot);
        settings.Set(AppSettingKeys.LaunchFolders, $"First>{firstRoot}|Second>{secondRoot}");
        var viewModel = new DownloadPageViewModel(
            download,
            manager,
            new FakeCommunityResourceSearchService(),
            new FakeCommunityResourceVersionService(),
            new FakeModpackInstallService(),
            new FakeLoaderProcessorRunner(),
            settings,
            new FakeMinecraftDiscoveryService(firstRoot),
            new NullFileDialogService(),
            new NullLoggerService());

        viewModel.SelectedMinecraftRootFolder = viewModel.MinecraftRootFolders.Single(folder => folder.Path == Path.GetFullPath(secondRoot));
        await viewModel.RefreshVersionsAsync();
        await viewModel.InstallSelectedVersionAsync();

        Assert.Equal(Path.GetFullPath(secondRoot), download.LastMinecraftRootPath);
        Assert.All(manager.LastFiles, file => Assert.StartsWith(Path.GetFullPath(secondRoot), file.LocalPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DownloadPageViewModelSkipsExistingVanillaInstallFiles()
    {
        using var temp = new TempDirectory();
        var versionFolder = Path.Combine(temp.Path, "versions", "1.20.1");
        Directory.CreateDirectory(versionFolder);
        await File.WriteAllTextAsync(Path.Combine(versionFolder, "1.20.1.json"), """{"id":"1.20.1"}""");
        await File.WriteAllTextAsync(Path.Combine(versionFolder, "1.20.1.jar"), "jar");
        var download = new FakeMinecraftClientDownloadService
        {
            Versions =
            [
                new MinecraftRemoteVersion("1.20.1", "release", DateTimeOffset.Parse("2023-06-12T12:00:00+00:00"), "https://example/1.20.1.json", "test")
            ],
            UseDefaultPlanFiles = true
        };
        var manager = new FakeDownloadManagerService();
        var viewModel = CreateDownloadPageViewModel(temp.Path, download, manager);

        await viewModel.RefreshVersionsAsync();
        await viewModel.InstallSelectedVersionAsync();

        Assert.Empty(manager.LastFiles);
        Assert.Contains("原版安装文件已存在", viewModel.StatusMessage);
        Assert.Contains("当前版本", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadPageViewModelSkipsQueuedVanillaInstallFiles()
    {
        using var temp = new TempDirectory();
        var queuedJsonPath = Path.Combine(temp.Path, "versions", "1.20.1", "1.20.1.json");
        var download = new FakeMinecraftClientDownloadService
        {
            Versions =
            [
                new MinecraftRemoteVersion("1.20.1", "release", DateTimeOffset.Parse("2023-06-12T12:00:00+00:00"), "https://example/1.20.1.json", "test")
            ],
            UseDefaultPlanFiles = true
        };
        var manager = new FakeDownloadManagerService();
        manager.AddSnapshot(new DownloadTaskSnapshot("Minecraft 1.20.1 下载", DownloadTaskState.Running, 2, 0, 0, 0, "下载中")
        {
            LocalPaths = [queuedJsonPath],
            PrimaryLocalPath = queuedJsonPath,
            CanCancel = true
        });
        var viewModel = CreateDownloadPageViewModel(temp.Path, download, manager);

        await viewModel.RefreshVersionsAsync();
        await viewModel.InstallSelectedVersionAsync();

        var onlyFile = Assert.Single(manager.LastFiles);
        Assert.EndsWith(Path.Combine("versions", "1.20.1", "1.20.1.jar"), onlyFile.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("队列中任务", viewModel.StatusMessage);
        Assert.Contains("当前版本", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadPageViewModelSyncsMinecraftRootPathWhenNavigatedBackAfterOtherPageChange()
    {
        using var temp = new TempDirectory();
        var firstRoot = Path.Combine(temp.Path, "First");
        var secondRoot = Path.Combine(temp.Path, "Second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, firstRoot);
        settings.Set(AppSettingKeys.LaunchFolders, $"First>{firstRoot}|Second>{secondRoot}");
        var viewModel = new DownloadPageViewModel(
            new FakeMinecraftClientDownloadService(),
            new FakeDownloadManagerService(),
            new FakeCommunityResourceSearchService(),
            new FakeCommunityResourceVersionService(),
            new FakeModpackInstallService(),
            new FakeLoaderProcessorRunner(),
            settings,
            new FakeMinecraftDiscoveryService(firstRoot),
            new NullFileDialogService(),
            new NullLoggerService());

        Assert.Equal(Path.GetFullPath(firstRoot), viewModel.MinecraftRootPath);

        settings.Set(AppSettingKeys.MinecraftRootPath, secondRoot);
        await viewModel.OnNavigatedToAsync();

        Assert.Equal(Path.GetFullPath(secondRoot), viewModel.MinecraftRootPath);
        Assert.Equal(Path.GetFullPath(secondRoot), viewModel.SelectedMinecraftRootFolder?.Path);
        Assert.Contains(viewModel.MinecraftRootFolders, folder => folder.Path == Path.GetFullPath(secondRoot));
    }

    [Fact]
    public async Task DownloadPageViewModelSelectsInstalledVanillaAndClearsPclInstanceCache()
    {
        using var temp = new TempDirectory();
        File.WriteAllLines(Path.Combine(temp.Path, "PCL.ini"), ["InstanceCache:old-cache", "Other:keep"]);
        var download = new FakeMinecraftClientDownloadService
        {
            Versions =
            [
                new MinecraftRemoteVersion("1.20.1", "release", DateTimeOffset.Parse("2023-06-12T12:00:00+00:00"), "https://example/1.20.1.json", "test")
            ],
            UseDefaultPlanFiles = true
        };
        var viewModel = CreateDownloadPageViewModel(temp.Path, download, new FakeDownloadManagerService());

        await viewModel.RefreshVersionsAsync();
        viewModel.InstanceName = "My 1.20.1";
        await viewModel.InstallSelectedVersionAsync();

        var ini = File.ReadAllText(Path.Combine(temp.Path, "PCL.ini"));
        Assert.Contains("Version:My 1.20.1", ini);
        Assert.Contains("InstanceCache:", ini);
        Assert.DoesNotContain("InstanceCache:old-cache", ini);
        Assert.Contains("Other:keep", ini);
        Assert.Contains("当前版本", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadPageViewModelInstallsLoaderAsSeparateInstanceWithVanillaDependencies()
    {
        using var temp = new TempDirectory();
        var download = new FakeMinecraftClientDownloadService
        {
            Versions =
            [
                new MinecraftRemoteVersion("1.20.1", "release", DateTimeOffset.Parse("2023-06-12T12:00:00+00:00"), "https://example/1.20.1.json", "test")
            ],
            UseDefaultPlanFiles = true
        };
        var manager = new FakeDownloadManagerService();
        var fabric = new FakeFabricLoaderInstallService();
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            download,
            manager,
            fabricLoaderInstall: fabric);

        await viewModel.RefreshVersionsAsync();
        viewModel.SelectedLoaderKind = "Fabric";
        viewModel.LoaderVersion = "0.15.11";
        await viewModel.InstallSelectedLoaderAsync();

        var expectedInstance = "1.20.1-Fabric-0.15.11";
        Assert.Equal(temp.Path, download.LastMinecraftRootPath);
        Assert.Equal(expectedInstance, fabric.LastInstanceName);
        Assert.Equal("1.20.1", fabric.LastMinecraftVersion);
        Assert.Equal("0.15.11", fabric.LastLoaderVersion);
        Assert.Contains(manager.LastFiles, file => file.LocalPath.EndsWith(Path.Combine("versions", "1.20.1", "1.20.1.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(manager.LastFiles, file => file.LocalPath.EndsWith(Path.Combine("libraries", "net", "fabricmc", "fabric-loader", "0.15.11", "fabric-loader-0.15.11.jar"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Version:" + expectedInstance, File.ReadAllText(Path.Combine(temp.Path, "PCL.ini")));
        Assert.Contains("已安装 Fabric 0.15.11", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadPageViewModelRefreshesLoaderVersionsAndUsesSelection()
    {
        using var temp = new TempDirectory();
        var download = new FakeMinecraftClientDownloadService
        {
            Versions =
            [
                new MinecraftRemoteVersion("1.20.1", "release", DateTimeOffset.Parse("2023-06-12T12:00:00+00:00"), "https://example/1.20.1.json", "test")
            ]
        };
        var loaderVersions = new FakeLoaderVersionService
        {
            Versions =
            [
                new LoaderVersionOption("Fabric", "0.15.11", true, "Fabric Meta"),
                new LoaderVersionOption("Fabric", "0.15.10", false, "Fabric Meta")
            ]
        };
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            download,
            new FakeDownloadManagerService(),
            loaderVersions: loaderVersions);

        await viewModel.RefreshVersionsAsync();
        viewModel.SelectedLoaderKind = "Fabric";
        await viewModel.RefreshLoaderVersionsAsync();
        viewModel.SelectedLoaderVersion = viewModel.LoaderVersions[1];

        Assert.Equal("Fabric", loaderVersions.LastLoaderKind);
        Assert.Equal("1.20.1", loaderVersions.LastMinecraftVersion);
        Assert.Equal(2, viewModel.LoaderVersionCount);
        Assert.Equal("0.15.10", viewModel.LoaderVersion);
        Assert.Contains("0.15.10", viewModel.LoaderInstancePreview);
    }

    [Fact]
    public async Task DownloadPageViewModelManagesSelectedDownloadTask()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var folders = new CaptureFolderOpenService();
        var file = new DownloadFile(
            ["https://example/file.jar"],
            Path.Combine(temp.Path, "mods", "file.jar"),
            new DownloadFileCheck(MinSize: 1));
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            folders: folders);
        manager.AddSnapshot(new DownloadTaskSnapshot("资源下载", DownloadTaskState.Running, 1, 0, 0, 0, "下载中")
        {
            CanCancel = true,
            PrimaryLocalPath = file.LocalPath
        }, [file]);

        Assert.True(viewModel.HasSelectedDownloadTask);
        Assert.Equal("运行中", viewModel.SelectedDownloadTaskStateText);
        Assert.Equal("0.00 %", viewModel.SelectedDownloadTaskProgressText);
        Assert.Equal("文件：0 / 1", viewModel.SelectedDownloadTaskFileText);
        Assert.Equal("已接收：0 B", viewModel.SelectedDownloadTaskBytesText);
        Assert.Contains(file.LocalPath, viewModel.SelectedDownloadTaskPathText);
        Assert.Contains("下载中", viewModel.SelectedDownloadTaskMessage);

        viewModel.CancelSelectedDownloadTaskCommand.Execute(null);
        viewModel.OpenSelectedDownloadTaskFolderCommand.Execute(null);

        Assert.Equal("资源下载", manager.CanceledName);
        Assert.Equal(Path.Combine(temp.Path, "mods"), folders.LastOpenedFolder);

        manager.AddSnapshot(new DownloadTaskSnapshot("资源下载", DownloadTaskState.Failed, 1, 0, 0, 0, "失败")
        {
            CanRetry = true,
            PrimaryLocalPath = file.LocalPath
        }, [file]);
        Assert.Equal("失败", viewModel.SelectedDownloadTaskStateText);
        Assert.Contains("失败", viewModel.SelectedDownloadTaskMessage);

        await viewModel.RetrySelectedDownloadTaskAsync();

        Assert.Equal("已完成", viewModel.SelectedDownloadTaskStateText);
        Assert.Equal(1, manager.RetryCount);
        Assert.Contains("已重试下载任务", viewModel.StatusMessage);
    }

    [Fact]
    public void DownloadPageViewModelKeepsSelectedTaskWhenSnapshotsRefresh()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager);
        manager.AddSnapshot(new DownloadTaskSnapshot("A", DownloadTaskState.Running, 2, 0, 0, 0, "A 0")
        {
            CanCancel = true
        });
        manager.AddSnapshot(new DownloadTaskSnapshot("B", DownloadTaskState.Running, 2, 0, 0, 0, "B 0")
        {
            CanCancel = true
        });

        viewModel.SelectedDownloadTask = viewModel.DownloadTasks.Single(task => task.Name == "B");
        var selectedReference = viewModel.SelectedDownloadTask;
        manager.AddSnapshot(new DownloadTaskSnapshot("A", DownloadTaskState.Running, 2, 1, 1024, 0.5, "A 1")
        {
            CanCancel = true
        });

        Assert.Equal(2, viewModel.DownloadTasks.Count);
        Assert.Equal("B", viewModel.SelectedDownloadTask?.Name);
        Assert.Same(selectedReference, viewModel.SelectedDownloadTask);
        Assert.Equal("运行中", viewModel.SelectedDownloadTaskStateText);
        Assert.Equal("0.00 %", viewModel.SelectedDownloadTaskProgressText);
        Assert.Equal("A 1", viewModel.DownloadTasks.Single(task => task.Name == "A").Message);
    }

    [Fact]
    public void DownloadPageViewModelCancelsAllRunningAndClearsFinishedTasks()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager);
        manager.AddSnapshot(new DownloadTaskSnapshot("running", DownloadTaskState.Running, 2, 1, 1024, 0.5, "running")
        {
            CanCancel = true
        });
        manager.AddSnapshot(new DownloadTaskSnapshot("done", DownloadTaskState.Succeeded, 1, 1, 1024, 1, "done"));
        manager.AddSnapshot(new DownloadTaskSnapshot("failed", DownloadTaskState.Failed, 1, 0, 0, 0, "failed")
        {
            CanRetry = true
        });
        manager.AddSnapshot(new DownloadTaskSnapshot("canceled", DownloadTaskState.Canceled, 1, 0, 0, 0, "canceled")
        {
            CanRetry = true
        });

        viewModel.CancelAllRunningDownloadTasksCommand.Execute(null);
        viewModel.ClearFinishedDownloadTasksCommand.Execute(null);

        Assert.Equal("running", manager.CanceledName);
        Assert.Equal("running", Assert.Single(viewModel.DownloadTasks).Name);
        Assert.Equal(1, viewModel.RunningTaskCount);
        Assert.Equal(0, viewModel.FinishedTaskCount);
        Assert.Equal(0, viewModel.FailedTaskCount);
        Assert.Contains("已清理 3 个已结束任务", viewModel.StatusMessage);
    }

    [Fact]
    public void DownloadPageViewModelSummarizesDownloadTaskProgress()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager);

        manager.AddSnapshot(new DownloadTaskSnapshot("libraries", DownloadTaskState.Running, 4, 1, 512, 0.25, "libraries")
        {
            CanCancel = true
        });
        manager.AddSnapshot(new DownloadTaskSnapshot("assets", DownloadTaskState.Running, 2, 2, 1024, 0.75, "assets")
        {
            CanCancel = true
        });

        Assert.Equal(0.5, viewModel.OverallTaskProgress, precision: 3);
        Assert.Equal("50.00 %", viewModel.OverallTaskProgressText);
        Assert.Equal("3 / 6", viewModel.DownloadedFileCountText);
        Assert.Equal("1.5 KB", viewModel.DownloadedBytesText);
        Assert.Equal("运行中", viewModel.SelectedDownloadTaskStateText);
        Assert.Equal("25.00 %", viewModel.SelectedDownloadTaskProgressText);
        Assert.Equal("文件：1 / 4", viewModel.SelectedDownloadTaskFileText);
        Assert.Equal("已接收：512 B", viewModel.SelectedDownloadTaskBytesText);
    }

    [Fact]
    public void DownloadPageViewModelMarshalsDownloadSnapshotsToUiDispatcher()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var dispatcher = new QueueingUiDispatcherService(checkAccess: false);
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            dispatcher: dispatcher);

        manager.AddSnapshot(new DownloadTaskSnapshot("background", DownloadTaskState.Running, 1, 0, 256, 0, "background")
        {
            CanCancel = true
        });

        Assert.Empty(viewModel.DownloadTasks);
        Assert.Equal(1, dispatcher.QueuedCount);

        dispatcher.RunQueued();

        Assert.Single(viewModel.DownloadTasks);
        Assert.Equal("background", viewModel.SelectedDownloadTask?.Name);
    }

    [Fact]
    public void DownloadPageViewModelDoesNotPropagateDispatcherFailuresFromSnapshotEvents()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            dispatcher: new ThrowingUiDispatcherService());

        manager.AddSnapshot(new DownloadTaskSnapshot("background", DownloadTaskState.Running, 1, 0, 256, 0, "background")
        {
            CanCancel = true
        });

        Assert.Empty(viewModel.DownloadTasks);
    }

    [Fact]
    public async Task DownloadPageViewModelSearchesSelectedResourceSection()
    {
        using var temp = new TempDirectory();
        var search = new FakeCommunityResourceSearchService
        {
            Result = new CommunityResourceSearchResult([CreateSodiumProject()], 1, "Modrinth")
        };
        var viewModel = CreateDownloadPageViewModel(temp.Path, new FakeMinecraftClientDownloadService(), new FakeDownloadManagerService(), search);
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);
        viewModel.ResourceSearchText = "sodium";
        viewModel.ResourceGameVersion = "1.20.1";
        viewModel.ResourceLoader = "fabric";

        await viewModel.SearchResourcesAsync();

        Assert.Equal(1, viewModel.ResourceResultCount);
        Assert.Equal("Sodium", viewModel.SelectedResourceProject?.Name);
        Assert.Equal(CommunityResourceType.Mod, search.LastQuery?.Type);
        Assert.Equal("fabric", search.LastQuery?.Loader);
    }

    [Fact]
    public async Task DownloadPageViewModelFiltersResourceSourceAndOpensProjectPage()
    {
        using var temp = new TempDirectory();
        var urls = new CaptureExternalUrlService();
        var search = new FakeCommunityResourceSearchService
        {
            Result = new CommunityResourceSearchResult([CreateSodiumProject(), CreateCurseForgeProject()], 2, "Modrinth + CurseForge")
        };
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            new FakeDownloadManagerService(),
            search,
            urls: urls);
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);

        await viewModel.SearchResourcesAsync();
        viewModel.SelectedResourceSource = "CurseForge";
        viewModel.OpenSelectedResourceProjectCommand.Execute(null);

        Assert.Equal(1, viewModel.ResourceResultCount);
        Assert.Equal(CommunityResourcePlatform.CurseForge, viewModel.SelectedResourceProject?.Platform);
        Assert.Equal("https://www.curseforge.com/minecraft/mc-mods/jei", urls.LastOpenedUrl);

        viewModel.SelectedResourceSource = "全部";
        Assert.Equal(2, viewModel.ResourceResultCount);
    }

    [Fact]
    public async Task DownloadPageViewModelLoadsResourceVersionsAndStartsDownload()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var versionService = new FakeCommunityResourceVersionService
        {
            Versions =
            [
                new CommunityResourceVersion(
                    CommunityResourcePlatform.Modrinth,
                    CommunityResourceType.Mod,
                    "id",
                    "ver1",
                    "Sodium 0.5.8",
                    "mc1.20.1-0.5.8",
                    DateTimeOffset.Parse("2024-02-01T00:00:00+00:00"),
                    ["1.20.1"],
                    ["fabric"],
                    [new CommunityResourceFile("sodium.jar", "https://cdn.modrinth.com/sodium.jar", 100, "0123456789abcdef0123456789abcdef01234567", null, true)],
                    [new CommunityResourceDependency("fabric-api", "fabric-api-ver", "required")])
            ]
        };
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            new FakeCommunityResourceSearchService(),
            versionService);
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);
        viewModel.SelectedResourceProject = CreateSodiumProject();
        viewModel.ResourceGameVersion = "1.20.1";
        viewModel.ResourceLoader = "fabric";

        await viewModel.LoadResourceVersionsAsync();
        await viewModel.DownloadSelectedResourceFileAsync();

        Assert.Equal(1, viewModel.ResourceVersionCount);
        Assert.Equal("sodium.jar", viewModel.SelectedResourceFile?.FileName);
        Assert.Equal(2, manager.LastFiles.Count);
        Assert.EndsWith(Path.Combine("mods", "sodium.jar"), manager.LastFiles[0].LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("mods", "fabric-api-ver.jar"), manager.LastFiles[1].LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1.20.1", versionService.LastGameVersion);
        Assert.Equal("fabric", versionService.LastLoader);
        Assert.True(viewModel.DownloadSelectedResourceFileCommand.CanExecute(null));
        Assert.Equal("下载到目标目录", viewModel.ResourceDownloadActionText);
        Assert.Contains("1 个必需依赖", viewModel.SelectedResourceVersionSummary);
    }

    [Fact]
    public async Task DownloadPageViewModelAutomaticallyLoadsResourceVersionsAfterSearch()
    {
        using var temp = new TempDirectory();
        var versionService = new FakeCommunityResourceVersionService
        {
            Versions =
            [
                new CommunityResourceVersion(
                    CommunityResourcePlatform.Modrinth,
                    CommunityResourceType.Mod,
                    "id",
                    "ver1",
                    "Sodium 0.5.8",
                    "mc1.20.1-0.5.8",
                    DateTimeOffset.Parse("2024-02-01T00:00:00+00:00"),
                    ["1.20.1"],
                    ["fabric"],
                    [new CommunityResourceFile("sodium.jar", "https://cdn.modrinth.com/sodium.jar", 100, null, null, true)],
                    [new CommunityResourceDependency("fabric-api", "fabric-api-ver", "required")])
            ]
        };
        var search = new FakeCommunityResourceSearchService
        {
            Result = new CommunityResourceSearchResult([CreateSodiumProject()], 1, "Modrinth")
        };
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            new FakeDownloadManagerService(),
            search,
            versionService);
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);
        viewModel.ResourceGameVersion = "1.20.1";
        viewModel.ResourceLoader = "fabric";

        await viewModel.SearchResourcesAsync();
        await WaitUntilAsync(() => viewModel.ResourceVersionCount == 1);

        Assert.Equal("sodium.jar", viewModel.SelectedResourceFile?.FileName);
        Assert.Contains("fabric", viewModel.SelectedResourcePlatformText);
        Assert.Contains("必需 1 个", viewModel.SelectedResourceDependencyText);
        Assert.Contains("启动平台：fabric", viewModel.SelectedResourceLoaderText);
        Assert.Contains("适用版本：1.20.1", viewModel.SelectedResourceGameVersionText);
        Assert.Contains("fabric-api", viewModel.SelectedResourceDependencyListText);
        Assert.Contains("必需依赖会联动下载", viewModel.DownloadInfoDetails);
        Assert.Equal("fabric", versionService.LastLoader);
    }

    [Fact]
    public async Task DownloadPageViewModelReloadsResourceVersionsWhenFiltersChange()
    {
        using var temp = new TempDirectory();
        var versionService = new FakeCommunityResourceVersionService
        {
            Versions =
            [
                new CommunityResourceVersion(
                    CommunityResourcePlatform.Modrinth,
                    CommunityResourceType.Mod,
                    "id",
                    "ver1",
                    "Sodium 0.5.8",
                    "mc1.20.1-0.5.8",
                    DateTimeOffset.Parse("2024-02-01T00:00:00+00:00"),
                    ["1.20.1"],
                    ["fabric"],
                    [new CommunityResourceFile("sodium.jar", "https://cdn.modrinth.com/sodium.jar", 100, null, null, true)],
                    [])
            ]
        };
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            new FakeDownloadManagerService(),
            new FakeCommunityResourceSearchService(),
            versionService);
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);
        viewModel.SelectedResourceProject = CreateSodiumProject();

        await WaitUntilAsync(() => versionService.GetVersionsCount == 1);

        viewModel.ResourceGameVersion = "1.20.1";
        await WaitUntilAsync(() => versionService.GetVersionsCount >= 2);

        viewModel.ResourceLoader = "fabric";
        await WaitUntilAsync(() => versionService.GetVersionsCount >= 3 && viewModel.ResourceVersionCount == 1);

        Assert.Equal("1.20.1", versionService.LastGameVersion);
        Assert.Equal("fabric", versionService.LastLoader);
        Assert.Contains("启动平台", viewModel.DownloadInfoDetails);
    }

    [Fact]
    public async Task DownloadPageViewModelSkipsExistingResourceFiles()
    {
        using var temp = new TempDirectory();
        var existing = Path.Combine(temp.Path, "mods", "sodium.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllBytes(existing, new byte[100]);
        var manager = new FakeDownloadManagerService();
        var file = new CommunityResourceFile("sodium.jar", "https://cdn.modrinth.com/sodium.jar", 100, null, null, true);
        var version = new CommunityResourceVersion(
            CommunityResourcePlatform.Modrinth,
            CommunityResourceType.Mod,
            "id",
            "ver1",
            "Sodium",
            "mc1.20.1-0.5.8",
            DateTimeOffset.Parse("2024-02-01T00:00:00+00:00"),
            ["1.20.1"],
            ["fabric"],
            [file],
            []);
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager);
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);
        viewModel.SelectedResourceProject = CreateSodiumProject();
        viewModel.SelectedResourceVersion = version;
        viewModel.SelectedResourceFile = file;

        await viewModel.DownloadSelectedResourceFileAsync();

        Assert.Empty(manager.LastFiles);
        Assert.Contains("跳过重复下载", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadPageViewModelSkipsExistingDependencyFiles()
    {
        using var temp = new TempDirectory();
        var dependencyPath = Path.Combine(temp.Path, "mods", "fabric-api-ver.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(dependencyPath)!);
        File.WriteAllText(dependencyPath, "already installed");
        var manager = new FakeDownloadManagerService();
        var file = new CommunityResourceFile("sodium.jar", "https://cdn.modrinth.com/sodium.jar", 100, null, null, true);
        var version = new CommunityResourceVersion(
            CommunityResourcePlatform.Modrinth,
            CommunityResourceType.Mod,
            "id",
            "ver1",
            "Sodium",
            "mc1.20.1-0.5.8",
            DateTimeOffset.Parse("2024-02-01T00:00:00+00:00"),
            ["1.20.1"],
            ["fabric"],
            [file],
            [new CommunityResourceDependency("fabric-api", "fabric-api-ver", "required")]);
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            resourceVersions: new FakeCommunityResourceVersionService());
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);
        viewModel.SelectedResourceProject = CreateSodiumProject();
        viewModel.SelectedResourceVersion = version;
        viewModel.SelectedResourceFile = file;

        await viewModel.DownloadSelectedResourceFileAsync();

        var onlyFile = Assert.Single(manager.LastFiles);
        Assert.EndsWith(Path.Combine("mods", "sodium.jar"), onlyFile.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("已跳过 1 个已存在文件", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadPageViewModelSkipsQueuedResourceAndDependencyFiles()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var queuedResourcePath = Path.Combine(temp.Path, "mods", "sodium.jar");
        var queuedDependencyPath = Path.Combine(temp.Path, "mods", "fabric-api-ver.jar");
        manager.AddSnapshot(new DownloadTaskSnapshot("正在下载资源", DownloadTaskState.Running, 2, 0, 0, 0, "下载中")
        {
            LocalPaths = [queuedResourcePath, queuedDependencyPath],
            PrimaryLocalPath = queuedResourcePath,
            CanCancel = true
        });
        var file = new CommunityResourceFile("sodium.jar", "https://cdn.modrinth.com/sodium.jar", 100, null, null, true);
        var version = new CommunityResourceVersion(
            CommunityResourcePlatform.Modrinth,
            CommunityResourceType.Mod,
            "id",
            "ver1",
            "Sodium",
            "mc1.20.1-0.5.8",
            DateTimeOffset.Parse("2024-02-01T00:00:00+00:00"),
            ["1.20.1"],
            ["fabric"],
            [file],
            [new CommunityResourceDependency("fabric-api", "fabric-api-ver", "required")]);
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            resourceVersions: new FakeCommunityResourceVersionService());
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);
        viewModel.SelectedResourceProject = CreateSodiumProject();
        viewModel.SelectedResourceVersion = version;
        viewModel.SelectedResourceFile = file;

        await viewModel.DownloadSelectedResourceFileAsync();

        Assert.Empty(manager.LastFiles);
        Assert.Contains("队列中任务", viewModel.StatusMessage);
        Assert.Contains("跳过重复下载", viewModel.StatusMessage);
    }

    [Fact]
    public void DownloadTaskSnapshotExposesChineseStateText()
    {
        Assert.Equal("等待中", new DownloadTaskSnapshot("waiting", DownloadTaskState.Waiting, 1, 0, 0, 0, "").StateText);
        Assert.Equal("下载中", new DownloadTaskSnapshot("running", DownloadTaskState.Running, 1, 0, 0, 0, "").StateText);
        Assert.Equal("已完成", new DownloadTaskSnapshot("done", DownloadTaskState.Succeeded, 1, 1, 0, 1, "").StateText);
        Assert.Equal("失败", new DownloadTaskSnapshot("failed", DownloadTaskState.Failed, 1, 0, 0, 0, "").StateText);
        Assert.Equal("已取消", new DownloadTaskSnapshot("canceled", DownloadTaskState.Canceled, 1, 0, 0, 0, "").StateText);
    }

    [Fact]
    public void CommunityResourceDependencyDisplaysReadableChineseDetails()
    {
        var required = new CommunityResourceDependency("fabric-api", "fabric-api-ver", "required");
        var optional = new CommunityResourceDependency("sodium-extra", null, "optional");
        var incompatible = new CommunityResourceDependency(null, "bad-version", "incompatible");
        var embedded = new CommunityResourceDependency(null, null, "embedded");

        Assert.Equal("必需：项目 fabric-api / 版本 fabric-api-ver", required.DisplayText);
        Assert.Equal("可选：项目 sodium-extra", optional.DisplayText);
        Assert.Equal("不兼容：版本 bad-version", incompatible.DisplayText);
        Assert.Equal("内置：未知依赖", embedded.DisplayText);
    }

    [Fact]
    public async Task DownloadPageViewModelBlocksResourceDownloadWhenFileHasNoUrl()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var file = new CommunityResourceFile("blocked.jar", "", 100, null, null, true);
        var version = new CommunityResourceVersion(
            CommunityResourcePlatform.CurseForge,
            CommunityResourceType.Mod,
            "id",
            "file1",
            "Blocked",
            "1.0.0",
            DateTimeOffset.Parse("2024-02-01T00:00:00+00:00"),
            ["1.20.1"],
            ["forge"],
            [file],
            []);
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager);
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);
        viewModel.SelectedResourceProject = CreateCurseForgeProject();
        viewModel.SelectedResourceVersion = version;
        viewModel.SelectedResourceFile = file;

        Assert.False(viewModel.DownloadSelectedResourceFileCommand.CanExecute(null));
        Assert.Equal("文件缺少下载地址", viewModel.ResourceDownloadActionText);

        await viewModel.DownloadSelectedResourceFileAsync();

        Assert.Empty(manager.LastFiles);
        Assert.Equal("所选文件缺少下载地址，无法创建下载任务", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadPageViewModelDownloadsModsIntoSelectedInstanceGameDirectory()
    {
        using var temp = new TempDirectory();
        var instance = WriteDownloadTestInstance(temp.Path, "fabric-1.20.1", """
        { "id": "fabric-1.20.1", "type": "release", "releaseTime": "2023-06-13T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, instance.Name);
        var manager = new FakeDownloadManagerService();
        var file = new CommunityResourceFile("sodium.jar", "https://cdn.modrinth.com/sodium.jar", 100, null, null, true);
        var version = new CommunityResourceVersion(
            CommunityResourcePlatform.Modrinth,
            CommunityResourceType.Mod,
            "id",
            "ver1",
            "Sodium",
            "mc1.20.1-0.5.8",
            DateTimeOffset.Parse("2024-02-01T00:00:00+00:00"),
            ["1.20.1"],
            ["fabric"],
            [file],
            []);
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            minecraftDiscovery: new FakeMinecraftDiscoveryService(temp.Path, [instance]));
        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Section == DownloadSection.Mod);
        viewModel.SelectedResourceProject = CreateSodiumProject();
        viewModel.SelectedResourceVersion = version;
        viewModel.SelectedResourceFile = file;

        await viewModel.DownloadSelectedResourceFileAsync();

        var downloaded = Assert.Single(manager.LastFiles);
        Assert.StartsWith(Path.GetFullPath(instance.VersionPath), downloaded.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("mods", "sodium.jar"), downloaded.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(instance.VersionPath, viewModel.ResourceInstallTarget, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadPageViewModelAppliesInstanceModDownloadPresetOnce()
    {
        using var temp = new TempDirectory();
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            new FakeDownloadManagerService(),
            configureSettings: settings =>
            {
                settings.Set(AppSettingKeys.DownloadPresetResourceSection, (int)DownloadSection.Mod);
                settings.Set(AppSettingKeys.DownloadPresetSearchText, "sodium");
                settings.Set(AppSettingKeys.DownloadPresetGameVersion, "1.20.1");
                settings.Set(AppSettingKeys.DownloadPresetLoader, "fabric");
            });

        await viewModel.OnNavigatedToAsync();

        Assert.Equal(DownloadSection.Mod, viewModel.SelectedSection.Section);
        Assert.Equal("sodium", viewModel.ResourceSearchText);
        Assert.Equal("1.20.1", viewModel.ResourceGameVersion);
        Assert.Equal("fabric", viewModel.ResourceLoader);
        Assert.Contains("已套用", viewModel.StatusMessage);

        viewModel.ResourceSearchText = "changed";
        await viewModel.OnNavigatedToAsync();

        Assert.Equal("changed", viewModel.ResourceSearchText);
    }

    [Fact]
    public async Task DownloadPageViewModelInstallsLocalModpack()
    {
        using var temp = new TempDirectory();
        File.WriteAllLines(Path.Combine(temp.Path, "PCL.ini"), ["InstanceCache:cached", "Other:keep"]);
        var manager = new FakeDownloadManagerService();
        var modpackInstall = new FakeModpackInstallService
        {
            Plan = new ModpackInstallPlan(
                "Test Pack",
                "Test Pack",
                "1.20.1",
                "fabric-loader",
                "0.15.11",
                Path.Combine(temp.Path, "versions", "Test Pack"),
                [new DownloadFile(["https://example/mod.jar"], Path.Combine(temp.Path, "versions", "Test Pack", "mods", "mod.jar"), new DownloadFileCheck())],
                ["loader pending"],
                [],
                1)
        };
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            modpackInstall: modpackInstall,
            fileDialogs: new FakeFileDialogService(Path.Combine(temp.Path, "test.mrpack")));

        await viewModel.InstallLocalModpackAsync();

        Assert.Equal("Test Pack", modpackInstall.LastModpackPath is null ? null : modpackInstall.Plan.InstanceName);
        Assert.Single(manager.LastFiles);
        Assert.Contains("Test Pack", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        var ini = File.ReadAllText(Path.Combine(temp.Path, "PCL.ini"));
        Assert.Contains("Version:Test Pack", ini);
        Assert.Contains("InstanceCache:", ini);
        Assert.DoesNotContain("InstanceCache:cached", ini);
        Assert.Contains("Other:keep", ini);
    }

    [Fact]
    public async Task DownloadPageViewModelKeepsProcessorsPendingWhenJavaPathIsMissing()
    {
        using var temp = new TempDirectory();
        var manager = new FakeDownloadManagerService();
        var processorRunner = new FakeLoaderProcessorRunner();
        var modpackInstall = new FakeModpackInstallService
        {
            Plan = new ModpackInstallPlan(
                "Forge Pack",
                "Forge Pack",
                "1.20.1",
                "forge",
                "47.2.0",
                Path.Combine(temp.Path, "versions", "Forge Pack"),
                [],
                [],
                [new LoaderProcessorStep("net.minecraftforge:installertools:1.2.10", [], [], new Dictionary<string, string>(), true)],
                0)
        };
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            modpackInstall: modpackInstall,
            processorRunner: processorRunner,
            fileDialogs: new FakeFileDialogService(Path.Combine(temp.Path, "forge.mrpack")));

        await viewModel.InstallLocalModpackAsync();

        Assert.Equal(0, processorRunner.RunCount);
        Assert.Contains("processors", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Java", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadPageViewModelRunsProcessorsWhenJavaPathExists()
    {
        using var temp = new TempDirectory();
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        WriteSmallFile(javaPath);
        var manager = new FakeDownloadManagerService();
        var processorRunner = new FakeLoaderProcessorRunner();
        var modpackInstall = new FakeModpackInstallService
        {
            Plan = new ModpackInstallPlan(
                "Forge Pack",
                "Forge Pack",
                "1.20.1",
                "forge",
                "47.2.0",
                Path.Combine(temp.Path, "versions", "Forge Pack"),
                [],
                [],
                [new LoaderProcessorStep("net.minecraftforge:installertools:1.2.10", [], [], new Dictionary<string, string>(), true)],
                0)
        };
        var viewModel = CreateDownloadPageViewModel(
            temp.Path,
            new FakeMinecraftClientDownloadService(),
            manager,
            modpackInstall: modpackInstall,
            processorRunner: processorRunner,
            fileDialogs: new FakeFileDialogService(Path.Combine(temp.Path, "forge.mrpack")),
            launchJavaPath: javaPath);

        await viewModel.InstallLocalModpackAsync();

        Assert.Equal(1, processorRunner.RunCount);
        Assert.Equal(temp.Path, processorRunner.LastMinecraftRootPath);
        Assert.Equal(javaPath, processorRunner.LastJavaPath);
        Assert.Equal(1, processorRunner.LastProcessorCount);
        Assert.Contains("processors", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDownloadByteClient : IDownloadByteClient
    {
        private readonly Dictionary<string, byte[]> _responses = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failures = new(StringComparer.OrdinalIgnoreCase);

        public List<string> RequestedUrls { get; } = [];

        public void Map(string url, byte[] bytes)
        {
            _responses[url] = bytes;
        }

        public void Fail(string url)
        {
            _failures.Add(url);
        }

        public Task<byte[]> GetBytesAsync(string url, bool simulateBrowserHeaders = false, CancellationToken cancellationToken = default)
        {
            RequestedUrls.Add(url);
            if (_failures.Contains(url))
            {
                throw new HttpRequestException("forced failure");
            }

            if (_responses.TryGetValue(url, out var bytes))
            {
                return Task.FromResult(bytes);
            }

        throw new HttpRequestException("not mapped: " + url);
        }
    }

    private sealed class StreamingOnlyDownloadByteClient(byte[] bytes) : IDownloadByteClient
    {
        public int ByteArrayCalls { get; private set; }

        public int StreamingCalls { get; private set; }

        public Task<byte[]> GetBytesAsync(string url, bool simulateBrowserHeaders = false, CancellationToken cancellationToken = default)
        {
            ByteArrayCalls++;
            throw new InvalidOperationException("DownloadManager should use streaming downloads.");
        }

        public async Task<long> DownloadToFileAsync(string url, string localPath, bool simulateBrowserHeaders = false, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);
            progress?.Report(bytes.Length);
            return bytes.Length;
        }
    }

    private sealed class ResumableDownloadByteClient(byte[] bytes, int firstChunkLength) : IDownloadByteClient
    {
        public int StreamingCalls { get; private set; }

        public long ResumeOffset { get; private set; }

        public Task<byte[]> GetBytesAsync(string url, bool simulateBrowserHeaders = false, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("DownloadManager should use streaming downloads.");
        }

        public async Task<long> DownloadToFileAsync(string url, string localPath, bool simulateBrowserHeaders = false, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            if (StreamingCalls == 1)
            {
                var first = bytes.Take(firstChunkLength).ToArray();
                await File.WriteAllBytesAsync(localPath, first, cancellationToken);
                progress?.Report(first.Length);
                throw new HttpRequestException("connection interrupted");
            }

            ResumeOffset = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
            await using var stream = new FileStream(localPath, FileMode.Append, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var remaining = bytes.Skip((int)ResumeOffset).ToArray();
            await stream.WriteAsync(remaining, cancellationToken);
            progress?.Report(remaining.Length);
            return remaining.Length;
        }
    }

    private sealed class DelayedDownloadByteClient(TimeSpan delay, int releaseAfterConcurrentRequests = 1) : IDownloadByteClient
    {
        private readonly Dictionary<string, byte[]> _responses = new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _lock = new();
        private int _activeRequests;

        public int MaxConcurrentRequests { get; private set; }

        public Task WaitForFirstRequestAsync()
        {
            return _firstRequest.Task;
        }

        public void Map(string url, byte[] bytes)
        {
            _responses[url] = bytes;
        }

        public async Task<byte[]> GetBytesAsync(string url, bool simulateBrowserHeaders = false, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeRequests);
            _firstRequest.TrySetResult();
            lock (_lock)
            {
                MaxConcurrentRequests = Math.Max(MaxConcurrentRequests, active);
            }

            if (active >= releaseAfterConcurrentRequests)
            {
                _releaseGate.TrySetResult();
            }

            try
            {
                var completed = await Task.WhenAny(_releaseGate.Task, Task.Delay(delay, cancellationToken));
                await completed;
                return _responses.TryGetValue(url, out var bytes)
                    ? bytes
                    : throw new HttpRequestException("not mapped: " + url);
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public int StartCount { get; private set; }

        public ProcessStartInfo? LastStartInfo { get; private set; }

        public int ExitCode { get; init; }

        public Process Start(ProcessStartInfo startInfo)
        {
            StartCount++;
            LastStartInfo = startInfo;
            return Process.GetCurrentProcess();
        }

        public Task<int> WaitForExitAsync(Process process, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExitCode);
        }
    }

    private static DownloadPageViewModel CreateDownloadPageViewModel(
        string root,
        IMinecraftClientDownloadService clientDownload,
        IDownloadManagerService downloadManager,
        ICommunityResourceSearchService? resourceSearch = null,
        ICommunityResourceVersionService? resourceVersions = null,
        IModpackInstallService? modpackInstall = null,
        ILoaderProcessorRunner? processorRunner = null,
        IFileDialogService? fileDialogs = null,
        string? launchJavaPath = null,
        IMinecraftDiscoveryService? minecraftDiscovery = null,
        Action<AppSettingsService>? configureSettings = null,
        ILoaderVersionService? loaderVersions = null,
        IFabricLoaderInstallService? fabricLoaderInstall = null,
        IQuiltLoaderInstallService? quiltLoaderInstall = null,
        IForgeLoaderInstallService? forgeLoaderInstall = null,
        INeoForgeLoaderInstallService? neoForgeLoaderInstall = null,
        IFolderOpenService? folders = null,
        IExternalUrlService? urls = null,
        IUiDispatcherService? dispatcher = null)
    {
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(root, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, root);
        if (!string.IsNullOrWhiteSpace(launchJavaPath))
        {
            settings.Set(AppSettingKeys.LaunchJavaPath, launchJavaPath);
        }

        configureSettings?.Invoke(settings);

        return new DownloadPageViewModel(
            clientDownload,
            downloadManager,
            resourceSearch ?? new FakeCommunityResourceSearchService(),
            resourceVersions ?? new FakeCommunityResourceVersionService(),
            modpackInstall ?? new FakeModpackInstallService(),
            processorRunner ?? new FakeLoaderProcessorRunner(),
            settings,
            minecraftDiscovery ?? new FakeMinecraftDiscoveryService(root),
            fileDialogs ?? new NullFileDialogService(),
            new NullLoggerService(),
            loaderVersions: loaderVersions,
            fabricLoaderInstall: fabricLoaderInstall,
            quiltLoaderInstall: quiltLoaderInstall,
            forgeLoaderInstall: forgeLoaderInstall,
            neoForgeLoaderInstall: neoForgeLoaderInstall,
            folders: folders,
            urls: urls,
            dispatcher: dispatcher);
    }

    private static void CreateTestMrpack(string path, string name = "Test Pack", string loaderName = "fabric-loader", string loaderVersion = "0.15.11")
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var index = archive.CreateEntry("modrinth.index.json");
        using (var stream = index.Open())
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            var json = """
            {
              "formatVersion": 1,
              "game": "minecraft",
              "versionId": "1.0.0",
              "name": "{PACK_NAME}",
              "dependencies": {
                "minecraft": "1.20.1",
                "{LOADER_NAME}": "{LOADER_VERSION}"
              },
              "files": [
                {
                  "path": "mods/example.jar",
                  "hashes": { "sha1": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                  "downloads": ["https://cdn.modrinth.com/example.jar"],
                  "fileSize": 123
                },
                {
                  "path": "server/only.jar",
                  "env": { "client": "unsupported", "server": "required" },
                  "hashes": { "sha1": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
                  "downloads": ["https://cdn.modrinth.com/server.jar"],
                  "fileSize": 456
                }
              ]
            }
            """;
            writer.Write(json
                .Replace("{PACK_NAME}", name, StringComparison.Ordinal)
                .Replace("{LOADER_NAME}", loaderName, StringComparison.Ordinal)
                .Replace("{LOADER_VERSION}", loaderVersion, StringComparison.Ordinal));
        }

        var overrideEntry = archive.CreateEntry("overrides/config/example.cfg");
        using var overrideStream = overrideEntry.Open();
        using var overrideWriter = new StreamWriter(overrideStream, Encoding.UTF8);
        overrideWriter.Write("enabled=true");
    }

    private static void AddZipText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static MinecraftInstance WriteDownloadTestInstance(string root, string name, string json)
    {
        var versionPath = Path.Combine(root, "versions", name);
        Directory.CreateDirectory(versionPath);
        File.WriteAllText(Path.Combine(versionPath, name + ".json"), json);
        return new MinecraftDiscoveryService().InspectInstance(root, versionPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name });
    }

    private static byte[] CreateInstallerJarBytes(string versionJson, string? installProfileJson = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("version.json");
            using (var entryStream = entry.Open())
            using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
            {
                writer.Write(versionJson);
            }

            if (!string.IsNullOrWhiteSpace(installProfileJson))
            {
                var installEntry = archive.CreateEntry("install_profile.json");
                using var installStream = installEntry.Open();
                using var installWriter = new StreamWriter(installStream, Encoding.UTF8);
                installWriter.Write(installProfileJson);
            }
        }

        return stream.ToArray();
    }

    private static void CreateProcessorJar(string path, string mainClass)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        var manifest = archive.CreateEntry("META-INF/MANIFEST.MF");
        using var stream = manifest.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.WriteLine("Manifest-Version: 1.0");
        writer.WriteLine("Main-Class: " + mainClass);
        writer.WriteLine();
    }

    private static void WriteSmallFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition(), "Condition was not satisfied before timeout.");
    }

    private static CommunityResourceProject CreateSodiumProject()
    {
        return new CommunityResourceProject(
            CommunityResourcePlatform.Modrinth,
            CommunityResourceType.Mod,
            "AANobbMI",
            "sodium",
            "Sodium",
            "Modern rendering engine",
            "https://modrinth.com/mod/sodium",
            null,
            1000,
            DateTimeOffset.Parse("2024-01-02T03:04:05+00:00"),
            ["1.20.1"],
            ["fabric"],
            ["optimization"]);
    }

    private static CommunityResourceProject CreateCurseForgeProject()
    {
        return new CommunityResourceProject(
            CommunityResourcePlatform.CurseForge,
            CommunityResourceType.Mod,
            "322385",
            "jei",
            "Just Enough Items",
            "Recipe viewer",
            "https://www.curseforge.com/minecraft/mc-mods/jei",
            null,
            1000,
            DateTimeOffset.Parse("2024-01-02T03:04:05+00:00"),
            ["1.20.1"],
            ["forge"],
            ["utility"]);
    }

    private static CommunityResourceFile CreateResourceFile(string fileName)
    {
        return new CommunityResourceFile(
            fileName,
            "https://cdn.modrinth.com/data/project/versions/version/" + Uri.EscapeDataString(Path.GetFileName(fileName)),
            128,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            null,
            true);
    }

    private sealed class FakeCommunityResourceSearchService : ICommunityResourceSearchService
    {
        public CommunityResourceSearchQuery? LastQuery { get; private set; }

        public CommunityResourceSearchResult Result { get; init; } = new([], 0, "fake");

        public Task<CommunityResourceSearchResult> SearchAsync(CommunityResourceSearchQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeCommunityResourceVersionService : ICommunityResourceVersionService
    {
        public IReadOnlyList<CommunityResourceVersion> Versions { get; init; } = [];

        public int GetVersionsCount { get; private set; }

        public string? LastGameVersion { get; private set; }

        public string? LastLoader { get; private set; }

        public Task<IReadOnlyList<CommunityResourceVersion>> GetVersionsAsync(CommunityResourceProject project, string gameVersion, string loader, CancellationToken cancellationToken = default)
        {
            GetVersionsCount++;
            LastGameVersion = gameVersion;
            LastLoader = loader;
            return Task.FromResult(Versions);
        }

        public DownloadFile CreateDownloadFile(CommunityResourceProject project, CommunityResourceVersion version, CommunityResourceFile file, string minecraftRootPath)
        {
            var directory = project.Type switch
            {
                CommunityResourceType.Mod => Path.Combine(minecraftRootPath, "mods"),
                CommunityResourceType.ResourcePack => Path.Combine(minecraftRootPath, "resourcepacks"),
                CommunityResourceType.Shader => Path.Combine(minecraftRootPath, "shaderpacks"),
                CommunityResourceType.DataPack => Path.Combine(minecraftRootPath, "datapacks"),
                CommunityResourceType.ModPack => Path.Combine(minecraftRootPath, "PCL", "Downloads", "ModPacks"),
                _ => Path.Combine(minecraftRootPath, "PCL", "Downloads")
            };
            return new DownloadFile([file.Url], Path.Combine(directory, Path.GetFileName(file.FileName)), new DownloadFileCheck(ActualSize: file.Size, Hash: file.Sha1), true);
        }

        public Task<IReadOnlyList<DownloadFile>> CreateDownloadFilesWithDependenciesAsync(
            CommunityResourceProject project,
            CommunityResourceVersion version,
            CommunityResourceFile file,
            string minecraftRootPath,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
        {
            LastGameVersion = gameVersion;
            LastLoader = loader;
            var files = new List<DownloadFile>
            {
                CreateDownloadFile(project, version, file, minecraftRootPath)
            };
            foreach (var dependency in version.Dependencies.Where(item => item.IsRequired))
            {
                files.Add(new DownloadFile(
                    ["https://cdn.modrinth.com/" + dependency.VersionId + ".jar"],
                    Path.Combine(minecraftRootPath, "mods", dependency.VersionId + ".jar"),
                    new DownloadFileCheck(),
                    true));
            }

            return Task.FromResult<IReadOnlyList<DownloadFile>>(files);
        }
    }

    private sealed class FakeMinecraftClientDownloadService : IMinecraftClientDownloadService
    {
        public IReadOnlyList<MinecraftRemoteVersion> Versions { get; init; } = [];

        public IReadOnlyList<DownloadFile> PlanFiles { get; init; } = [];

        public bool UseDefaultPlanFiles { get; init; }

        public string? LastMinecraftRootPath { get; private set; }

        public int GetVersionManifestCount { get; private set; }

        public Task<IReadOnlyList<MinecraftRemoteVersion>> GetVersionManifestAsync(CancellationToken cancellationToken = default)
        {
            GetVersionManifestCount++;
            return Task.FromResult(Versions);
        }

        public Task<MinecraftClientInstallPlan> CreateInstallPlanAsync(string minecraftRootPath, string versionId, string instanceName, CancellationToken cancellationToken = default)
        {
            LastMinecraftRootPath = minecraftRootPath;
            var versionFolder = Path.Combine(minecraftRootPath, "versions", instanceName);
            var files = UseDefaultPlanFiles
                ? new List<DownloadFile>
                {
                    new(["https://example/" + instanceName + ".json"], Path.Combine(versionFolder, instanceName + ".json"), new DownloadFileCheck(IsJson: true)),
                    new(["https://example/" + instanceName + ".jar"], Path.Combine(versionFolder, instanceName + ".jar"), new DownloadFileCheck(MinSize: 1))
                }
                : PlanFiles;
            return Task.FromResult(new MinecraftClientInstallPlan(versionId, instanceName, versionFolder, files));
        }
    }

    private sealed class FakeModpackInstallService : IModpackInstallService
    {
        public ModpackInstallPlan Plan { get; init; } = new(
            "",
            "",
            "",
            null,
            null,
            "",
            [],
            [],
            [],
            0);

        public string? LastModpackPath { get; private set; }

        public string? LastMinecraftRootPath { get; private set; }

        public Task<ModpackInstallPlan> CreateModrinthInstallPlanAsync(string modpackPath, string minecraftRootPath, string? instanceName = null, CancellationToken cancellationToken = default)
        {
            LastModpackPath = modpackPath;
            LastMinecraftRootPath = minecraftRootPath;
            return Task.FromResult(Plan);
        }
    }

    private sealed class FakeLoaderProcessorRunner : ILoaderProcessorRunner
    {
        public int RunCount { get; private set; }

        public string? LastMinecraftRootPath { get; private set; }

        public string? LastJavaPath { get; private set; }

        public int LastProcessorCount { get; private set; }

        public Task<LoaderProcessorRunResult> RunAsync(
            string minecraftRootPath,
            string javaPath,
            IReadOnlyList<LoaderProcessorStep> processors,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            LastMinecraftRootPath = minecraftRootPath;
            LastJavaPath = javaPath;
            LastProcessorCount = processors.Count;
            return Task.FromResult(new LoaderProcessorRunResult(true, processors.Select(item => item.JarCoordinate).ToList(), [], [], [], []));
        }
    }

    private sealed class FakeLoaderVersionService : ILoaderVersionService
    {
        public IReadOnlyList<LoaderVersionOption> Versions { get; init; } = [];

        public string? LastLoaderKind { get; private set; }

        public string? LastMinecraftVersion { get; private set; }

        public Task<IReadOnlyList<LoaderVersionOption>> GetVersionsAsync(
            string loaderKind,
            string minecraftVersion,
            CancellationToken cancellationToken = default)
        {
            LastLoaderKind = loaderKind;
            LastMinecraftVersion = minecraftVersion;
            return Task.FromResult(Versions);
        }
    }

    private sealed class FakeFabricLoaderInstallService : IFabricLoaderInstallService
    {
        public string? LastMinecraftRootPath { get; private set; }

        public string? LastInstanceName { get; private set; }

        public string? LastInstancePath { get; private set; }

        public string? LastMinecraftVersion { get; private set; }

        public string? LastLoaderVersion { get; private set; }

        public Task<LoaderInstallPlan> CreateInstallPlanAsync(
            string minecraftRootPath,
            string instanceName,
            string instancePath,
            string minecraftVersion,
            string loaderVersion,
            CancellationToken cancellationToken = default)
        {
            LastMinecraftRootPath = minecraftRootPath;
            LastInstanceName = instanceName;
            LastInstancePath = instancePath;
            LastMinecraftVersion = minecraftVersion;
            LastLoaderVersion = loaderVersion;
            return Task.FromResult(new LoaderInstallPlan(
                "fabric-loader",
                loaderVersion,
                Path.Combine(instancePath, instanceName + ".json"),
                [
                    new(
                        ["https://maven.fabricmc.net/net/fabricmc/fabric-loader/" + loaderVersion + "/fabric-loader-" + loaderVersion + ".jar"],
                        Path.Combine(minecraftRootPath, "libraries", "net", "fabricmc", "fabric-loader", loaderVersion, "fabric-loader-" + loaderVersion + ".jar"),
                        new DownloadFileCheck(MinSize: 1))
                ],
                []));
        }
    }

    private sealed class CaptureFolderOpenService : IFolderOpenService
    {
        public string? LastOpenedFolder { get; private set; }

        public void OpenFolder(string folderPath)
        {
            LastOpenedFolder = folderPath;
        }
    }

    private sealed class CaptureExternalUrlService : IExternalUrlService
    {
        public string? LastOpenedUrl { get; private set; }

        public void OpenUrl(string url)
        {
            LastOpenedUrl = url;
        }
    }

    private sealed class FakeFileDialogService(string? modpackPath) : IFileDialogService
    {
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
            return modpackPath;
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

    private sealed class CapturePromptService(bool confirmationResult) : IUserPromptService
    {
        public int ConfirmCount { get; private set; }

        public bool Confirm(string title, string message)
        {
            ConfirmCount++;
            return confirmationResult;
        }

        public string? Prompt(string title, string message, string defaultValue)
        {
            return defaultValue;
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

    private sealed class ThrowingUiDispatcherService : IUiDispatcherService
    {
        public bool CheckAccess()
        {
            return false;
        }

        public void Invoke(Action action)
        {
            throw new InvalidOperationException("dispatcher failed");
        }

        public Task InvokeAsync(Action action)
        {
            throw new InvalidOperationException("dispatcher failed");
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            throw new InvalidOperationException("dispatcher failed");
        }
    }

    private sealed class FakeDownloadManagerService : IDownloadManagerService
    {
        private readonly List<DownloadTaskSnapshot> _tasks = [];
        private readonly Dictionary<string, IReadOnlyList<DownloadFile>> _files = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<DownloadTaskSnapshot>? SnapshotChanged;

        public IReadOnlyList<DownloadTaskSnapshot> Tasks => _tasks;

        public IReadOnlyList<DownloadFile> LastFiles { get; private set; } = [];

        public string? CanceledName { get; private set; }

        public int RetryCount { get; private set; }

        public Task<DownloadTaskSnapshot> DownloadAsync(string name, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken = default)
        {
            LastFiles = files;
            _files[name] = files;
            var snapshot = new DownloadTaskSnapshot(name, DownloadTaskState.Succeeded, files.Count, files.Count, 0, 1, "下载完成")
            {
                CanRetry = false,
                PrimaryLocalPath = files.FirstOrDefault()?.LocalPath ?? "",
                LocalPaths = files.Select(file => file.LocalPath).ToList()
            };
            UpsertSnapshot(snapshot);
            SnapshotChanged?.Invoke(this, snapshot);
            return Task.FromResult(snapshot);
        }

        public bool Cancel(string name)
        {
            CanceledName = name;
            return _tasks.Any(task => string.Equals(task.Name, name, StringComparison.OrdinalIgnoreCase) && task.CanCancel);
        }

        public int CancelAllRunning()
        {
            var count = 0;
            foreach (var task in _tasks.Where(task => task.State == DownloadTaskState.Running).ToList())
            {
                if (Cancel(task.Name))
                {
                    count++;
                }
            }

            return count;
        }

        public Task<DownloadTaskSnapshot?> RetryAsync(string name, CancellationToken cancellationToken = default)
        {
            RetryCount++;
            if (!_files.TryGetValue(name, out var files))
            {
                return Task.FromResult<DownloadTaskSnapshot?>(null);
            }

            return RetryKnownAsync(name, files, cancellationToken);
        }

        public void AddSnapshot(DownloadTaskSnapshot snapshot, IReadOnlyList<DownloadFile>? files = null)
        {
            UpsertSnapshot(snapshot);
            if (files is not null)
            {
                _files[snapshot.Name] = files;
            }

            SnapshotChanged?.Invoke(this, snapshot);
        }

        public int ClearFinished()
        {
            return _tasks.RemoveAll(task => task.State is DownloadTaskState.Succeeded or DownloadTaskState.Failed or DownloadTaskState.Canceled);
        }

        private void UpsertSnapshot(DownloadTaskSnapshot snapshot)
        {
            var index = _tasks.FindIndex(task => string.Equals(task.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _tasks[index] = snapshot;
                return;
            }

            _tasks.Add(snapshot);
        }

        private async Task<DownloadTaskSnapshot?> RetryKnownAsync(string name, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken)
        {
            return await DownloadAsync(name, files, cancellationToken);
        }
    }

    private sealed class FakeMinecraftDiscoveryService(string root, IReadOnlyList<MinecraftInstance>? instances = null) : IMinecraftDiscoveryService
    {
        public string GetDefaultMinecraftRoot()
        {
            return root;
        }

        public Task<IReadOnlyList<MinecraftInstance>> ScanAsync(string? rootPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(instances ?? []);
        }

        public MinecraftInstance InspectInstance(string rootPath, string versionPath, IReadOnlySet<string> availableInstances)
        {
            throw new NotSupportedException();
        }
    }
}
