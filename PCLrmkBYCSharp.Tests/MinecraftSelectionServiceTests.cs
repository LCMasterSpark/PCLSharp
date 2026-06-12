using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

public sealed class MinecraftSelectionServiceTests
{
    [Fact]
    public void WriteSelectedInstanceNameStoresOldPclIniVersionKey()
    {
        using var temp = new TempDirectory();
        var service = new MinecraftSelectionService();

        service.WriteSelectedInstanceName(temp.Path, "1.20.1");

        Assert.Equal("1.20.1", service.ReadSelectedInstanceName(temp.Path));
        Assert.Contains("Version:1.20.1", File.ReadAllText(Path.Combine(temp.Path, "PCL.ini")));
    }

    [Fact]
    public void WriteSelectedInstanceNamePreservesOtherPclIniKeys()
    {
        using var temp = new TempDirectory();
        File.WriteAllLines(Path.Combine(temp.Path, "PCL.ini"), ["InstanceCache:123", "Version:old"]);
        var service = new MinecraftSelectionService();

        service.WriteSelectedInstanceName(temp.Path, "new");

        var content = File.ReadAllText(Path.Combine(temp.Path, "PCL.ini"));
        Assert.Contains("InstanceCache:123", content);
        Assert.Contains("Version:new", content);
    }

    [Fact]
    public void ClearInstanceCacheEmptiesOldPclIniCacheKeyAndPreservesSelection()
    {
        using var temp = new TempDirectory();
        File.WriteAllLines(Path.Combine(temp.Path, "PCL.ini"), ["InstanceCache:cached", "Version:1.20.1", "Other:keep"]);
        var service = new MinecraftSelectionService();

        service.ClearInstanceCache(temp.Path);

        var content = File.ReadAllText(Path.Combine(temp.Path, "PCL.ini"));
        Assert.Contains("InstanceCache:", content);
        Assert.DoesNotContain("InstanceCache:cached", content);
        Assert.Contains("Version:1.20.1", content);
        Assert.Contains("Other:keep", content);
    }
}
