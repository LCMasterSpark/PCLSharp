using System.IO;
using System.IO.Compression;
using System.Text.Json;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.Tests;

public sealed class NativesExtractorTests
{
    private static void CreateNativeJar(string rootPath, string libGroup, string artifact, string version,
        string classifier, Action<ZipArchive> writeJar)
    {
        var groupPath = libGroup.Replace(".", Path.DirectorySeparatorChar.ToString());
        var libDir = Path.Combine(rootPath, "libraries", groupPath, artifact, version);
        Directory.CreateDirectory(libDir);
        var jarName = $"{artifact}-{version}-{classifier}.jar";
        var jarPath = Path.Combine(libDir, jarName);
        using (var stream = File.Create(jarPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            writeJar(archive);
        }
    }

    private static void WriteVersionJson(string rootPath, string versionName, string libGroup, string artifact,
        string version, string classifier)
    {
        var versionsDir = Path.Combine(rootPath, "versions", versionName);
        Directory.CreateDirectory(versionsDir);
        var jarName = $"{artifact}-{version}-{classifier}.jar";
        var groupPath = libGroup.Replace(".", "/");
        var libPath = $"{groupPath}/{artifact}/{version}/{jarName}";

        var json = new
        {
            id = versionName,
            mainClass = "net.minecraft.client.main.Main",
            libraries = new[]
            {
                new
                {
                    name = $"{libGroup}:{artifact}:{version}",
                    natives = new { windows = classifier },
                    downloads = new
                    {
                        classifiers = new Dictionary<string, object>
                        {
                            [classifier] = new { path = libPath }
                        }
                    }
                }
            }
        };
        File.WriteAllText(Path.Combine(versionsDir, versionName + ".json"),
            JsonSerializer.Serialize(json));
    }

    private static MinecraftInstance CreateInstance(string name, string root, string versionPath)
    {
        var info = new MinecraftVersionInfo(
            name, "release", DateTimeOffset.Now, DateTimeOffset.Now,
            "", "net.minecraft.client.main.Main", name,
            false, false, false, false);
        var jsonPath = Path.Combine(versionPath, name + ".json");
        return new MinecraftInstance(name, root, versionPath, jsonPath,
            MinecraftInstanceState.Ready, info, "",
            false, MinecraftInstanceDisplayType.Auto);
    }

    [Fact]
    public async Task ExtractAsync_ExtractsDllFromNativeJar()
    {
        using var temp = new TempDirectory();
        var root = temp.Path;
        WriteVersionJson(root, "Ver1", "net.example.mods", "libOne", "1.0", "natives-windows");
        CreateNativeJar(root, "net.example.mods", "libOne", "1.0", "natives-windows",
            archive => { var e = archive.CreateEntry("lwjgl.dll"); using var s = e.Open(); s.WriteByte(0x4D); });

        var versionPath = Path.Combine(root, "versions", "Ver1");
        var instance = CreateInstance("Ver1", root, versionPath);
        var svc = new NativesExtractor(new NullLoggerService());
        var dir = await svc.ExtractAsync(instance);

        Assert.NotNull(dir);
        Assert.True(File.Exists(Path.Combine(dir, "lwjgl.dll")));
    }

    [Fact]
    public async Task ExtractAsync_SkipsMetaInf()
    {
        using var temp = new TempDirectory();
        var root = temp.Path;
        WriteVersionJson(root, "Ver2", "net.example.skip", "libTwo", "1.0", "natives-windows");
        CreateNativeJar(root, "net.example.skip", "libTwo", "1.0", "natives-windows",
            archive => { archive.CreateEntry("META-INF/MANIFEST.MF"); archive.CreateEntry("real.dll"); });

        var instance = CreateInstance("Ver2", root, Path.Combine(root, "versions", "Ver2"));
        var svc = new NativesExtractor(new NullLoggerService());
        var dir = await svc.ExtractAsync(instance);

        Assert.NotNull(dir);
        Assert.False(File.Exists(Path.Combine(dir, "META-INF", "MANIFEST.MF")));
        Assert.True(File.Exists(Path.Combine(dir, "real.dll")));
    }

    [Fact]
    public async Task ExtractAsync_RemovesStaleFiles()
    {
        using var temp = new TempDirectory();
        var root = temp.Path;
        WriteVersionJson(root, "Ver3", "net.example.clean", "libThree", "1.0", "natives-windows");
        CreateNativeJar(root, "net.example.clean", "libThree", "1.0", "natives-windows",
            archive => { archive.CreateEntry("new.dll"); });

        var instance = CreateInstance("Ver3", root, Path.Combine(root, "versions", "Ver3"));
        var nativeDir = Path.Combine(Path.Combine(root, "versions", "Ver3"), "Ver3-natives");
        Directory.CreateDirectory(nativeDir);
        File.WriteAllText(Path.Combine(nativeDir, "old.dll"), "stale");

        var svc = new NativesExtractor(new NullLoggerService());
        var dir = await svc.ExtractAsync(instance);

        Assert.False(File.Exists(Path.Combine(dir, "old.dll")));
        Assert.True(File.Exists(Path.Combine(dir, "new.dll")));
    }

    [Fact]
    public async Task ExtractAsync_EmptyJson_ReturnsDirectory()
    {
        using var temp = new TempDirectory();
        var root = temp.Path;
        var versionsDir = Path.Combine(root, "versions", "Empty");
        Directory.CreateDirectory(versionsDir);
        File.WriteAllText(Path.Combine(versionsDir, "Empty.json"), "{}");
        var instance = CreateInstance("Empty", root, versionsDir);
        var svc = new NativesExtractor(new NullLoggerService());
        var dir = await svc.ExtractAsync(instance);
        Assert.NotNull(dir);
        Assert.True(Directory.Exists(dir));
    }
}
