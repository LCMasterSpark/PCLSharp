namespace PCLrmkBYCSharp.Models;

public sealed record MinecraftVersionInfo(
    string Id,
    string Type,
    DateTimeOffset? ReleaseTime,
    DateTimeOffset? Time,
    string InheritsFrom,
    string MainClass,
    string VanillaVersion,
    bool HasForge,
    bool HasFabric,
    bool HasNeoForge,
    bool HasOptiFine,
    int LibraryCount = 0,
    string AssetsIndex = "",
    bool HasLegacyMinecraftArguments = false,
    bool HasModernArguments = false,
    string ForgeVersion = "",
    string FabricVersion = "",
    string NeoForgeVersion = "",
    string OptiFineVersion = "");
