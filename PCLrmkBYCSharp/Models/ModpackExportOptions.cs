namespace PCLrmkBYCSharp.Models;

public sealed record ModpackExportOptions(
    bool IncludeConfig = true,
    bool IncludeMods = true,
    bool IncludeResourcePacks = true,
    bool IncludeShaderPacks = true,
    bool IncludeSaves = true,
    bool IncludeScreenshots = false,
    bool IncludeOptions = true,
    bool IncludeExtraData = true);
