using System.IO;
using System.IO.Compression;
using System.Text.Json;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.Tests;

public sealed class ModpackExportServiceTests
{
    private static MinecraftInstance CreateVanillaInstance(string name, string root, string jsonPath)
    {
        var versionDir = Path.GetDirectoryName(jsonPath) ?? root;
        var info = new MinecraftVersionInfo(
            "1.20.1", "release", DateTimeOffset.Now, DateTimeOffset.Now,
            "", "net.minecraft.client.main.Main", "1.20.1",
            false, false, false, false);
        return new MinecraftInstance(name, root, versionDir, jsonPath,
            MinecraftInstanceState.Ready, info, "",
            false, MinecraftInstanceDisplayType.Auto);
    }

    [Fact]
    public async Task ExportsModrinthPackWithIndex()
    {
        using var temp = new TempDirectory();
        var gameDir = Path.Combine(temp.Path, ".minecraft");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        File.WriteAllText(Path.Combine(gameDir, "mods", "test.jar"), "dummy");

        var jsonPath = Path.Combine(temp.Path, "test.json");
        File.WriteAllText(jsonPath, "{}");
        var instance = CreateVanillaInstance("TestPack", temp.Path, jsonPath);
        var target = Path.Combine(temp.Path, "output.mrpack");

        var service = new ModpackExportService(new NullLoggerService());
        var options = new ModpackExportOptions { IncludeMods = true };
        var result = await service.ExportModrinthAsync(instance, gameDir, target, "MyPack", "2.0", options);

        Assert.True(File.Exists(target));
        Assert.Equal(target, result.TargetPath);
        using var zip = ZipFile.OpenRead(target);
        var entry = zip.GetEntry("modrinth.index.json");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("minecraft", doc.RootElement.GetProperty("game").GetString());
        Assert.Equal("MyPack", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExportsCurseForgeZipWithManifest()
    {
        using var temp = new TempDirectory();
        var gameDir = Path.Combine(temp.Path, ".minecraft");
        Directory.CreateDirectory(gameDir);

        var jsonPath = Path.Combine(temp.Path, "test.json");
        File.WriteAllText(jsonPath, "{}");
        var instance = CreateVanillaInstance("ForgePack", temp.Path, jsonPath);
        var target = Path.Combine(temp.Path, "forge.zip");

        var service = new ModpackExportService(new NullLoggerService());
        var result = await service.ExportModrinthAsync(instance, gameDir, target, "FP", "1.0");

        Assert.True(File.Exists(target));
        using var zip = ZipFile.OpenRead(target);
        var entry = zip.GetEntry("manifest.json");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("minecraftModpack", doc.RootElement.GetProperty("manifestType").GetString());
    }

    [Fact]
    public async Task RespectsOverrideOptions()
    {
        using var temp = new TempDirectory();
        var gameDir = Path.Combine(temp.Path, ".minecraft");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        File.WriteAllText(Path.Combine(gameDir, "mods", "A.jar"), "a");
        Directory.CreateDirectory(Path.Combine(gameDir, "config"));
        File.WriteAllText(Path.Combine(gameDir, "config", "B.cfg"), "b");

        var jsonPath = Path.Combine(temp.Path, "test.json");
        File.WriteAllText(jsonPath, "{}");
        var instance = CreateVanillaInstance("OptTest", temp.Path, jsonPath);
        var target = Path.Combine(temp.Path, "opt.mrpack");

        var options = new ModpackExportOptions { IncludeMods = true, IncludeConfig = false };
        var service = new ModpackExportService(new NullLoggerService());
        await service.ExportModrinthAsync(instance, gameDir, target, "O", "1", options);

        using var zip = ZipFile.OpenRead(target);
        Assert.NotNull(zip.GetEntry("overrides/mods/A.jar"));
        Assert.Null(zip.GetEntry("overrides/config/B.cfg"));
    }

    [Fact]
    public async Task WarnsWhenGameDirectoryMissing()
    {
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "test.json");
        File.WriteAllText(jsonPath, "{}");
        var instance = CreateVanillaInstance("MissingDir", temp.Path, jsonPath);
        var target = Path.Combine(temp.Path, "missing.mrpack");

        var service = new ModpackExportService(new NullLoggerService());
        var result = await service.ExportModrinthAsync(instance, Path.Combine(temp.Path, "nonexistent"), target, "M", "1");

        Assert.True(result.Warnings.Count > 0);
    }

    [Fact]
    public async Task CreatesOutputDirectoryIfNeeded()
    {
        using var temp = new TempDirectory();
        var gameDir = Path.Combine(temp.Path, ".minecraft");
        Directory.CreateDirectory(gameDir);
        var jsonPath = Path.Combine(temp.Path, "test.json");
        File.WriteAllText(jsonPath, "{}");
        var instance = CreateVanillaInstance("Nested", temp.Path, jsonPath);
        var target = Path.Combine(temp.Path, "sub", "deep", "nested.mrpack");

        var service = new ModpackExportService(new NullLoggerService());
        await service.ExportModrinthAsync(instance, gameDir, target, "N", "1");

        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task UsesDefaultValuesWhenPackMetaIsEmpty()
    {
        using var temp = new TempDirectory();
        var gameDir = Path.Combine(temp.Path, ".minecraft");
        Directory.CreateDirectory(gameDir);
        var jsonPath = Path.Combine(temp.Path, "test.json");
        File.WriteAllText(jsonPath, "{}");
        var instance = CreateVanillaInstance("DefaultPack", temp.Path, jsonPath);
        var target = Path.Combine(temp.Path, "default.mrpack");

        var service = new ModpackExportService(new NullLoggerService());
        await service.ExportModrinthAsync(instance, gameDir, target, "", "");

        using var zip = ZipFile.OpenRead(target);
        var entry = zip.GetEntry("modrinth.index.json");
        using var reader = new StreamReader(entry.Open());
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("DefaultPack", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("1.0.0", doc.RootElement.GetProperty("versionId").GetString());
    }

    [Fact]
    public async Task ThrowsOnEmptyTargetPath()
    {
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "test.json");
        File.WriteAllText(jsonPath, "{}");
        var instance = CreateVanillaInstance("ErrPack", temp.Path, jsonPath);

        var service = new ModpackExportService(new NullLoggerService());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExportModrinthAsync(instance, temp.Path, "", "E", "1"));
    }
}