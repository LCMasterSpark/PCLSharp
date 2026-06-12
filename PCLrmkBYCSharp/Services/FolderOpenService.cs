using System.Diagnostics;
using System.IO;

namespace PCLrmkBYCSharp.Services;

public sealed class FolderOpenService : IFolderOpenService
{
    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("文件夹路径不能为空。", nameof(folderPath));
        }

        Directory.CreateDirectory(folderPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        });
    }
}
