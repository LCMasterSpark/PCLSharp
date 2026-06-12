namespace PCLrmkBYCSharp.Services;

public interface IMinecraftSelectionService
{
    string ReadSelectedInstanceName(string minecraftRootPath);

    void WriteSelectedInstanceName(string minecraftRootPath, string instanceName);

    void ClearInstanceCache(string minecraftRootPath);
}
