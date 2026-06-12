using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface IMinecraftRootFolderService
{
    IReadOnlyList<MinecraftRootFolder> LoadFolders(string defaultRootPath, string selectedRootPath);

    MinecraftRootFolder AddFolder(string folderPath, string? displayName = null);

    MinecraftRootFolder RenameFolder(string folderPath, string displayName);

    void RemoveFolder(string folderPath);
}
