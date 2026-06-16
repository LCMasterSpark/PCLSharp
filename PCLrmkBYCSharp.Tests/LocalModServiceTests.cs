using System.IO;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

public sealed class LocalModServiceTests
{
    [Fact]
    public async Task ScanAsync_ReturnsEmptyWhenDirectoryMissing()
    {
        using var temp = new TempDirectory();
        var svc = new LocalModService(new NullLoggerService());
        var result = await svc.ScanAsync(Path.Combine(temp.Path, "noexist"));
        Assert.Empty(result);
    }

    [Fact]
    public async Task ScanAsync_FindsOnlyModFiles()
    {
        using var temp = new TempDirectory();
        var modsDir = Path.Combine(temp.Path, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "MyMod.jar"), "j");
        File.WriteAllText(Path.Combine(modsDir, "Pack.zip"), "z");
        File.WriteAllText(Path.Combine(modsDir, "readme.txt"), "t");
        var svc = new LocalModService(new NullLoggerService());
        var result = await svc.ScanAsync(modsDir);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ScanAsync_RecognizesDisabledMods()
    {
        using var temp = new TempDirectory();
        var modsDir = Path.Combine(temp.Path, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "Active.jar"), "a");
        File.WriteAllText(Path.Combine(modsDir, "Inactive.jar.disabled"), "i");
        var svc = new LocalModService(new NullLoggerService());
        var result = await svc.ScanAsync(modsDir);
        Assert.Equal(2, result.Count);
        Assert.True(result.First(m => m.FileName == "Active.jar").IsEnabled);
        Assert.False(result.First(m => m.FileName == "Inactive.jar.disabled").IsEnabled);
    }

    [Fact]
    public void SetEnabled_DisablesModFile()
    {
        using var temp = new TempDirectory();
        var modsDir = Path.Combine(temp.Path, "mods");
        Directory.CreateDirectory(modsDir);
        var jarPath = Path.Combine(modsDir, "Toggle.jar");
        File.WriteAllText(jarPath, "t");
        var svc = new LocalModService(new NullLoggerService());
        var mods = svc.ScanAsync(modsDir).GetAwaiter().GetResult();
        svc.SetEnabled(mods[0], false);
        Assert.False(File.Exists(jarPath));
        Assert.True(File.Exists(jarPath + ".disabled"));
    }

    [Fact]
    public void SetEnabled_ReEnablesModFile()
    {
        using var temp = new TempDirectory();
        var modsDir = Path.Combine(temp.Path, "mods");
        Directory.CreateDirectory(modsDir);
        var disabledPath = Path.Combine(modsDir, "ReEnable.jar.disabled");
        File.WriteAllText(disabledPath, "r");
        var svc = new LocalModService(new NullLoggerService());
        var mods = svc.ScanAsync(modsDir).GetAwaiter().GetResult();
        svc.SetEnabled(mods[0], true);
        Assert.False(File.Exists(disabledPath));
        Assert.True(File.Exists(Path.Combine(modsDir, "ReEnable.jar")));
    }

    [Fact]
    public async Task ScanAsync_DeduplicatesSameModFile()
    {
        using var temp = new TempDirectory();
        var modsDir = Path.Combine(temp.Path, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "Dupe.jar"), "d1");
        File.WriteAllText(Path.Combine(modsDir, "Dupe.jar.disabled"), "d2");
        var svc = new LocalModService(new NullLoggerService());
        var result = await svc.ScanAsync(modsDir);
        Assert.Single(result);
    }
}
