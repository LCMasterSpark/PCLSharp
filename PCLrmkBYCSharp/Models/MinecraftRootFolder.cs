namespace PCLrmkBYCSharp.Models;

public sealed record MinecraftRootFolder(
    string Name,
    string Path,
    MinecraftRootFolderType Type,
    int VersionCount = 0,
    bool IsCurrent = false)
{
    public string TypeText => Type switch
    {
        MinecraftRootFolderType.Vanilla => "默认",
        MinecraftRootFolderType.RenamedVanilla => "重命名",
        _ => "自定义"
    };

    public string CurrentText => IsCurrent ? "当前" : "";

    public string VersionCountText => VersionCount <= 0 ? "无版本" : $"{VersionCount} 个版本";
}

public enum MinecraftRootFolderType
{
    Vanilla,
    RenamedVanilla,
    Custom
}
