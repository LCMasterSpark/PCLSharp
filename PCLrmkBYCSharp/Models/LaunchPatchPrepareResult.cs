namespace PCLrmkBYCSharp.Models;

public sealed record LaunchPatchPrepareResult(
    bool Success,
    IReadOnlyList<string> PreparedFiles,
    IReadOnlyList<string> MissingSources,
    string Message)
{
    public static LaunchPatchPrepareResult Ok(IReadOnlyList<string> preparedFiles)
    {
        var message = preparedFiles.Count == 0
            ? "无需准备启动补丁文件"
            : $"已准备 {preparedFiles.Count} 个启动补丁文件";
        return new LaunchPatchPrepareResult(true, preparedFiles, [], message);
    }

    public static LaunchPatchPrepareResult Failed(IReadOnlyList<string> missingSources)
    {
        return new LaunchPatchPrepareResult(false, [], missingSources, $"缺少 {missingSources.Count} 个启动补丁源文件");
    }
}
