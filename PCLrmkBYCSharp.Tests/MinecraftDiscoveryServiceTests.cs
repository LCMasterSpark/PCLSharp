using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.Launch;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Tests;

public sealed class MinecraftDiscoveryServiceTests
{
    [Fact]
    public async Task ScanAsyncReturnsEmptyWhenVersionsFolderDoesNotExist()
    {
        using var temp = new TempDirectory();
        var service = new MinecraftDiscoveryService();

        var instances = await service.ScanAsync(temp.Path);

        Assert.Empty(instances);
    }

    [Fact]
    public async Task ScanAsyncReadsStandardVanillaInstance()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T13:25:51+00:00",
          "time": "2023-06-12T13:25:51+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "5" },
          "arguments": { "game": ["--demo"], "jvm": ["-Xmx2G"] },
          "libraries": [{ "name": "com.mojang:brigadier:1.0.18" }]
        }
        """);
        var service = new MinecraftDiscoveryService();

        var instances = await service.ScanAsync(temp.Path);

        var instance = Assert.Single(instances);
        Assert.Equal("1.20.1", instance.Name);
        Assert.Equal(MinecraftInstanceState.Ready, instance.State);
        Assert.Equal("1.20.1", instance.Version.VanillaVersion);
        Assert.Equal("release", instance.Version.Type);
        Assert.Equal("5", instance.Version.AssetsIndex);
        Assert.Equal(1, instance.Version.LibraryCount);
        Assert.True(instance.Version.HasModernArguments);
        Assert.False(instance.Version.HasLegacyMinecraftArguments);
        Assert.False(instance.HasError);
    }

    [Fact]
    public async Task ScanAsyncHonorsOldPclSetupVersionMetadata()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "Skyblock Burgeria v3.0", """
        {
          "id": "Skyblock Burgeria v3.0",
          "type": "release",
          "releaseTime": "2023-06-12T13:25:51+00:00",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": []
        }
        """);
        var setupPath = Path.Combine(temp.Path, "versions", "Skyblock Burgeria v3.0", "PCL", "Setup.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(setupPath)!);
        await File.WriteAllLinesAsync(setupPath, [
            "VersionVanillaName:1.20.1",
            "VersionForge:47.2.32"
        ]);
        var service = new MinecraftDiscoveryService();

        var instance = Assert.Single(await service.ScanAsync(temp.Path));
        var requirement = new JavaSelectorService().GetRequirement(instance);

        Assert.Equal("1.20.1", instance.Version.VanillaVersion);
        Assert.True(instance.Version.HasForge);
        Assert.Equal("Forge", instance.LoaderSummary);
        Assert.Equal("Java 17", requirement.DisplayText);
    }

    [Fact]
    public void MinecraftInstanceUsesOriginalPclImageResourcePathsForVersionIcons()
    {
        var release = CreateInstance("release");
        var snapshot = CreateInstance("snapshot");
        var fabric = CreateInstance("release", hasFabric: true);
        var neoforge = CreateInstance("release", hasNeoForge: true);
        var forge = CreateInstance("release", hasForge: true);
        var error = CreateInstance("release", state: MinecraftInstanceState.InvalidJson);

        Assert.Equal("/Resources/Images/ReleaseTypes/Release.png", release.IconPath);
        Assert.Equal("/Resources/Images/ReleaseTypes/Beta.png", snapshot.IconPath);
        Assert.Equal("/Resources/Images/Blocks/Fabric.png", fabric.IconPath);
        Assert.Equal("/Resources/Images/Blocks/NeoForge.png", neoforge.IconPath);
        Assert.Equal("/Resources/Images/Blocks/Anvil.png", forge.IconPath);
        Assert.Equal("/Resources/Images/Icons/Disabled.png", error.IconPath);
    }

    [Fact]
    public void InstancePageDetailSectionsSwitchContentWithoutTabs()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        Assert.Equal([0, 1, 2], viewModel.InstanceDetailSections.Select(section => section.Value));
        Assert.True(viewModel.IsInstanceOverviewSectionSelected);
        Assert.False(viewModel.IsInstanceModSectionSelected);

        viewModel.SelectedInstanceDetailSection = 1;

        Assert.False(viewModel.IsInstanceOverviewSectionSelected);
        Assert.True(viewModel.IsInstanceModSectionSelected);
        Assert.False(viewModel.IsInstanceLaunchSettingsSectionSelected);

        viewModel.SelectedInstanceDetailSection = 2;

        Assert.True(viewModel.IsInstanceLaunchSettingsSectionSelected);
        Assert.False(viewModel.IsInstanceModSectionSelected);
    }

    [Fact]
    public async Task ScanAsyncDetectsMissingJson()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "versions", "Broken"));
        var service = new MinecraftDiscoveryService();

        var instances = await service.ScanAsync(temp.Path);

        var instance = Assert.Single(instances);
        Assert.Equal(MinecraftInstanceState.MissingJson, instance.State);
    }

    [Fact]
    public async Task ScanAsyncDetectsInvalidJson()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "Broken", "{ bad json");
        var service = new MinecraftDiscoveryService();

        var instances = await service.ScanAsync(temp.Path);

        var instance = Assert.Single(instances);
        Assert.Equal(MinecraftInstanceState.InvalidJson, instance.State);
    }

    [Fact]
    public async Task ScanAsyncDetectsMissingInheritedVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "Fabric", """
        {
          "id": "Fabric",
          "inheritsFrom": "1.20.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }]
        }
        """);
        var service = new MinecraftDiscoveryService();

        var instances = await service.ScanAsync(temp.Path);

        var instance = Assert.Single(instances);
        Assert.Equal(MinecraftInstanceState.MissingInherit, instance.State);
        Assert.True(instance.Version.HasFabric);
    }

    [Fact]
    public async Task ScanAsyncAcceptsExistingInheritedVersionAndSortsReadyByReleaseTime()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "Forge", """
        {
          "id": "Forge",
          "inheritsFrom": "1.20.1",
          "releaseTime": "2023-06-13T12:00:00+00:00",
          "libraries": [{ "name": "net.minecraftforge:forge:1.20.1-47.0.0" }]
        }
        """);
        var service = new MinecraftDiscoveryService();

        var instances = await service.ScanAsync(temp.Path);

        Assert.Collection(
            instances,
            first =>
            {
                Assert.Equal("Forge", first.Name);
                Assert.Equal(MinecraftInstanceState.Ready, first.State);
                Assert.True(first.Version.HasForge);
            },
            second => Assert.Equal("1.20.1", second.Name),
            third => Assert.Equal("1.19.4", third.Name));
    }

    [Fact]
    public async Task InstancePageViewModelRestoresSelectedInstanceFromSettings()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.19.4");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();

        Assert.Equal("1.19.4", viewModel.SelectedInstance?.Name);
    }

    [Fact]
    public async Task InstancePageViewModelShowsSelectedVersionTechnicalDetails()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "FabricPack", """
        {
          "id": "FabricPack",
          "type": "release",
          "releaseTime": "2023-06-12T13:25:51+00:00",
          "time": "2023-06-13T13:25:51+00:00",
          "inheritsFrom": "1.20.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "assetIndex": { "id": "5" },
          "minecraftArguments": "--username ${auth_player_name}",
          "libraries": [
            { "name": "net.fabricmc:fabric-loader:0.15.0" },
            { "name": "com.mojang:brigadier:1.0.18" }
          ]
        }
        """);
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "FabricPack");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();

        Assert.Equal("FabricPack", viewModel.SelectedInstance?.Name);
        Assert.Contains("角色：正在管理", viewModel.SelectedInstanceOverview);
        Assert.Contains("状态：可启动", viewModel.SelectedInstanceOverview);
        Assert.Contains("游戏目录模式：版本隔离", viewModel.SelectedInstanceOverview);
        Assert.Contains(Path.Combine(temp.Path, "versions", "FabricPack"), viewModel.SelectedInstanceOverview);
        Assert.Contains("版本 ID：FabricPack", viewModel.SelectedInstanceTechnicalDetail);
        Assert.Contains("加载器：Fabric", viewModel.SelectedInstanceTechnicalDetail);
        Assert.Contains("继承版本：1.20.1", viewModel.SelectedInstanceTechnicalDetail);
        Assert.Contains("主类：net.fabricmc.loader.impl.launch.knot.KnotClient", viewModel.SelectedInstanceTechnicalDetail);
        Assert.Contains("资源索引：5", viewModel.SelectedInstanceTechnicalDetail);
        Assert.Contains("依赖库：2 个", viewModel.SelectedInstanceTechnicalDetail);
        Assert.Contains("参数格式：旧版 minecraftArguments", viewModel.SelectedInstanceTechnicalDetail);
        Assert.Contains(Path.Combine(temp.Path, "versions", "FabricPack"), viewModel.SelectedInstanceTechnicalDetail);

        viewModel.VersionIsolationEnabled = false;

        Assert.Contains("游戏目录模式：公共 .minecraft", viewModel.SelectedInstanceOverview);
        Assert.Contains("游戏目录：" + Path.GetFullPath(temp.Path), viewModel.SelectedInstanceOverview);
    }

    [Fact]
    public async Task InstancePageViewModelCanSetSelectedInstanceAsLaunchVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        viewModel.UseSelectedInstanceForLaunchCommand.Execute(null);

        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        var launchRow = Assert.Single(viewModel.InstanceRows, row => row.IsLaunchVersion);
        Assert.Equal("1.20.1", launchRow.Instance?.Name);
        Assert.Equal("启动版本", launchRow.LaunchText);
        Assert.True(launchRow.IsManagedVersion);
        Assert.Equal("正在管理", launchRow.ManagedText);
        Assert.True(launchRow.IsLaunchAndManagedVersion);
        Assert.Equal("启动并正在管理", launchRow.RoleText);
        Assert.Contains("已设为启动版本", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageUseSelectedInstanceForLaunchUpdatesRowsWithoutRebuilding()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        var firstRow = viewModel.InstanceRows.First();
        var rowCount = viewModel.InstanceRows.Count;
        var other = viewModel.Instances.Single(instance => instance.Name == "1.19.4");
        viewModel.SelectInstanceCommand.Execute(other);

        viewModel.UseSelectedInstanceForLaunchCommand.Execute(null);

        Assert.Equal(rowCount, viewModel.InstanceRows.Count);
        Assert.Same(firstRow, viewModel.InstanceRows.First());
        Assert.Equal("1.19.4", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("1.19.4", Assert.Single(viewModel.InstanceRows, row => row.IsLaunchVersion).Instance?.Name);
    }

    [Fact]
    public async Task InstancePageManagementSelectionDoesNotChangeLaunchVersionUntilConfirmed()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        var other = viewModel.Instances.Single(instance => instance.Name == "1.19.4");
        viewModel.SelectInstanceCommand.Execute(other);

        Assert.Equal("1.19.4", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.19.4", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Contains("正在管理：1.19.4", viewModel.VersionManagementSummary);
        Assert.Contains("启动版本：1.20.1", viewModel.VersionManagementSummary);
        Assert.False(viewModel.IsSelectedInstanceLaunchVersion);
        Assert.Equal("设为启动版本", viewModel.SelectedLaunchActionText);
        Assert.True(viewModel.UseSelectedInstanceForLaunchCommand.CanExecute(null));
        Assert.Equal("1.20.1", Assert.Single(viewModel.InstanceRows, row => row.IsLaunchVersion).Instance?.Name);
        var managedRow = Assert.Single(viewModel.InstanceRows, row => row.IsManagedVersion);
        Assert.Equal("1.19.4", managedRow.Instance?.Name);
        Assert.Equal("正在管理", managedRow.ManagedText);
        Assert.Equal("正在管理", managedRow.RoleText);
        var launchRow = Assert.Single(viewModel.InstanceRows, row => row.IsLaunchVersion);
        Assert.Equal("启动版本", launchRow.RoleText);

        var restored = CreateInstancePageViewModel(temp.Path, settings).ViewModel;
        await restored.RefreshAsync();

        Assert.Equal("1.19.4", restored.SelectedInstance?.Name);
        Assert.Contains("正在管理：1.19.4", restored.VersionManagementSummary);
        Assert.Contains("启动版本：1.20.1", restored.VersionManagementSummary);
        Assert.Equal("1.20.1", Assert.Single(restored.InstanceRows, row => row.IsLaunchVersion).Instance?.Name);
        Assert.Equal("1.19.4", Assert.Single(restored.InstanceRows, row => row.IsManagedVersion).Instance?.Name);

        var launchVersion = restored.Instances.Single(instance => instance.Name == "1.20.1");
        restored.SelectInstanceCommand.Execute(launchVersion);

        Assert.True(restored.IsSelectedInstanceLaunchVersion);
        Assert.Equal("已是启动版本", restored.SelectedLaunchActionText);
        Assert.False(restored.UseSelectedInstanceForLaunchCommand.CanExecute(null));
        var launchAndManagedRow = Assert.Single(restored.InstanceRows, row => row.IsLaunchVersion && row.IsManagedVersion);
        Assert.Equal("1.20.1", launchAndManagedRow.Instance?.Name);
        Assert.True(launchAndManagedRow.IsLaunchAndManagedVersion);
        Assert.Equal("启动并正在管理", launchAndManagedRow.RoleText);
    }

    [Fact]
    public async Task InstancePageViewModelShowsHiddenManagedVersionFromLaunchSelector()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "hidden-1.19.4", """
        { "id": "hidden-1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        Directory.CreateDirectory(Path.Combine(temp.Path, "versions", "hidden-1.19.4", "PCL"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "versions", "hidden-1.19.4", "PCL", "Setup.ini"), "DisplayType:1");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "hidden-1.19.4");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();

        Assert.True(viewModel.ShowHiddenInstances);
        Assert.Equal("hidden-1.19.4", viewModel.SelectedInstance?.Name);
        Assert.Single(viewModel.Instances);
        var managedRow = Assert.Single(viewModel.InstanceRows, row => row.IsManagedVersion);
        Assert.Equal("hidden-1.19.4", managedRow.Instance?.Name);
        Assert.DoesNotContain(viewModel.InstanceRows, row => row.IsLaunchVersion);
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("hidden-1.19.4", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
    }

    [Fact]
    public async Task MainWindowF11TogglesHiddenVersionsOnInstancePageLikeOldPcl()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "hidden-1.19.4", """
        { "id": "hidden-1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        Directory.CreateDirectory(Path.Combine(temp.Path, "versions", "hidden-1.19.4", "PCL"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "versions", "hidden-1.19.4", "PCL", "Setup.ini"), "DisplayType:1");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "1.20.1");
        var instancePage = CreateInstancePageViewModel(temp.Path, settings).ViewModel;
        await instancePage.RefreshAsync();
        var navigation = new TestNavigationService(instancePage);
        var mainWindow = new MainWindowViewModel(navigation, settings, new NullLoggerService());
        mainWindow.NavigateCommand.Execute(PageRoute.Instance);

        Assert.False(instancePage.ShowHiddenInstances);
        Assert.DoesNotContain(instancePage.InstanceRows, row => row.Instance?.Name == "hidden-1.19.4");

        mainWindow.ToggleHiddenVersionsCommand.Execute(null);

        Assert.True(instancePage.ShowHiddenInstances);
        Assert.Contains(instancePage.InstanceRows, row => row.Instance?.Name == "hidden-1.19.4");
        Assert.Contains("隐藏版本", mainWindow.StatusText);

        mainWindow.ToggleHiddenVersionsCommand.Execute(null);

        Assert.False(instancePage.ShowHiddenInstances);
        Assert.DoesNotContain(instancePage.InstanceRows, row => row.Instance?.Name == "hidden-1.19.4");
        Assert.Contains("可用版本", mainWindow.StatusText);
    }

    [Fact]
    public async Task InstancePageViewModelCanStarAndHideVersionUsingOldSetupIni()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.ToggleSelectedInstanceStarCommand.ExecuteAsync(null);
        await viewModel.ToggleSelectedInstanceHiddenCommand.ExecuteAsync(null);

        var setupPath = Path.Combine(temp.Path, "versions", "1.20.1", "PCL", "Setup.ini");
        var setup = await File.ReadAllTextAsync(setupPath);
        Assert.Contains("IsStar:True", setup);
        Assert.Contains("DisplayType:1", setup);
        Assert.Empty(viewModel.Instances);
        Assert.Equal(1, viewModel.HiddenCount);
        viewModel.ShowHiddenInstances = true;
        Assert.True(viewModel.SelectedInstance?.IsStar);
        Assert.True(viewModel.SelectedInstance?.IsHidden);
        Assert.Equal("隐藏的版本", viewModel.SelectedInstance?.GroupName);
        Assert.Equal("取消收藏", viewModel.SelectedStarActionText);
        Assert.Equal("取消隐藏", viewModel.SelectedHiddenActionText);
        Assert.Contains("标记：已收藏，已隐藏", viewModel.SelectedInstanceOverview);
        Assert.Contains("分类：隐藏的版本", viewModel.SelectedInstanceOverview);
    }

    [Fact]
    public async Task InstancePageViewModelListQuickActionsOperateOnTargetVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "Alpha", """
        { "id": "Alpha", "releaseTime": "2023-01-01T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "Beta", """
        { "id": "Beta", "releaseTime": "2023-02-01T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "Alpha");
        var folders = new CaptureFolderOpenService();
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, folders: folders).ViewModel;

        await viewModel.RefreshAsync();
        var beta = viewModel.Instances.Single(instance => instance.Name == "Beta");

        viewModel.UseInstanceForLaunchFromListCommand.Execute(beta);

        Assert.Equal("Beta", viewModel.SelectedInstance?.Name);
        Assert.Equal("Beta", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("Beta", Assert.Single(viewModel.InstanceRows, row => row.IsLaunchVersion).Instance?.Name);

        viewModel.OpenInstanceFolderFromListCommand.Execute(beta);

        Assert.Equal(Path.Combine(temp.Path, "versions", "Beta"), Assert.Single(folders.OpenedPaths));

        await viewModel.ToggleInstanceStarFromListCommand.ExecuteAsync(beta);

        Assert.Equal("Beta", viewModel.SelectedInstance?.Name);
        Assert.Contains("IsStar:True", await File.ReadAllTextAsync(Path.Combine(temp.Path, "versions", "Beta", "PCL", "Setup.ini")));
        Assert.Contains(viewModel.InstanceRows, row => row.Instance?.Name == "Beta" && row.Instance.IsStar);
    }

    [Fact]
    public async Task InstancePageViewModelGroupsVersionManagementRowsLikeOldPcl()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "fabric-1.20.1", """
        { "id": "fabric-1.20.1", "inheritsFrom": "1.20.1", "releaseTime": "2023-06-13T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        WriteVersionJson(temp.Path, "hidden-1.19.4", """
        { "id": "hidden-1.19.4", "type": "release", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        Directory.CreateDirectory(Path.Combine(temp.Path, "versions", "fabric-1.20.1", "PCL"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "versions", "fabric-1.20.1", "PCL", "Setup.ini"), "IsStar:True");
        Directory.CreateDirectory(Path.Combine(temp.Path, "versions", "hidden-1.19.4", "PCL"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "versions", "hidden-1.19.4", "PCL", "Setup.ini"), "DisplayType:1");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasInstanceRows);
        Assert.Contains(viewModel.InstanceRows, row => row.IsHeader && row.GroupTitle == "收藏夹");
        Assert.Contains(viewModel.InstanceRows, row => row.IsHeader && row.GroupTitle.StartsWith("Fabric 版本", StringComparison.Ordinal));
        Assert.Equal(2, viewModel.InstanceRows.Count(row => row.Instance?.Name == "fabric-1.20.1"));
        Assert.DoesNotContain(viewModel.InstanceRows, row => row.Instance?.Name == "hidden-1.19.4");

        viewModel.ShowHiddenInstances = true;
        Assert.Single(viewModel.Instances);
        Assert.Contains(viewModel.InstanceRows, row => row.IsHeader && row.GroupTitle.StartsWith("隐藏的版本", StringComparison.Ordinal));
        var hiddenRow = Assert.Single(viewModel.InstanceRows, row => row.IsSelectable);
        viewModel.SelectInstanceCommand.Execute(hiddenRow.Instance);

        Assert.Equal("hidden-1.19.4", viewModel.SelectedInstance?.Name);
    }

    [Fact]
    public async Task InstancePageViewModelKeepsVersionRowsStableWhenChangingManagedVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "Alpha", """
        { "id": "Alpha", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "Beta", """
        { "id": "Beta", "type": "release", "releaseTime": "2023-06-13T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();

        var initialRows = viewModel.InstanceRows.ToList();
        var alphaRow = Assert.Single(initialRows, row => row.Instance?.Name == "Alpha");
        var betaRow = Assert.Single(initialRows, row => row.Instance?.Name == "Beta");

        viewModel.SelectedInstance = betaRow.Instance;

        Assert.Equal(initialRows.Count, viewModel.InstanceRows.Count);
        Assert.Same(alphaRow, Assert.Single(viewModel.InstanceRows, row => row.Instance?.Name == "Alpha"));
        Assert.Same(betaRow, Assert.Single(viewModel.InstanceRows, row => row.Instance?.Name == "Beta"));
        Assert.False(alphaRow.IsManagedVersion);
        Assert.True(betaRow.IsManagedVersion);
    }

    [Fact]
    public async Task InstancePageViewModelUsesSharedVersionSortMode()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "Alpha", """
        { "id": "Alpha", "type": "release", "releaseTime": "2023-01-01T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "Beta", """
        { "id": "Beta", "type": "release", "releaseTime": "2023-02-01T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "Gamma", """
        { "id": "Gamma", "type": "release", "releaseTime": "2023-03-01T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.VersionSortMode, 2);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();

        Assert.Equal(new[] { "Gamma", "Beta", "Alpha" }, viewModel.InstanceRows.Where(row => row.IsSelectable).Select(row => row.Name));

        viewModel.VersionSortMode = 1;

        Assert.Equal(1, settings.Get(AppSettingKeys.VersionSortMode, 0));
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, viewModel.InstanceRows.Where(row => row.IsSelectable).Select(row => row.Name));
    }

    [Fact]
    public async Task InstancePageViewModelSyncsSharedSortModeWhenNavigatedBack()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "Alpha", """
        { "id": "Alpha", "type": "release", "releaseTime": "2023-03-01T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "Beta", """
        { "id": "Beta", "type": "release", "releaseTime": "2023-01-01T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "Gamma", """
        { "id": "Gamma", "type": "release", "releaseTime": "2023-02-01T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        Assert.Equal(new[] { "Alpha", "Gamma", "Beta" }, viewModel.InstanceRows.Where(row => row.IsSelectable).Select(row => row.Name));

        settings.Set(AppSettingKeys.VersionSortMode, 1);
        await viewModel.OnNavigatedToAsync();

        Assert.Equal(1, viewModel.VersionSortMode);
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, viewModel.InstanceRows.Where(row => row.IsSelectable).Select(row => row.Name));
    }

    [Fact]
    public async Task InstancePageViewModelSyncsMinecraftRootPathWhenNavigatedBackAfterOtherPageChange()
    {
        using var temp = new TempDirectory();
        var rootA = Path.Combine(temp.Path, "A");
        var rootB = Path.Combine(temp.Path, "B");
        WriteVersionJson(rootA, "RootA", """
        { "id": "RootA", "releaseTime": "2023-01-01T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(rootB, "RootB", """
        { "id": "RootB", "releaseTime": "2023-02-01T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, rootA);
        var viewModel = CreateInstancePageViewModel(rootA, settings).ViewModel;

        await viewModel.OnNavigatedToAsync();
        Assert.Equal("RootA", viewModel.SelectedInstance?.Name);

        settings.Set(AppSettingKeys.MinecraftRootPath, rootB);
        await viewModel.OnNavigatedToAsync();

        Assert.Equal(Path.GetFullPath(rootB), viewModel.MinecraftRootPath);
        Assert.Equal("RootB", viewModel.SelectedInstance?.Name);
        Assert.Contains(viewModel.InstanceRows, row => row.Instance?.Name == "RootB");
        Assert.DoesNotContain(viewModel.InstanceRows, row => row.Instance?.Name == "RootA");
        Assert.Equal(Path.GetFullPath(rootB), settings.Get(AppSettingKeys.MinecraftRootPath, ""));
    }

    [Fact]
    public async Task InstancePageViewModelSearchesManagementListWithoutChangingLaunchVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "type": "release", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "fabric-1.20.1", """
        { "id": "fabric-1.20.1", "inheritsFrom": "1.20.1", "releaseTime": "2023-06-13T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        WriteVersionJson(temp.Path, "hidden-1.19.4", """
        { "id": "hidden-1.19.4", "type": "release", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        Directory.CreateDirectory(Path.Combine(temp.Path, "versions", "hidden-1.19.4", "PCL"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "versions", "hidden-1.19.4", "PCL", "Setup.ini"), "DisplayType:1");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();

        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);
        Assert.Equal(2, viewModel.InstanceListVisibleCount);
        Assert.Contains("显示 2 / 2 个可用版本", viewModel.InstanceListSummary);
        viewModel.InstanceSearchText = "fabric";

        Assert.Single(viewModel.Instances);
        Assert.Equal(1, viewModel.InstanceListVisibleCount);
        Assert.Contains("搜索：fabric", viewModel.InstanceListSummary);
        Assert.Equal("fabric-1.20.1", Assert.Single(viewModel.InstanceRows, row => row.IsSelectable).Instance?.Name);
        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));

        viewModel.SelectInstanceCommand.Execute(viewModel.Instances.Single());

        Assert.Equal("fabric-1.20.1", viewModel.SelectedInstance?.Name);
        Assert.Equal("fabric-1.20.1", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.False(viewModel.IsSelectedInstanceLaunchVersion);

        viewModel.InstanceSearchText = "hidden";

        Assert.Empty(viewModel.Instances);
        Assert.False(viewModel.HasInstanceRows);
        Assert.Equal("没有匹配当前搜索条件的版本。", viewModel.InstanceListEmptyText);
        Assert.Equal("fabric-1.20.1", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));

        viewModel.ShowHiddenInstances = true;

        Assert.Single(viewModel.Instances);
        Assert.Equal(1, viewModel.InstanceListVisibleCount);
        Assert.Contains("隐藏版本", viewModel.InstanceListSummary);
        Assert.Equal("hidden-1.19.4", Assert.Single(viewModel.InstanceRows, row => row.IsSelectable).Instance?.Name);
    }

    [Fact]
    public async Task InstancePageViewModelDeletesVersionAfterConfirmation()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, confirmDelete: true).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.DeleteSelectedInstanceCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(Path.Combine(temp.Path, "versions", "1.20.1")));
        Assert.Empty(viewModel.Instances);
        Assert.Equal("", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Contains("已删除", viewModel.StatusMessage);
        Assert.Contains("当前没有可用启动版本", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelSwitchesLaunchVersionAfterDeletingCurrentOne()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, "1.20.1");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, confirmDelete: true).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.DeleteSelectedInstanceCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(Path.Combine(temp.Path, "versions", "1.20.1")));
        Assert.Equal("1.19.4", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.19.4", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("1.19.4", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Equal("1.19.4", Assert.Single(viewModel.InstanceRows, row => row.IsLaunchVersion).Instance?.Name);
        Assert.Contains("已切换到 1.19.4", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelReturnsToLaunchVersionAfterDeletingManagedOnlyVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "1.19.4", """
        { "id": "1.19.4", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "1.19.4");
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, "1.20.1");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, confirmDelete: true).ViewModel;

        await viewModel.RefreshAsync();
        Assert.Equal("1.19.4", viewModel.SelectedInstance?.Name);

        await viewModel.DeleteSelectedInstanceCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(Path.Combine(temp.Path, "versions", "1.19.4")));
        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("1.20.1", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Equal("1.20.1", Assert.Single(viewModel.InstanceRows, row => row.IsManagedVersion).Instance?.Name);
        Assert.Contains("正在管理 1.20.1", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelDeletesVersionFromListWithoutChangingLaunchVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "LaunchVersion", """
        { "id": "LaunchVersion", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "ManagedLater", """
        { "id": "ManagedLater", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "LaunchVersion");
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "LaunchVersion");
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, "LaunchVersion");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, confirmDelete: true).ViewModel;

        await viewModel.RefreshAsync();
        var target = Assert.Single(viewModel.Instances, instance => instance.Name == "ManagedLater");
        await viewModel.DeleteInstanceFromListCommand.ExecuteAsync(target);

        Assert.True(Directory.Exists(Path.Combine(temp.Path, "versions", "LaunchVersion")));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "versions", "ManagedLater")));
        Assert.Equal("LaunchVersion", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("LaunchVersion", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Equal("LaunchVersion", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal("LaunchVersion", viewModel.SelectedInstance?.Name);
        Assert.True(viewModel.IsSelectedInstanceLaunchVersion);
        Assert.Contains("ManagedLater 已删除", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelKeepsVersionWhenDeleteCanceled()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, confirmDelete: false).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.DeleteSelectedInstanceCommand.ExecuteAsync(null);

        Assert.True(Directory.Exists(Path.Combine(temp.Path, "versions", "1.20.1")));
        Assert.Contains("已取消", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelRenamesVersionLikeOldPcl()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "OldName", """
        { "id": "OldName", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var oldPath = Path.Combine(temp.Path, "versions", "OldName");
        File.WriteAllText(Path.Combine(oldPath, "OldName.jar"), "");
        Directory.CreateDirectory(Path.Combine(oldPath, "OldName-natives"));
        Directory.CreateDirectory(Path.Combine(oldPath, "PCL"));
        File.WriteAllText(Path.Combine(oldPath, "PCL", "Setup.ini"), "CustomInfo:" + oldPath);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "OldName");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, promptValue: "NewName").ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.RenameSelectedInstanceCommand.ExecuteAsync(null);

        var newPath = Path.Combine(temp.Path, "versions", "NewName");
        Assert.False(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(newPath));
        Assert.True(File.Exists(Path.Combine(newPath, "NewName.json")));
        Assert.True(File.Exists(Path.Combine(newPath, "NewName.jar")));
        Assert.True(Directory.Exists(Path.Combine(newPath, "NewName-natives")));
        Assert.Contains("\"id\": \"NewName\"", File.ReadAllText(Path.Combine(newPath, "NewName.json")));
        Assert.Contains(newPath, File.ReadAllText(Path.Combine(newPath, "PCL", "Setup.ini")));
        Assert.Equal("NewName", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("NewName", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Equal("NewName", viewModel.SelectedInstance?.Name);
        Assert.Contains("启动版本已同步", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelRenamesVersionFromListWithoutChangingLaunchVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "LaunchVersion", """
        { "id": "LaunchVersion", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "ManagedLater", """
        { "id": "ManagedLater", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "LaunchVersion");
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "LaunchVersion");
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, "LaunchVersion");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, promptValue: "RenamedManaged").ViewModel;

        await viewModel.RefreshAsync();
        var target = Assert.Single(viewModel.Instances, instance => instance.Name == "ManagedLater");
        await viewModel.RenameInstanceFromListCommand.ExecuteAsync(target);

        Assert.True(Directory.Exists(Path.Combine(temp.Path, "versions", "LaunchVersion")));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "versions", "ManagedLater")));
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "versions", "RenamedManaged")));
        Assert.Equal("LaunchVersion", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("LaunchVersion", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Equal("RenamedManaged", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal("RenamedManaged", viewModel.SelectedInstance?.Name);
        Assert.False(viewModel.IsSelectedInstanceLaunchVersion);
        Assert.DoesNotContain("启动版本已同步", viewModel.StatusMessage);
        Assert.Contains("ManagedLater 已重命名为 RenamedManaged", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelClonesVersionLikeOldPcl()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "OldName", """
        { "id": "OldName", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var oldPath = Path.Combine(temp.Path, "versions", "OldName");
        File.WriteAllText(Path.Combine(oldPath, "OldName.jar"), "");
        Directory.CreateDirectory(Path.Combine(oldPath, "OldName-natives"));
        Directory.CreateDirectory(Path.Combine(oldPath, "PCL"));
        File.WriteAllText(Path.Combine(oldPath, "PCL", "Setup.ini"), "CustomInfo:" + oldPath);
        File.WriteAllText(Path.Combine(oldPath, "options.txt"), "lang:zh_cn");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "OldName");
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, "OldName");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, promptValue: "CopiedName").ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.CloneSelectedInstanceCommand.ExecuteAsync(null);

        var newPath = Path.Combine(temp.Path, "versions", "CopiedName");
        Assert.True(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(newPath));
        Assert.True(File.Exists(Path.Combine(oldPath, "OldName.json")));
        Assert.True(File.Exists(Path.Combine(newPath, "CopiedName.json")));
        Assert.True(File.Exists(Path.Combine(newPath, "CopiedName.jar")));
        Assert.True(Directory.Exists(Path.Combine(newPath, "CopiedName-natives")));
        Assert.True(File.Exists(Path.Combine(newPath, "options.txt")));
        Assert.Contains("\"id\": \"CopiedName\"", File.ReadAllText(Path.Combine(newPath, "CopiedName.json")));
        Assert.Contains(newPath, File.ReadAllText(Path.Combine(newPath, "PCL", "Setup.ini")));
        Assert.Equal("OldName", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("OldName", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Equal("CopiedName", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal("CopiedName", viewModel.SelectedInstance?.Name);
        Assert.Contains("已复制", viewModel.StatusMessage);
        Assert.Contains("正在管理 CopiedName", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelClonesVersionFromListWithoutChangingLaunchVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "LaunchVersion", """
        { "id": "LaunchVersion", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        WriteVersionJson(temp.Path, "ManagedLater", """
        { "id": "ManagedLater", "releaseTime": "2023-03-14T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "LaunchVersion");
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "LaunchVersion");
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, "LaunchVersion");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, promptValue: "CopiedManaged").ViewModel;

        await viewModel.RefreshAsync();
        var target = Assert.Single(viewModel.Instances, instance => instance.Name == "ManagedLater");
        await viewModel.CloneInstanceFromListCommand.ExecuteAsync(target);

        Assert.True(Directory.Exists(Path.Combine(temp.Path, "versions", "LaunchVersion")));
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "versions", "ManagedLater")));
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "versions", "CopiedManaged")));
        Assert.Equal("LaunchVersion", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("LaunchVersion", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Equal("CopiedManaged", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal("CopiedManaged", viewModel.SelectedInstance?.Name);
        Assert.False(viewModel.IsSelectedInstanceLaunchVersion);
        Assert.Contains("ManagedLater 已复制为 CopiedManaged", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelKeepsSelectionWhenCloneCanceled()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "OldName", """
        { "id": "OldName", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "OldName");
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "OldName");
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, "OldName");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, promptValue: null).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.CloneSelectedInstanceCommand.ExecuteAsync(null);

        Assert.True(Directory.Exists(Path.Combine(temp.Path, "versions", "OldName")));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "versions", "OldName - 副本")));
        Assert.Equal("OldName", viewModel.SelectedInstance?.Name);
        Assert.Equal("OldName", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("OldName", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal("OldName", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Contains("已取消复制版本", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelImportsExternalVersionFolder()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "external", "ImportedPack");
        Directory.CreateDirectory(sourcePath);
        File.WriteAllText(Path.Combine(sourcePath, "external-version.json"), """
        { "id": "ExternalId", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        File.WriteAllText(Path.Combine(sourcePath, "notes.txt"), "keep me");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, fileDialogs: new FolderFileDialogService(sourcePath)).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.ImportInstanceCommand.ExecuteAsync(null);

        var importedPath = Path.Combine(temp.Path, "versions", "ImportedPack");
        Assert.True(Directory.Exists(importedPath));
        Assert.True(File.Exists(Path.Combine(importedPath, "ImportedPack.json")));
        Assert.True(File.Exists(Path.Combine(importedPath, "notes.txt")));
        Assert.Contains("\"id\": \"ImportedPack\"", File.ReadAllText(Path.Combine(importedPath, "ImportedPack.json")));
        Assert.Equal("ImportedPack", viewModel.SelectedInstance?.Name);
        Assert.Equal("ImportedPack", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Contains("版本已导入", viewModel.StatusMessage);
        Assert.Contains("正在管理 ImportedPack", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelKeepsSelectionWhenImportCanceled()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.SelectedInstanceName, "1.20.1");
        settings.Set(AppSettingKeys.InstanceManageSelectedName, "1.20.1");
        new MinecraftSelectionService().WriteSelectedInstanceName(temp.Path, "1.20.1");
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.ImportInstanceCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Instances);
        Assert.Equal("1.20.1", viewModel.SelectedInstance?.Name);
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.SelectedInstanceName, ""));
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.InstanceManageSelectedName, ""));
        Assert.Equal("1.20.1", new MinecraftSelectionService().ReadSelectedInstanceName(temp.Path));
        Assert.Contains("已取消导入版本", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelExportsLaunchScriptForSelectedVersion()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var target = Path.Combine(temp.Path, "启动 1.20.1.bat");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.LoginLegacyName, "Alex¨Steve");
        var pipeline = new TestLaunchPipelineService();
        var fileDialogs = new SaveFileDialogService(target);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, launchPipeline: pipeline, fileDialogs: fileDialogs).ViewModel;

        await viewModel.RefreshAsync();
        viewModel.VersionJavaPath = "使用全局设置";
        await viewModel.ExportSelectedInstanceScriptCommand.ExecuteAsync(null);

        Assert.Equal(1, pipeline.LaunchCalls);
        Assert.False(pipeline.LastRequest?.StartProcess);
        Assert.Equal(target, pipeline.LastRequest?.SaveBatchPath);
        Assert.Equal("1.20.1", pipeline.LastRequest?.Instance?.Name);
        Assert.Null(pipeline.LastRequest?.JavaPath);
        Assert.Equal("Alex", pipeline.LastRequest?.LegacyName);
        Assert.Equal("启动 1.20.1.bat", fileDialogs.LastDefaultFileName);
        Assert.Contains(target, viewModel.StatusMessage);
    }

    [Fact]
    public async Task ModpackExportServiceCreatesModrinthPackWithOverrides()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T13:25:51+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": [
            { "name": "net.fabricmc:fabric-loader:0.15.0" },
            { "name": "org.quiltmc:quilt-loader:0.23.1" },
            { "name": "net.minecraftforge:forge:1.20.1-47.2.0" },
            { "name": "net.neoforged:neoforge:20.4.237" }
          ]
        }
        """);
        var instance = Assert.Single(await new MinecraftDiscoveryService().ScanAsync(temp.Path));
        var gameDirectory = instance.VersionPath;
        Directory.CreateDirectory(Path.Combine(gameDirectory, "mods"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "saves", "World"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "screenshots"));
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "mods", "example.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "resourcepacks", "pack.zip"), "pack");
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "saves", "World", "level.dat"), "save");
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "screenshots", "shot.png"), "png");
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "options.txt"), "lang:zh_cn");
        var target = Path.Combine(temp.Path, "export.mrpack");

        var result = await new ModpackExportService(new NullLoggerService())
            .ExportModrinthAsync(
                instance,
                gameDirectory,
                target,
                "Test Pack",
                "2.0.0",
                new ModpackExportOptions(IncludeSaves: false, IncludeScreenshots: true));

        Assert.Equal(4, result.OverrideFileCount);
        using var archive = ZipFile.OpenRead(target);
        Assert.NotNull(archive.GetEntry("modrinth.index.json"));
        Assert.NotNull(archive.GetEntry("overrides/mods/example.jar"));
        Assert.NotNull(archive.GetEntry("overrides/resourcepacks/pack.zip"));
        Assert.NotNull(archive.GetEntry("overrides/options.txt"));
        Assert.NotNull(archive.GetEntry("overrides/screenshots/shot.png"));
        Assert.Null(archive.GetEntry("overrides/saves/World/level.dat"));
        using var indexStream = archive.GetEntry("modrinth.index.json")!.Open();
        using var document = await System.Text.Json.JsonDocument.ParseAsync(indexStream);
        Assert.Equal("minecraft", document.RootElement.GetProperty("game").GetString());
        Assert.Equal("Test Pack", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("2.0.0", document.RootElement.GetProperty("versionId").GetString());
        var dependencies = document.RootElement.GetProperty("dependencies");
        Assert.Equal("1.20.1", dependencies.GetProperty("minecraft").GetString());
        Assert.Equal("0.15.0", dependencies.GetProperty("fabric-loader").GetString());
        Assert.Equal("0.23.1", dependencies.GetProperty("quilt-loader").GetString());
        Assert.Equal("47.2.0", dependencies.GetProperty("forge").GetString());
        Assert.Equal("20.4.237", dependencies.GetProperty("neoforge").GetString());
    }

    [Fact]
    public async Task ModpackExportServiceCreatesCurseForgeZipWhenTargetIsZip()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "forge-1.20.1", """
        {
          "id": "forge-1.20.1",
          "inheritsFrom": "1.20.1",
          "releaseTime": "2023-06-12T13:25:51+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": [
            { "name": "net.minecraftforge:forge:1.20.1-47.2.0" }
          ]
        }
        """);
        WriteVersionJson(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T13:25:51+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """);
        var instance = (await new MinecraftDiscoveryService().ScanAsync(temp.Path)).Single(item => item.Name == "forge-1.20.1");
        var gameDirectory = instance.VersionPath;
        Directory.CreateDirectory(Path.Combine(gameDirectory, "mods"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "config"));
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "mods", "example.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "config", "example.toml"), "config");
        var target = Path.Combine(temp.Path, "export.zip");

        var result = await new ModpackExportService(new NullLoggerService())
            .ExportModrinthAsync(
                instance,
                gameDirectory,
                target,
                "Curse Pack",
                "3.0.0",
                new ModpackExportOptions());

        Assert.Equal(2, result.OverrideFileCount);
        using var archive = ZipFile.OpenRead(target);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.Null(archive.GetEntry("modrinth.index.json"));
        Assert.NotNull(archive.GetEntry("overrides/mods/example.jar"));
        Assert.NotNull(archive.GetEntry("overrides/config/example.toml"));
        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        using var document = await System.Text.Json.JsonDocument.ParseAsync(manifestStream);
        Assert.Equal("minecraftModpack", document.RootElement.GetProperty("manifestType").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("manifestVersion").GetInt32());
        Assert.Equal("Curse Pack", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("3.0.0", document.RootElement.GetProperty("version").GetString());
        Assert.Equal("overrides", document.RootElement.GetProperty("overrides").GetString());
        var minecraft = document.RootElement.GetProperty("minecraft");
        Assert.Equal("1.20.1", minecraft.GetProperty("version").GetString());
        var loader = Assert.Single(minecraft.GetProperty("modLoaders").EnumerateArray());
        Assert.Equal("forge-47.2.0", loader.GetProperty("id").GetString());
        Assert.True(loader.GetProperty("primary").GetBoolean());
    }

    [Fact]
    public async Task InstancePageViewModelExportsSelectedVersionAsModrinthPack()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T13:25:51+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.LaunchArgumentIndieV2, 4);
        var target = Path.Combine(temp.Path, "instance.mrpack");
        var fileDialogs = new SaveFileDialogService(target);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, fileDialogs: fileDialogs).ViewModel;

        await viewModel.RefreshAsync();
        Directory.CreateDirectory(Path.Combine(viewModel.GameDirectoryPath, "mods"));
        Directory.CreateDirectory(Path.Combine(viewModel.GameDirectoryPath, "resourcepacks"));
        await File.WriteAllTextAsync(Path.Combine(viewModel.GameDirectoryPath, "mods", "example.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(viewModel.GameDirectoryPath, "resourcepacks", "pack.zip"), "pack");
        viewModel.ExportPackName = "整合包导出测试";
        viewModel.ExportPackVersion = "2.3.4";
        viewModel.ExportIncludeMods = false;

        await viewModel.ExportSelectedInstanceModpackCommand.ExecuteAsync(null);

        Assert.Equal("整合包导出测试.mrpack", fileDialogs.LastDefaultFileName);
        Assert.True(File.Exists(target));
        using var archive = ZipFile.OpenRead(target);
        Assert.NotNull(archive.GetEntry("modrinth.index.json"));
        Assert.Null(archive.GetEntry("overrides/mods/example.jar"));
        Assert.NotNull(archive.GetEntry("overrides/resourcepacks/pack.zip"));
        using var indexStream = archive.GetEntry("modrinth.index.json")!.Open();
        using var document = await System.Text.Json.JsonDocument.ParseAsync(indexStream);
        Assert.Equal("整合包导出测试", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("2.3.4", document.RootElement.GetProperty("versionId").GetString());
        Assert.Contains("整合包已导出", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelSavesLaunchSettingsPerInstance()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        viewModel.MinMemoryMb = 1024;
        viewModel.MaxMemoryMb = 6144;
        viewModel.LaunchWindowWidth = 1280;
        viewModel.LaunchWindowHeight = 720;
        viewModel.VersionArgumentTitle = "实例标题 {version}";
        viewModel.ExtraJvmArgs = "-XX:+UseG1GC";
        viewModel.ExtraGameArgs = "--demo";
        viewModel.ServerIp = "mc。example。com：25565";
        viewModel.VersionAdvanceRun = "echo instance {name}";
        viewModel.VersionAdvanceRunWait = false;
        viewModel.VersionRamType = 1;
        viewModel.VersionRamCustom = 25;
        viewModel.VersionRamOptimize = 2;
        viewModel.VersionServerLogin = 4;
        viewModel.VersionServerAuthServer = "https://auth.example.com/api/yggdrasil";
        viewModel.VersionServerAuthRegister = "https://auth.example.com/auth/register";
        viewModel.VersionServerAuthName = "Example Auth";
        viewModel.VersionDisplayType = MinecraftInstanceDisplayType.Rubbish;
        viewModel.VersionCustomInfo = "整合包测试版本";
        viewModel.DisableModUpdate = true;
        viewModel.IgnoreJavaCompatibility = true;
        viewModel.DisableFileCheck = true;
        await viewModel.SaveInstanceLaunchSettingsCommand.ExecuteAsync(null);

        Assert.Equal("整合包测试版本", viewModel.SelectedInstance?.CustomInfo);
        Assert.Equal("整合包测试版本", viewModel.SelectedInstance?.DisplayInfo);
        Assert.Contains("分类：不常用版本", viewModel.SelectedInstanceOverview);
        Assert.Contains("整合包测试版本", viewModel.SelectedInstanceDetail);
        Assert.Contains("已保存", viewModel.StatusMessage);

        var restored = CreateInstancePageViewModel(temp.Path, settings).ViewModel;
        await restored.RefreshAsync();

        Assert.Equal(1024, restored.MinMemoryMb);
        Assert.Equal(6144, restored.MaxMemoryMb);
        Assert.Equal(1280, restored.LaunchWindowWidth);
        Assert.Equal(720, restored.LaunchWindowHeight);
        Assert.Equal("实例标题 {version}", restored.VersionArgumentTitle);
        Assert.Equal("实例标题 {version}", settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionArgumentTitle}", ""));
        Assert.Equal("-XX:+UseG1GC", restored.ExtraJvmArgs);
        Assert.Equal("--demo", restored.ExtraGameArgs);
        Assert.Equal("mc.example.com:25565", restored.ServerIp);
        Assert.Equal("mc.example.com:25565", settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionServerEnter}", ""));
        Assert.Equal("echo instance {name}", restored.VersionAdvanceRun);
        Assert.False(restored.VersionAdvanceRunWait);
        Assert.Equal(1, restored.VersionRamType);
        Assert.Equal(25, restored.VersionRamCustom);
        Assert.Equal(2, restored.VersionRamOptimize);
        Assert.Equal(4, restored.VersionServerLogin);
        Assert.True(restored.IsVersionAuthServerLogin);
        Assert.False(restored.IsVersionNideServerLogin);
        Assert.Equal("https://auth.example.com/api/yggdrasil", restored.VersionServerAuthServer);
        Assert.Equal("https://auth.example.com/auth/register", restored.VersionServerAuthRegister);
        Assert.Equal("Example Auth", restored.VersionServerAuthName);
        Assert.True(restored.DisableModUpdate);
        Assert.True(restored.IgnoreJavaCompatibility);
        Assert.True(restored.DisableFileCheck);
        Assert.True(settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionAdvanceDisableModUpdate}", false));
        Assert.True(settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionAdvanceJava}", false));
        Assert.True(settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionAdvanceAssetsV2}", false));
        Assert.Equal(restored.VersionCustomInfo, settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionArgumentInfo}", ""));
        Assert.Equal("echo instance {name}", settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionAdvanceRun}", ""));
        Assert.False(settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionAdvanceRunWait}", true));
        Assert.Equal(1, settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionRamType}", 2));
        Assert.Equal(25, settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionRamCustom}", 15));
        Assert.Equal([2, 0, 1], restored.VersionRamTypeOptions.Select(option => option.Value));
        Assert.Equal(2, settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionRamOptimize}", 0));
        Assert.Equal([0, 1, 2], restored.VersionRamOptimizeOptions.Select(option => option.Value));
        Assert.Equal([0, 1, 2, 3, 4], restored.VersionServerLoginOptions.Select(option => option.Value));
        Assert.Equal(4, settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionServerLogin}", 0));
        Assert.Equal("https://auth.example.com/api/yggdrasil", settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionServerAuthServer}", ""));
        Assert.Equal("https://auth.example.com/auth/register", settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionServerAuthRegister}", ""));
        Assert.Equal("Example Auth", settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionServerAuthName}", ""));
        Assert.Equal(MinecraftInstanceDisplayType.Rubbish, restored.VersionDisplayType);
        var setup = File.ReadAllText(Path.Combine(temp.Path, "versions", "1.20.1", "PCL", "Setup.ini"));
        Assert.Contains("DisplayType:4", setup);
        Assert.Contains("CustomInfo:整合包测试版本", setup);
        Assert.Equal("整合包测试版本", restored.SelectedInstance?.CustomInfo);
        Assert.Equal("整合包测试版本", restored.SelectedInstance?.DisplayInfo);
    }

    [Fact]
    public async Task InstancePageViewModelCanResetInstanceLaunchSettingsToGlobal()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        { "id": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.LaunchWindowWidth, 854);
        settings.Set(AppSettingKeys.VersionAdvanceJvm, "-Dglobal=true");
        settings.Set(AppSettingKeys.LaunchArgumentIndieV2, 4);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, confirmDelete: true).ViewModel;

        await viewModel.RefreshAsync();
        viewModel.LaunchWindowWidth = 1280;
        viewModel.ExtraJvmArgs = "-Dinstance=true";
        viewModel.VersionIsolationEnabled = false;
        viewModel.VersionDisplayType = MinecraftInstanceDisplayType.Rubbish;
        viewModel.VersionCustomInfo = "Instance Pack";
        await viewModel.SaveInstanceLaunchSettingsCommand.ExecuteAsync(null);

        Assert.True(settings.HasSaved($"Instance.1.20.1.{AppSettingKeys.LaunchWindowWidth}"));
        Assert.True(settings.HasSaved($"Instance.1.20.1.{AppSettingKeys.VersionAdvanceJvm}"));
        Assert.Contains("覆盖了", viewModel.InstanceLaunchOverrideSummary);

        await viewModel.ResetInstanceLaunchSettingsCommand.ExecuteAsync(null);

        Assert.False(settings.HasSaved($"Instance.1.20.1.{AppSettingKeys.LaunchWindowWidth}"));
        Assert.False(settings.HasSaved($"Instance.1.20.1.{AppSettingKeys.VersionAdvanceJvm}"));
        Assert.False(settings.HasSaved($"Instance.1.20.1.{AppSettingKeys.VersionArgumentIndieV2}"));
        Assert.Equal(854, viewModel.LaunchWindowWidth);
        Assert.Equal("-Dglobal=true", viewModel.ExtraJvmArgs);
        Assert.True(viewModel.VersionIsolationEnabled);
        Assert.Equal(MinecraftInstanceDisplayType.Auto, viewModel.VersionDisplayType);
        Assert.Equal("", viewModel.VersionCustomInfo);
        Assert.Equal("", viewModel.SelectedInstance?.CustomInfo);
        Assert.Contains("未覆盖全局", viewModel.InstanceLaunchOverrideSummary);
        Assert.Contains("已恢复", viewModel.StatusMessage);
    }

    [Fact]
    public void InstancePageViewModelExposesOldPclGcComboValues()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        Assert.Collection(
            viewModel.VersionGcOptions,
            option => Assert.Equal(0, option.Value),
            option => Assert.Equal(1, option.Value),
            option => Assert.Equal(2, option.Value),
            option => Assert.Equal(3, option.Value),
            option => Assert.Equal(5, option.Value),
            option => Assert.Equal(4, option.Value));
    }

    [Fact]
    public async Task InstancePageViewModelSavesVersionIsolationAsInstanceSetting()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T13:25:51+00:00",
          "time": "2023-06-12T13:25:51+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.LaunchArgumentIndieV2, 4);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        Assert.True(viewModel.VersionIsolationEnabled);

        viewModel.VersionIsolationEnabled = false;

        Assert.False(settings.Get($"Instance.1.20.1.{AppSettingKeys.VersionArgumentIndieV2}", true));
        Assert.Equal(Path.GetFullPath(temp.Path), viewModel.GameDirectoryPath);
    }

    [Fact]
    public async Task InstancePageViewModelOpensVersionGameContentFoldersThroughService()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "1.20.1", """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T13:25:51+00:00",
          "time": "2023-06-12T13:25:51+00:00",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": []
        }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.LaunchArgumentIndieV2, 4);
        var folders = new CaptureFolderOpenService();
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, folders: folders).ViewModel;

        await viewModel.RefreshAsync();
        var instance = Assert.IsType<MinecraftInstance>(viewModel.SelectedInstance);
        var gameDirectory = viewModel.GameDirectoryPath;

        viewModel.OpenSelectedInstanceFolderCommand.Execute(null);
        viewModel.OpenSelectedSavesFolderCommand.Execute(null);
        viewModel.OpenSelectedModsFolderCommand.Execute(null);
        viewModel.OpenSelectedResourcePacksFolderCommand.Execute(null);
        viewModel.OpenSelectedShaderPacksFolderCommand.Execute(null);
        viewModel.OpenSelectedScreenshotsFolderCommand.Execute(null);

        Assert.Equal(
            [
                instance.VersionPath,
                Path.Combine(gameDirectory, "saves"),
                Path.Combine(gameDirectory, "mods"),
                Path.Combine(gameDirectory, "resourcepacks"),
                Path.Combine(gameDirectory, "shaderpacks"),
                Path.Combine(gameDirectory, "screenshots")
            ],
            folders.OpenedPaths);
    }

    [Fact]
    public async Task InstancePageViewModelCompletesMissingClientJar()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "assets", "indexes"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "assets", "indexes", "5.json"), "{\"objects\":{}}");
        WriteVersionJson(temp.Path, "1.20.1", """
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
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.ToolDownloadSource, 2);
        var (viewModel, client) = CreateInstancePageViewModel(temp.Path, settings);
        client.Map("https://piston-data.mojang.com/v1/objects/client/1.20.1.jar", Enumerable.Repeat((byte)8, 2048).ToArray());

        await viewModel.RefreshAsync();
        await viewModel.CompleteSelectedInstanceFilesAsync();

        Assert.True(File.Exists(Path.Combine(temp.Path, "versions", "1.20.1", "1.20.1.jar")));
        Assert.Contains("补全完成", viewModel.FileCompletionSummary);
        Assert.Contains("成功 1 个", viewModel.FileCompletionSummary);
        Assert.Contains("文件补全", viewModel.FileCompletionTaskName);
        Assert.Contains(viewModel.FileCompletionDetails, detail => detail.Contains("下载任务", StringComparison.Ordinal));
        Assert.Contains("补全完成", viewModel.StatusMessage);
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.LastFileCompletionInstanceName, ""));
        Assert.True(settings.Get(AppSettingKeys.LastFileCompletionSucceeded, false));
        Assert.Contains("1.20.1", settings.Get(AppSettingKeys.LastFileCompletionMessage, ""));
    }

    [Fact]
    public async Task InstancePageViewModelSkipsFileCompletionWhenVersionFileCheckIsDisabled()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "assets", "indexes"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "assets", "indexes", "5.json"), "{\"objects\":{}}");
        WriteVersionJson(temp.Path, "1.20.1", """
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
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set($"Instance.1.20.1.{AppSettingKeys.VersionAdvanceAssetsV2}", true);
        var (viewModel, client) = CreateInstancePageViewModel(temp.Path, settings);
        client.Map("https://piston-data.mojang.com/v1/objects/client/1.20.1.jar", Enumerable.Repeat((byte)8, 2048).ToArray());

        await viewModel.RefreshAsync();

        Assert.True(viewModel.DisableFileCheck);
        Assert.False(viewModel.CompleteSelectedInstanceFilesCommand.CanExecute(null));

        await viewModel.CompleteSelectedInstanceFilesAsync();

        Assert.False(File.Exists(Path.Combine(temp.Path, "versions", "1.20.1", "1.20.1.jar")));
        Assert.Contains("关闭文件校验", viewModel.FileCompletionSummary);
        Assert.Contains(viewModel.FileCompletionDetails, detail => detail.Contains("取消", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LocalModServiceScansMetadataAndTogglesDisabledFileLikeOldPcl()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "mods");
        Directory.CreateDirectory(modsPath);
        var jarPath = Path.Combine(modsPath, "sodium-fabric.jar");
        WriteFabricModJar(jarPath, "Sodium", "0.5.8", "Rendering engine");
        var service = new LocalModService(new NullLoggerService());

        var mod = Assert.Single(await service.ScanAsync(modsPath));

        Assert.True(mod.IsEnabled);
        Assert.Equal("sodium-fabric.jar", mod.EnabledFileName);
        Assert.Equal("Sodium", mod.DisplayName);
        Assert.Equal("0.5.8", mod.Version);
        Assert.Contains("Rendering", mod.Description, StringComparison.OrdinalIgnoreCase);

        var disabled = service.SetEnabled(mod, false);
        Assert.False(disabled.IsEnabled);
        Assert.False(File.Exists(jarPath));
        Assert.True(File.Exists(jarPath + ".disabled"));

        var enabled = service.SetEnabled(disabled, true);
        Assert.True(enabled.IsEnabled);
        Assert.True(File.Exists(jarPath));
        Assert.False(File.Exists(jarPath + ".disabled"));
    }

    [Fact]
    public async Task LocalModServicePrefersChineseLocalizedMetadata()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "mods");
        Directory.CreateDirectory(modsPath);
        var jarPath = Path.Combine(modsPath, "localized.jar");
        WriteFabricModJar(jarPath, """{"zh_cn":"中文模组","en_us":"English Mod"}""", "1.0.0", """{"zh_cn":"中文描述","en_us":"English description"}""");
        var service = new LocalModService(new NullLoggerService());

        var mod = Assert.Single(await service.ScanAsync(modsPath));

        Assert.Equal("中文模组", mod.DisplayName);
        Assert.Equal("中文描述", mod.Description);
    }

    [Fact]
    public async Task LocalModServicePrefersEnabledDuplicateMod()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "mods");
        Directory.CreateDirectory(modsPath);
        WriteFabricModJar(Path.Combine(modsPath, "same.jar"), "Enabled", "1", "Enabled copy");
        WriteFabricModJar(Path.Combine(modsPath, "same.jar.disabled"), "Disabled", "1", "Disabled copy");
        var service = new LocalModService(new NullLoggerService());

        var mod = Assert.Single(await service.ScanAsync(modsPath));

        Assert.True(mod.IsEnabled);
        Assert.Equal("Enabled", mod.DisplayName);
    }

    [Fact]
    public async Task LocalModUpdateServiceMatchesModrinthFileHashAndFindsLatestVersion()
    {
        using var temp = new TempDirectory();
        var jarPath = Path.Combine(temp.Path, "mods", "sodium.jar");
        WriteFabricModJar(jarPath, "Sodium", "0.5.8", "Rendering engine");
        var mod = Assert.Single(await new LocalModService(new NullLoggerService()).ScanAsync(Path.GetDirectoryName(jarPath)!));
        var sha1 = Convert.ToHexString(SHA1.HashData(await File.ReadAllBytesAsync(jarPath))).ToLowerInvariant();
        var client = new FakeDownloadByteClient();
        client.Map(
            $"https://api.modrinth.com/v2/version_file/{sha1}?algorithm=sha1",
            Encoding.UTF8.GetBytes($$"""
            {
              "project_id": "sodium",
              "id": "sodium-current",
              "name": "Sodium 0.5.8",
              "version_number": "0.5.8",
              "date_published": "2024-01-01T00:00:00Z",
              "game_versions": ["1.20.1"],
              "loaders": ["fabric"],
              "files": [
                {
                  "filename": "sodium.jar",
                  "url": "https://cdn.example/sodium.jar",
                  "size": 123,
                  "primary": true,
                  "hashes": { "sha1": "{{sha1}}" }
                }
              ]
            }
            """));
        client.Map(
            CommunityResourceVersionService.BuildModrinthVersionsUrl("sodium", CommunityResourceType.Mod, "1.20.1", "fabric"),
            Encoding.UTF8.GetBytes("""
            [
              {
                "project_id": "sodium",
                "id": "sodium-latest",
                "name": "Sodium 0.5.9",
                "version_number": "0.5.9",
                "date_published": "2024-02-01T00:00:00Z",
                "game_versions": ["1.20.1"],
                "loaders": ["fabric"],
                "files": [
                  {
                    "filename": "sodium-0.5.9.jar",
                    "url": "https://cdn.example/sodium-0.5.9.jar",
                    "size": 456,
                    "primary": true,
                    "hashes": { "sha1": "0000000000000000000000000000000000000000" }
                  }
                ]
              }
            ]
            """));
        var service = new LocalModUpdateService(client, new NullLoggerService());

        var updates = await service.CheckModrinthUpdatesAsync([mod], "1.20.1", "fabric");

        var update = Assert.Single(updates.Values);
        Assert.True(update.MatchedOnline);
        Assert.True(update.HasUpdate);
        Assert.Equal("sodium-latest", update.LatestVersion?.VersionId);
        Assert.Equal("sodium-0.5.9.jar", update.LatestFile?.FileName);
    }

    [Fact]
    public async Task InstancePageViewModelLoadsLocalModsFromInstanceDirectoryAndUsesOldNameStyleSetting()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "FabricPack", """
        { "id": "FabricPack", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        var modsPath = Path.Combine(temp.Path, "versions", "FabricPack", "mods");
        Directory.CreateDirectory(modsPath);
        WriteFabricModJar(Path.Combine(modsPath, "sodium-fabric.jar"), "Sodium", "0.5.8", "Rendering engine");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set(AppSettingKeys.ToolModLocalNameStyle, 1);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.RefreshLocalModsAsync();

        var fileNameStyle = Assert.Single(viewModel.LocalMods);
        Assert.Equal("sodium-fabric", fileNameStyle.Title);
        Assert.Contains("Sodium", fileNameStyle.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(modsPath, viewModel.LocalModsDirectory);

        settings.Set(AppSettingKeys.ToolModLocalNameStyle, 0);
        var restored = CreateInstancePageViewModel(temp.Path, settings).ViewModel;
        await restored.RefreshAsync();
        await restored.RefreshLocalModsAsync();

        var translatedStyle = Assert.Single(restored.LocalMods);
        Assert.Equal("Sodium", translatedStyle.Title);
        Assert.Equal("0.5.8", translatedStyle.Subtitle);
    }

    [Fact]
    public async Task InstancePageViewModelCanToggleAndDeleteSelectedLocalMod()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "FabricPack", """
        { "id": "FabricPack", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        var modsPath = Path.Combine(temp.Path, "versions", "FabricPack", "mods");
        Directory.CreateDirectory(modsPath);
        var jarPath = Path.Combine(modsPath, "sodium-fabric.jar");
        WriteFabricModJar(jarPath, "Sodium", "0.5.8", "Rendering engine");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, confirmDelete: true).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.RefreshLocalModsAsync();
        await viewModel.ToggleSelectedLocalModEnabledCommand.ExecuteAsync(null);

        Assert.False(File.Exists(jarPath));
        Assert.True(File.Exists(jarPath + ".disabled"));
        Assert.Equal(1, viewModel.DisabledLocalModCount);

        await viewModel.DeleteSelectedLocalModCommand.ExecuteAsync(null);

        Assert.False(File.Exists(jarPath + ".disabled"));
        Assert.Empty(viewModel.LocalMods);
    }

    [Fact]
    public async Task InstancePageViewModelInstallsSelectedLocalModFilesLikeOldPcl()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "FabricPack", """
        { "id": "FabricPack", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        var sourcePath = Path.Combine(temp.Path, "downloaded-mod.jar.disabled");
        WriteFabricModJar(sourcePath, "Downloaded Mod", "1", "Installed from disk");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var fileDialogs = new ModFileDialogService([sourcePath]);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, fileDialogs: fileDialogs).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.InstallLocalModsCommand.ExecuteAsync(null);

        var installedPath = Path.Combine(temp.Path, "versions", "FabricPack", "mods", "downloaded-mod.jar");
        Assert.True(File.Exists(installedPath));
        Assert.Single(viewModel.LocalMods);
        Assert.Contains("已安装", viewModel.StatusMessage);
        Assert.Equal(Path.Combine(temp.Path, "versions", "FabricPack", "mods"), fileDialogs.LastInitialDirectory);
    }

    [Fact]
    public async Task InstancePageViewModelPreparesDownloadModPresetForSelectedInstance()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "FabricPack", """
        { "id": "FabricPack", "inheritsFrom": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings).ViewModel;

        await viewModel.RefreshAsync();
        viewModel.LocalModSearchText = "sodium";
        await viewModel.DownloadModsForSelectedInstanceCommand.ExecuteAsync(null);

        Assert.Equal(PageRoute.Download, settings.Get(AppSettingKeys.LastRoute, PageRoute.Launch));
        Assert.Equal((int)DownloadSection.Mod, settings.Get(AppSettingKeys.DownloadPresetResourceSection, -1));
        Assert.Equal("sodium", settings.Get(AppSettingKeys.DownloadPresetSearchText, ""));
        Assert.Equal("1.20.1", settings.Get(AppSettingKeys.DownloadPresetGameVersion, ""));
        Assert.Equal("fabric", settings.Get(AppSettingKeys.DownloadPresetLoader, ""));
        Assert.Contains("下载", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstancePageViewModelShowsLocalModUpdateResults()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "FabricPack", """
        { "id": "FabricPack", "inheritsFrom": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        var modsPath = Path.Combine(temp.Path, "versions", "FabricPack", "mods");
        var jarPath = Path.Combine(modsPath, "sodium.jar");
        WriteFabricModJar(jarPath, "Sodium", "0.5.8", "Rendering engine");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var updateService = new FakeLocalModUpdateService(new LocalModUpdateInfo(
            "sodium.jar",
            true,
            "sodium",
            "sodium-current",
            "0.5.8",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            new CommunityResourceVersion(
                CommunityResourcePlatform.Modrinth,
                CommunityResourceType.Mod,
                "sodium",
                "sodium-latest",
                "Sodium 0.5.9",
                "0.5.9",
                DateTimeOffset.Parse("2024-02-01T00:00:00Z"),
                ["1.20.1"],
                ["fabric"],
                [new CommunityResourceFile("sodium-0.5.9.jar", "https://cdn.example/sodium-0.5.9.jar", 456, "new-sha", null, true)],
                []),
            new CommunityResourceFile("sodium-0.5.9.jar", "https://cdn.example/sodium-0.5.9.jar", 456, "new-sha", null, true),
            "https://modrinth.com/mod/sodium/changelog?g=1.20.1"));
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, localModUpdates: updateService).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.RefreshLocalModsAsync();
        await viewModel.CheckLocalModUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.UpdateLocalModCount);
        var row = Assert.Single(viewModel.LocalMods);
        Assert.True(row.HasUpdate);
        Assert.Contains("0.5.9", row.UpdateText);
        Assert.Contains("更新", row.DetailText);
    }

    [Fact]
    public async Task InstancePageViewModelUpdatesLocalModsThroughDownloadManager()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "FabricPack", """
        { "id": "FabricPack", "inheritsFrom": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        var modsPath = Path.Combine(temp.Path, "versions", "FabricPack", "mods");
        var oldJarPath = Path.Combine(modsPath, "sodium.jar");
        WriteFabricModJar(oldJarPath, "Sodium", "0.5.8", "Rendering engine");
        var latestSource = Path.Combine(temp.Path, "source-sodium.jar");
        WriteFabricModJar(latestSource, "Sodium", "0.5.9", "Rendering engine updated");
        var latestBytes = await File.ReadAllBytesAsync(latestSource);
        var latestFile = new CommunityResourceFile("sodium-0.5.9.jar", "https://cdn.example/sodium-0.5.9.jar", latestBytes.Length, null, null, true);
        var latestVersion = new CommunityResourceVersion(
            CommunityResourcePlatform.Modrinth,
            CommunityResourceType.Mod,
            "sodium",
            "sodium-latest",
            "Sodium 0.5.9",
            "0.5.9",
            DateTimeOffset.Parse("2024-02-01T00:00:00Z"),
            ["1.20.1"],
            ["fabric"],
            [latestFile],
            []);
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var updateService = new FakeLocalModUpdateService(new LocalModUpdateInfo(
            "sodium.jar",
            true,
            "sodium",
            "sodium-current",
            "0.5.8",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            latestVersion,
            latestFile,
            "https://modrinth.com/mod/sodium/changelog?g=1.20.1"));
        var (viewModel, client) = CreateInstancePageViewModel(temp.Path, settings, localModUpdates: updateService);
        client.Map(latestFile.Url, latestBytes);

        await viewModel.RefreshAsync();
        await viewModel.RefreshLocalModsAsync();
        await viewModel.CheckLocalModUpdatesCommand.ExecuteAsync(null);

        Assert.True(viewModel.UpdateAllLocalModsCommand.CanExecute(null));

        await viewModel.UpdateAllLocalModsCommand.ExecuteAsync(null);

        var updatedPath = Path.Combine(modsPath, "sodium-0.5.9.jar");
        Assert.False(File.Exists(oldJarPath));
        Assert.True(File.Exists(updatedPath));
        Assert.Equal("已更新 1 个 Mod", viewModel.StatusMessage);
        var updatedRow = Assert.Single(viewModel.LocalMods);
        Assert.Equal("Sodium", updatedRow.Title);
        Assert.Equal("0.5.9", updatedRow.Subtitle);
        Assert.Equal(0, viewModel.UpdateLocalModCount);
    }

    [Fact]
    public async Task InstancePageViewModelBlocksLocalModUpdatesWhenVersionDisablesModUpdate()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "FabricPack", """
        { "id": "FabricPack", "inheritsFrom": "1.20.1", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        var modsPath = Path.Combine(temp.Path, "versions", "FabricPack", "mods");
        WriteFabricModJar(Path.Combine(modsPath, "sodium.jar"), "Sodium", "0.5.8", "Rendering engine");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        settings.Set($"Instance.FabricPack.{AppSettingKeys.VersionAdvanceDisableModUpdate}", true);
        var updateService = new FakeLocalModUpdateService();
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, localModUpdates: updateService).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.RefreshLocalModsAsync();

        Assert.True(viewModel.DisableModUpdate);
        Assert.False(viewModel.CheckLocalModUpdatesCommand.CanExecute(null));

        await viewModel.CheckLocalModUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(0, updateService.CallCount);
        Assert.Equal(0, viewModel.UpdateLocalModCount);
    }

    [Fact]
    public async Task InstancePageViewModelCanBatchSelectFilterEnableDisableAndDeleteLocalMods()
    {
        using var temp = new TempDirectory();
        WriteVersionJson(temp.Path, "FabricPack", """
        { "id": "FabricPack", "releaseTime": "2023-06-12T12:00:00+00:00", "libraries": [{ "name": "net.fabricmc:fabric-loader:0.15.0" }] }
        """);
        var modsPath = Path.Combine(temp.Path, "versions", "FabricPack", "mods");
        Directory.CreateDirectory(modsPath);
        var sodium = Path.Combine(modsPath, "sodium.jar");
        var lithium = Path.Combine(modsPath, "lithium.jar");
        var iris = Path.Combine(modsPath, "iris.jar");
        WriteFabricModJar(sodium, "Sodium", "1", "Rendering engine");
        WriteFabricModJar(lithium + ".disabled", "Lithium", "1", "Server optimizations");
        WriteFabricModJar(iris, "Iris", "1", "Shaders");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.MinecraftRootPath, temp.Path);
        var viewModel = CreateInstancePageViewModel(temp.Path, settings, confirmDelete: true).ViewModel;

        await viewModel.RefreshAsync();
        await viewModel.RefreshLocalModsAsync();

        Assert.Equal(3, viewModel.LocalModCount);
        Assert.Equal(2, viewModel.EnabledLocalModCount);
        Assert.Equal(1, viewModel.DisabledLocalModCount);

        viewModel.SelectAllLocalModsCommand.Execute(null);
        Assert.Equal(3, viewModel.SelectedLocalModCount);
        Assert.Equal(2, viewModel.SelectedEnabledLocalModCount);
        await viewModel.DisableSelectedLocalModsCommand.ExecuteAsync(null);

        Assert.False(File.Exists(sodium));
        Assert.True(File.Exists(sodium + ".disabled"));
        Assert.False(File.Exists(iris));
        Assert.True(File.Exists(iris + ".disabled"));
        Assert.Equal(3, viewModel.DisabledLocalModCount);
        Assert.Equal(0, viewModel.SelectedLocalModCount);

        viewModel.LocalModFilter = 2;
        Assert.Equal(3, viewModel.LocalModCount);
        viewModel.SelectAllLocalModsCommand.Execute(null);
        await viewModel.EnableSelectedLocalModsCommand.ExecuteAsync(null);

        Assert.True(File.Exists(sodium));
        Assert.True(File.Exists(lithium));
        Assert.True(File.Exists(iris));
        Assert.Equal(3, viewModel.EnabledLocalModCount);

        viewModel.LocalModFilter = 0;
        viewModel.LocalModSearchText = "Sodium";
        viewModel.SelectAllLocalModsCommand.Execute(null);
        Assert.Equal(1, viewModel.SelectedLocalModCount);
        Assert.Contains("1", viewModel.LocalModSelectionSummary, StringComparison.Ordinal);
        await viewModel.DeleteSelectedLocalModsCommand.ExecuteAsync(null);

        Assert.False(File.Exists(sodium));
        Assert.True(File.Exists(lithium));
        Assert.True(File.Exists(iris));
        Assert.Equal(0, viewModel.SelectedLocalModCount);
    }

    private static MinecraftInstance CreateInstance(
        string type,
        MinecraftInstanceState state = MinecraftInstanceState.Ready,
        bool hasForge = false,
        bool hasFabric = false,
        bool hasNeoForge = false,
        bool hasOptiFine = false)
    {
        var version = new MinecraftVersionInfo(
            "test",
            type,
            null,
            null,
            "",
            "net.minecraft.client.main.Main",
            "test",
            hasForge,
            hasFabric,
            hasNeoForge,
            hasOptiFine);
        return new MinecraftInstance("test", "C:\\MC", "C:\\MC\\versions\\test", "C:\\MC\\versions\\test\\test.json", state, version, "");
    }

    private static void WriteVersionJson(string root, string name, string json)
    {
        var versionPath = Path.Combine(root, "versions", name);
        Directory.CreateDirectory(versionPath);
        File.WriteAllText(Path.Combine(versionPath, $"{name}.json"), json);
    }

    private static void WriteFabricModJar(string path, string name, string version, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("fabric.mod.json");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write($$"""
        {
          "schemaVersion": 1,
          "id": "{{Path.GetFileNameWithoutExtension(path)}}",
          "name": {{JsonStringOrRaw(name)}},
          "version": "{{version}}",
          "description": {{JsonStringOrRaw(description)}}
        }
        """);
    }

    private static string JsonStringOrRaw(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[')
            ? value
            : JsonSerializer.Serialize(value);
    }

    private static (InstancePageViewModel ViewModel, FakeDownloadByteClient Client) CreateInstancePageViewModel(
        string root,
        AppSettingsService settings,
        bool confirmDelete = true,
        string? promptValue = null,
        TestLaunchPipelineService? launchPipeline = null,
        IFileDialogService? fileDialogs = null,
        ILocalModUpdateService? localModUpdates = null,
        IModpackExportService? modpackExport = null,
        IFolderOpenService? folders = null)
    {
        var logger = new NullLoggerService();
        var checker = new FileCheckService(logger);
        var sources = new DownloadSourceService(settings);
        var client = new FakeDownloadByteClient();
        var downloadManager = new DownloadManagerService(client, checker, logger);
        var completer = new LaunchFileCompleter(sources, checker, logger);
        var instanceManagement = new TestMinecraftInstanceManagementService();
        var viewModel = new InstancePageViewModel(
            new MinecraftDiscoveryService(instanceManagement),
            instanceManagement,
            completer,
            launchPipeline ?? new TestLaunchPipelineService(),
            downloadManager,
            modpackExport,
            settings,
            fileDialogs ?? new NullFileDialogService(),
            new TestPromptService(confirmDelete, promptValue),
            logger,
            localModUpdateService: localModUpdates,
            folders: folders);
        return (viewModel, client);
    }

    private sealed class CaptureFolderOpenService : IFolderOpenService
    {
        public List<string> OpenedPaths { get; } = [];

        public void OpenFolder(string folderPath)
        {
            OpenedPaths.Add(folderPath);
        }
    }

    private sealed class TestPromptService(bool confirm, string? promptValue = null) : IUserPromptService
    {
        public bool Confirm(string title, string message)
        {
            return confirm;
        }

        public string? Prompt(string title, string message, string defaultValue)
        {
            return promptValue;
        }
    }

    private sealed class TestNavigationService : INavigationService
    {
        private readonly PageViewModelBase instancePage;
        private readonly PageViewModelBase launchPage = new TestPageViewModel(PageRoute.Launch);

        public TestNavigationService(PageViewModelBase instancePage)
        {
            this.instancePage = instancePage;
            CurrentPage = instancePage;
        }

        public IReadOnlyList<PageNavigationItem> Pages { get; } =
        [
            new(PageRoute.Launch, "启动", "启动"),
            new(PageRoute.Instance, "实例", "实例")
        ];

        public PageViewModelBase CurrentPage { get; private set; }

        public void Navigate(PageRoute route)
        {
            CurrentPage = route == PageRoute.Instance ? instancePage : launchPage;
        }
    }

    private sealed class TestPageViewModel(PageRoute route) : PageViewModelBase(route, route.ToString(), route.ToString());

    private sealed class FolderFileDialogService(string folderPath) : IFileDialogService
    {
        public string? PickFolder(string title, string initialDirectory)
        {
            return folderPath;
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
            return null;
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

    private sealed class ModFileDialogService(IReadOnlyList<string> files) : IFileDialogService
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
            return null;
        }

        public string? PickModpackFile(string initialDirectory)
        {
            return null;
        }

        public IReadOnlyList<string> PickModFiles(string initialDirectory)
        {
            LastInitialDirectory = initialDirectory;
            return files;
        }

        public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter)
        {
            return null;
        }
    }

    private sealed class TestLaunchPipelineService : ILaunchPipelineService
    {
        public event EventHandler<IReadOnlyList<LaunchStepState>>? StepsChanged
        {
            add { }
            remove { }
        }

        public LaunchRequest? LastRequest { get; private set; }

        public int LaunchCalls { get; private set; }

        public IReadOnlyList<LaunchStepState> Steps { get; } = [];

        public Task<LaunchResult> GenerateProfileAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LaunchResult(true, null, [], null));
        }

        public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            LaunchCalls++;
            LastRequest = request;
            return Task.FromResult(new LaunchResult(true, null, [], null));
        }
    }

    private sealed class TestMinecraftInstanceManagementService : IMinecraftInstanceManagementService
    {
        private readonly MinecraftInstanceManagementService _inner = new();

        public MinecraftInstanceMetadata ReadMetadata(string versionPath)
        {
            return _inner.ReadMetadata(versionPath);
        }

        public void SetStar(MinecraftInstance instance, bool isStar)
        {
            _inner.SetStar(instance, isStar);
        }

        public void SetDisplayType(MinecraftInstance instance, MinecraftInstanceDisplayType displayType)
        {
            _inner.SetDisplayType(instance, displayType);
        }

        public void SetCustomInfo(MinecraftInstance instance, string customInfo)
        {
            _inner.SetCustomInfo(instance, customInfo);
        }

        public string RenameInstance(MinecraftInstance instance, string newName)
        {
            return _inner.RenameInstance(instance, newName);
        }

        public string CloneInstance(MinecraftInstance instance, string newName)
        {
            return _inner.CloneInstance(instance, newName);
        }

        public string ImportInstance(string sourceVersionPath, string targetMinecraftRoot, string? targetName = null)
        {
            return _inner.ImportInstance(sourceVersionPath, targetMinecraftRoot, targetName);
        }

        public void DeleteInstance(MinecraftInstance instance, bool permanent = false)
        {
            _inner.DeleteInstance(instance, permanent: true);
        }
    }

    private sealed class FakeLocalModUpdateService(params LocalModUpdateInfo[] infos) : ILocalModUpdateService
    {
        private readonly IReadOnlyDictionary<string, LocalModUpdateInfo> _infos = infos.ToDictionary(
            info => info.EnabledFileName,
            StringComparer.OrdinalIgnoreCase);

        public int CallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, LocalModUpdateInfo>> CheckModrinthUpdatesAsync(
            IReadOnlyList<LocalModFile> mods,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var result = mods
                .Where(mod => _infos.ContainsKey(mod.EnabledFileName))
                .ToDictionary(mod => mod.EnabledFileName, mod => _infos[mod.EnabledFileName], StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, LocalModUpdateInfo>>(result);
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
