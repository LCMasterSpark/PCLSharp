using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface IFileCheckService
{
    string? Check(string localPath, DownloadFileCheck check);
}
