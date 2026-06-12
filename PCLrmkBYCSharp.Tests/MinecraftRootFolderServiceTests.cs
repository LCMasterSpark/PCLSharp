using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

public sealed class MinecraftRootFolderServiceTests
{
    [Fact]
    public void LoadFoldersReadsOldLaunchFoldersFormatAndRenamesDefaultFolder()
    {
        using var temp = new TempDirectory();
        var defaultRoot = Path.Combine(temp.Path, ".minecraft");
        var customRoot = Path.Combine(temp.Path, "CustomMc");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.LaunchFolders, $"官启重命名>{defaultRoot}|整合包>{customRoot}");
        var service = new MinecraftRootFolderService(settings);

        var folders = service.LoadFolders(defaultRoot, defaultRoot);

        Assert.Contains(folders, folder => folder.Name == "官启重命名" && folder.Type == MinecraftRootFolderType.RenamedVanilla);
        Assert.Contains(folders, folder => folder.Name == "整合包" && folder.Type == MinecraftRootFolderType.Custom);
    }

    [Fact]
    public void LoadFoldersMarksCurrentFolderAndCountsValidVersions()
    {
        using var temp = new TempDirectory();
        var defaultRoot = Path.Combine(temp.Path, ".minecraft");
        var customRoot = Path.Combine(temp.Path, "CustomMc");
        WriteVersionJson(defaultRoot, "1.20.1");
        WriteVersionJson(customRoot, "PackA");
        WriteVersionJson(customRoot, "PackB");
        Directory.CreateDirectory(Path.Combine(customRoot, "versions", "Broken"));
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        settings.Set(AppSettingKeys.LaunchFolders, $"整合包>{customRoot}");
        var service = new MinecraftRootFolderService(settings);

        var folders = service.LoadFolders(defaultRoot, customRoot);

        var defaultFolder = Assert.Single(folders, folder => folder.Path == Path.GetFullPath(defaultRoot));
        Assert.False(defaultFolder.IsCurrent);
        Assert.Equal(1, defaultFolder.VersionCount);
        Assert.Equal("1 个版本", defaultFolder.VersionCountText);
        var current = Assert.Single(folders, folder => folder.Path == Path.GetFullPath(customRoot));
        Assert.True(current.IsCurrent);
        Assert.Equal("当前", current.CurrentText);
        Assert.Equal(2, current.VersionCount);
    }

    [Fact]
    public void AddFolderCreatesVersionsFolderAndSynchronizesSelectedRoot()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "PackA");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var service = new MinecraftRootFolderService(settings);

        var folder = service.AddFolder(root, "整合包 A");

        Assert.Equal("整合包 A", folder.Name);
        Assert.Equal(Path.GetFullPath(root), folder.Path);
        Assert.True(Directory.Exists(Path.Combine(root, "versions")));
        Assert.Equal(Path.GetFullPath(root), settings.Get(AppSettingKeys.MinecraftRootPath, ""));
        Assert.Equal(Path.GetFullPath(root), settings.Get(AppSettingKeys.LaunchFolderSelect, ""));
        Assert.Contains("整合包 A>", settings.Get(AppSettingKeys.LaunchFolders, ""));
    }

    [Fact]
    public void AddFolderFindsMinecraftSubfolderWhenParentIsSelected()
    {
        using var temp = new TempDirectory();
        var parent = Path.Combine(temp.Path, "Game");
        var minecraft = Path.Combine(parent, ".minecraft");
        Directory.CreateDirectory(Path.Combine(minecraft, "versions"));
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var service = new MinecraftRootFolderService(settings);

        var folder = service.AddFolder(parent);

        Assert.Equal(Path.GetFullPath(minecraft), folder.Path);
    }

    [Fact]
    public void AddFolderRejectsLaunchInvalidPathCharacters()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var service = new MinecraftRootFolderService(settings);

        Assert.Throws<InvalidOperationException>(() => service.AddFolder(Path.Combine(temp.Path, "bad!path")));
        Assert.Throws<InvalidOperationException>(() => service.AddFolder(Path.Combine(temp.Path, "bad;path")));
    }

    [Fact]
    public void RenameFolderUpdatesStoredLaunchFoldersEntry()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "PackA");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var service = new MinecraftRootFolderService(settings);
        service.AddFolder(root, "旧名称");

        service.RenameFolder(root, "新名称");

        Assert.Contains("新名称>" + Path.GetFullPath(root), settings.Get(AppSettingKeys.LaunchFolders, ""));
        Assert.DoesNotContain("旧名称>", settings.Get(AppSettingKeys.LaunchFolders, ""));
    }

    [Fact]
    public void RenameDefaultFolderStoresCustomNameAsRenamedVanilla()
    {
        using var temp = new TempDirectory();
        var defaultRoot = Path.Combine(temp.Path, ".minecraft");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var service = new MinecraftRootFolderService(settings);

        service.RenameFolder(defaultRoot, "官启");
        var folders = service.LoadFolders(defaultRoot, defaultRoot);

        var folder = Assert.Single(folders, item => item.Path == Path.GetFullPath(defaultRoot));
        Assert.Equal("官启", folder.Name);
        Assert.Equal(MinecraftRootFolderType.RenamedVanilla, folder.Type);
    }

    private static void WriteVersionJson(string root, string name)
    {
        var versionPath = Path.Combine(root, "versions", name);
        Directory.CreateDirectory(versionPath);
        File.WriteAllText(Path.Combine(versionPath, name + ".json"), "{}");
    }
}
