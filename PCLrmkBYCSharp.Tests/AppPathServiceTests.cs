using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

public sealed class AppPathServiceTests
{
    [Fact]
    public void RuntimeDirectoryUsesDedicatedAsciiLauncherPath()
    {
        var service = new AppPathService();
        var expectedSuffix = Path.Combine("PCLSharp", "Runtime");

        Assert.EndsWith(expectedSuffix, service.RuntimeDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Plain Craft Launcher Sharp", service.RuntimeDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
