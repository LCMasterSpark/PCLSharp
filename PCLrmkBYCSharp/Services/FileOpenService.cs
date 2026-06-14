using System.Diagnostics;
using System.IO;

namespace PCLrmkBYCSharp.Services;

public sealed class FileOpenService : IFileOpenService
{
    public void OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("文件路径不能为空。", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("文件不存在。", filePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        });
    }
}
