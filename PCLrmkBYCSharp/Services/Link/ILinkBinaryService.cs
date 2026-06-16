using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

/// <summary>
/// 联机后端可执行文件的下载、校验与管理。
/// </summary>
public interface ILinkBinaryService
{
    /// <summary>获取当前已安装版本信息（若有）。</summary>
    LinkBinaryInfo GetInstalledInfo(LinkProviderKind provider);

    /// <summary>获取发布版的最新版本信息（从远程查询）。</summary>
    Task<LinkBinaryReleaseInfo> FetchLatestReleaseAsync(LinkProviderKind provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载指定版本的可执行文件到本地。
    /// <paramref name="release"/> 为 null 时自动取最新版。
    /// 返回下载后的本地路径。
    /// </summary>
    Task<string> DownloadAsync(LinkProviderKind provider, IProgress<long>? progress = null, LinkBinaryReleaseInfo? release = null, CancellationToken cancellationToken = default);
}

/// <summary>本地已安装的版本信息。</summary>
public sealed record LinkBinaryInfo(LinkProviderKind Provider, string ExecutablePath, string Version, long FileSize, DateTime LastModified);

/// <summary>远程发布版本信息。</summary>
public sealed record LinkBinaryReleaseInfo(LinkProviderKind Provider, string Version, Uri DownloadUrl, string ExpectedSha256);
