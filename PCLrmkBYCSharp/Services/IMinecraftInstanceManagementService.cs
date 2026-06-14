using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface IMinecraftInstanceManagementService
{
    MinecraftInstanceMetadata ReadMetadata(string versionPath);

    void SetStar(MinecraftInstance instance, bool isStar);

    void SetDisplayType(MinecraftInstance instance, MinecraftInstanceDisplayType displayType);

    void SetCustomInfo(MinecraftInstance instance, string customInfo);

    string RenameInstance(MinecraftInstance instance, string newName);

    string CloneInstance(MinecraftInstance instance, string newName);

    string ImportInstance(string sourceVersionPath, string targetMinecraftRoot, string? targetName = null);

    void DeleteInstance(MinecraftInstance instance, bool permanent = false);
}

public sealed record MinecraftInstanceMetadata(
    bool IsStar,
    MinecraftInstanceDisplayType DisplayType,
    string CustomInfo,
    string VanillaVersion = "",
    string ForgeVersion = "",
    string FabricVersion = "",
    string NeoForgeVersion = "",
    string OptiFineVersion = "");
