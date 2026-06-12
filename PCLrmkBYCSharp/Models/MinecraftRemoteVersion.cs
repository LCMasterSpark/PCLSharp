namespace PCLrmkBYCSharp.Models;

public sealed record MinecraftRemoteVersion(
    string Id,
    string Type,
    DateTimeOffset ReleaseTime,
    string Url,
    string SourceName)
{
    public string TypeText => Type.Trim().ToLowerInvariant() switch
    {
        "release" => "正式版",
        "snapshot" => "快照版",
        "old_alpha" => "远古 Alpha",
        "old_beta" => "远古 Beta",
        _ => string.IsNullOrWhiteSpace(Type) ? "未知类型" : Type
    };
}
