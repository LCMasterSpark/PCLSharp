using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class FileCheckService(IAppLoggerService logger) : IFileCheckService
{
    public string? Check(string localPath, DownloadFileCheck check)
    {
        try
        {
            var info = new FileInfo(localPath);
            if (!info.Exists)
            {
                return "文件不存在：" + localPath;
            }

            if (check.ActualSize >= 0 && info.Length != check.ActualSize)
            {
                return $"文件大小应为 {check.ActualSize} B，实际为 {info.Length} B";
            }

            if (check.MinSize >= 0 && info.Length < check.MinSize)
            {
                return $"文件大小应大于等于 {check.MinSize} B，实际为 {info.Length} B";
            }

            if (!string.IsNullOrWhiteSpace(check.Hash))
            {
                var actual = ComputeHash(localPath, check.Hash.Length);
                if (!string.Equals(check.Hash, actual, StringComparison.OrdinalIgnoreCase))
                {
                    return $"文件 Hash 应为 {check.Hash}，实际为 {actual}";
                }
            }

            if (check.IsJson)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(localPath));
                if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                {
                    return "不是有效的 JSON 文件";
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.Warn("检查文件出错：" + ex.Message);
            return ex.Message;
        }
    }

    private static string ComputeHash(string localPath, int hashLength)
    {
        using var stream = File.OpenRead(localPath);
        byte[] hash = hashLength switch
        {
            < 35 => MD5.HashData(stream),
            64 => SHA256.HashData(stream),
            _ => SHA1.HashData(stream)
        };
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
