namespace PCLrmkBYCSharp.Models;

public sealed record MinecraftInstance(
    string Name,
    string RootPath,
    string VersionPath,
    string JsonPath,
    MinecraftInstanceState State,
    MinecraftVersionInfo Version,
    string ErrorMessage,
    bool IsStar = false,
    MinecraftInstanceDisplayType DisplayType = MinecraftInstanceDisplayType.Auto,
    string CustomInfo = "")
{
    public bool HasError => State != MinecraftInstanceState.Ready;

    public bool IsHidden => DisplayType == MinecraftInstanceDisplayType.Hidden;

    public string StarText => IsStar ? "★" : "";

    public string HiddenText => IsHidden ? "隐藏" : "";

    public string GroupName
    {
        get
        {
            if (IsHidden)
            {
                return "隐藏的版本";
            }

            if (HasError)
            {
                return "错误的版本";
            }

            if (IsStar)
            {
                return "收藏夹";
            }

            if (Version.HasForge || Version.HasNeoForge || Version.HasFabric || Version.HasOptiFine || DisplayType == MinecraftInstanceDisplayType.Api)
            {
                return "可安装 Mod";
            }

            return DisplayType switch
            {
                MinecraftInstanceDisplayType.Rubbish => "不常用版本",
                MinecraftInstanceDisplayType.Fool => "愚人节版本",
                MinecraftInstanceDisplayType.OriginalLike => "常规版本",
                _ => "常规版本"
            };
        }
    }

    public string DisplayInfo => string.IsNullOrWhiteSpace(CustomInfo) ? LoaderSummary : CustomInfo;

    public string DisplayVersion => string.IsNullOrWhiteSpace(Version.VanillaVersion)
        ? Version.Id
        : Version.VanillaVersion;

    public string IconPath
    {
        get
        {
            if (HasError)
            {
                return "/Resources/Images/Icons/Disabled.png";
            }

            if (DisplayType == MinecraftInstanceDisplayType.Fool)
            {
                return "/Resources/Images/Blocks/Egg.png";
            }

            if (DisplayType == MinecraftInstanceDisplayType.Rubbish)
            {
                return "/Resources/Images/Blocks/CobbleStone.png";
            }

            if (Version.HasFabric)
            {
                return "/Resources/Images/Blocks/Fabric.png";
            }

            if (Version.HasNeoForge)
            {
                return "/Resources/Images/Blocks/NeoForge.png";
            }

            if (Version.HasForge)
            {
                return "/Resources/Images/Blocks/Anvil.png";
            }

            if (Version.HasOptiFine)
            {
                return "/Resources/Images/Blocks/Grass.png";
            }

            return Version.Type.ToLowerInvariant() switch
            {
                "old_alpha" => "/Resources/Images/ReleaseTypes/Alpha.png",
                "old_beta" => "/Resources/Images/ReleaseTypes/Beta.png",
                "snapshot" => "/Resources/Images/ReleaseTypes/Beta.png",
                _ => "/Resources/Images/ReleaseTypes/Release.png"
            };
        }
    }

    public string IconDescription
    {
        get
        {
            if (HasError)
            {
                return "版本异常";
            }

            if (Version.HasFabric)
            {
                return "Fabric";
            }

            if (Version.HasNeoForge)
            {
                return "NeoForge";
            }

            if (Version.HasForge)
            {
                return "Forge";
            }

            if (Version.HasOptiFine)
            {
                return "OptiFine";
            }

            return Version.Type switch
            {
                "old_alpha" => "远古 Alpha",
                "old_beta" => "远古 Beta",
                "snapshot" => "快照版",
                _ => "正式版"
            };
        }
    }

    public string LoaderSummary
    {
        get
        {
            var loaders = new List<string>();
            if (Version.HasForge) loaders.Add("Forge");
            if (Version.HasNeoForge) loaders.Add("NeoForge");
            if (Version.HasFabric) loaders.Add("Fabric");
            if (Version.HasOptiFine) loaders.Add("OptiFine");
            return loaders.Count == 0 ? "原版/未知" : string.Join(", ", loaders);
        }
    }
}
