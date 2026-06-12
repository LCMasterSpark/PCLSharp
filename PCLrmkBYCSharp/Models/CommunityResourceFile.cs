namespace PCLrmkBYCSharp.Models;

public sealed record CommunityResourceFile(
    string FileName,
    string Url,
    long Size,
    string? Sha1,
    string? Sha512,
    bool IsPrimary)
{
    public string SizeText => Size <= 0 ? "\u672a\u77e5\u5927\u5c0f" : $"{Size / 1024d / 1024d:0.##} MB";
}
