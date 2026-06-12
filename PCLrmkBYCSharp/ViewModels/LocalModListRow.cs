using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.ViewModels;

public sealed class LocalModListRow
{
    public LocalModListRow(LocalModFile mod, int nameStyle, bool isSelected, LocalModUpdateInfo? updateInfo)
    {
        Mod = mod;
        IsSelected = isSelected;
        UpdateInfo = updateInfo;
        if (nameStyle == 1)
        {
            Title = mod.BaseName;
            Subtitle = "";
            Description = BuildTranslatedDescription(mod);
        }
        else
        {
            Title = mod.DisplayName;
            Subtitle = string.IsNullOrWhiteSpace(mod.Version) ? "" : mod.Version;
            Description = BuildFileDescription(mod);
        }
    }

    public LocalModFile Mod { get; }

    public bool IsSelected { get; }

    public LocalModUpdateInfo? UpdateInfo { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Description { get; }

    public string StateText => Mod.StateText;

    public string SizeText => Mod.SizeText;

    public bool IsEnabled => Mod.IsEnabled;

    public bool HasUpdate => UpdateInfo?.HasUpdate == true;

    public string UpdateText => UpdateInfo?.Summary ?? "未检查更新";

    public string DetailText
    {
        get
        {
            var parts = new List<string>
            {
                "文件：" + Mod.FileName,
                "状态：" + Mod.StateText,
                "大小：" + Mod.SizeText
            };
            if (!string.IsNullOrWhiteSpace(Mod.Version))
            {
                parts.Add("版本：" + Mod.Version);
            }

            if (!string.IsNullOrWhiteSpace(Mod.Description))
            {
                parts.Add("描述：" + Mod.Description);
            }

            if (UpdateInfo is not null)
            {
                parts.Add("更新：" + UpdateInfo.Summary);
            }

            return string.Join(Environment.NewLine, parts);
        }
    }

    private static string BuildTranslatedDescription(LocalModFile mod)
    {
        var parts = new List<string>();
        if (!string.Equals(mod.DisplayName, mod.BaseName, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(mod.DisplayName);
        }

        if (!string.IsNullOrWhiteSpace(mod.Version))
        {
            parts.Add(mod.Version);
        }

        if (!string.IsNullOrWhiteSpace(mod.Description))
        {
            parts.Add(mod.Description);
        }

        return parts.Count == 0 ? mod.EnabledFileName : string.Join(" / ", parts);
    }

    private static string BuildFileDescription(LocalModFile mod)
    {
        return string.IsNullOrWhiteSpace(mod.Description)
            ? mod.BaseName
            : mod.BaseName + ": " + mod.Description;
    }
}
