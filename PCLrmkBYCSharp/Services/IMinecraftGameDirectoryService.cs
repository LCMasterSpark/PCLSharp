using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface IMinecraftGameDirectoryService
{
    MinecraftGameDirectory Resolve(MinecraftInstance instance);

    MinecraftGameDirectory Resolve(LaunchRequest request);

    string GetPath(MinecraftInstance instance, bool isIsolated);
}

public sealed record MinecraftGameDirectory(string Path, bool IsIsolated);
