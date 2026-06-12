namespace PCLrmkBYCSharp.Models;

public sealed record CommunityResourceDependency(
    string? ProjectId,
    string? VersionId,
    string DependencyType)
{
    public bool IsRequired => string.Equals(DependencyType, "required", StringComparison.OrdinalIgnoreCase);

    public string TypeText => DependencyType.Trim().ToLowerInvariant() switch
    {
        "required" => "必需",
        "optional" => "可选",
        "incompatible" => "不兼容",
        "embedded" => "内置",
        "" => "未知类型",
        _ => DependencyType
    };

    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ProjectId))
            {
                parts.Add("项目 " + ProjectId);
            }

            if (!string.IsNullOrWhiteSpace(VersionId))
            {
                parts.Add("版本 " + VersionId);
            }

            var id = parts.Count == 0 ? "未知依赖" : string.Join(" / ", parts);
            return $"{TypeText}：{id}";
        }
    }
}
