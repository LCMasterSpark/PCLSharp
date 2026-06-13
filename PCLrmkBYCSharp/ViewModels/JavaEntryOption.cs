using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.ViewModels;

public sealed record JavaEntryOption(
    JavaEntry Entry,
    bool IsCompatible,
    bool IsSaved,
    string RequirementText)
{
    public string DisplayName => Entry.DisplayName;

    public string DetailText
    {
        get
        {
            var parts = new List<string>();
            parts.Add(IsCompatible ? "兼容当前版本" : $"不兼容，当前版本需要 {RequirementText}");
            if (IsSaved)
            {
                parts.Add("已保存");
            }

            parts.Add(Entry.PathJava);
            return string.Join(" · ", parts);
        }
    }
}
