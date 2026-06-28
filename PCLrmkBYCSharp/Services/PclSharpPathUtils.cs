using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PCLrmkBYCSharp.Services;

/// <summary>
/// 本地路径工具——替换旧 MeloongCore.PathUtils 的短路径转换功能。
/// </summary>
public static class PclSharpPathUtils
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetShortPathNameW(string lpszLongPath, [Out] char[] buffer, int cchBuffer);

    /// <summary>
    /// 若路径较长，则尽量将其转换为短路径。
    /// 结果的开头不含 \\?\，结尾不含分隔符。
    /// </summary>
    public static string ToShortPath(string pathName, bool keepFileName = false)
    {
        if (string.IsNullOrEmpty(pathName)) return pathName;
        if (!pathName.Contains(':')) return pathName;
        if (pathName.Length <= 200) return pathName;

        pathName = ForCompare(pathName);

        // 保留文件名
        var pathToKeep = "";
        var pathToShorten = pathName;
        if (pathName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            keepFileName = true;
        if (keepFileName && FileExists(pathName))
        {
            pathToKeep = GetLastPart(pathName);
            pathToShorten = RemoveLastPart(pathName);
        }

        // 逐级向上寻找已存在的文件夹，将不存在的部分挪到 suffix
        while (!DirectoryExists(pathToShorten) && !FileExists(pathToShorten))
        {
            var parentPath = Path.GetDirectoryName(pathToShorten);
            if (string.IsNullOrEmpty(parentPath) || parentPath == pathToShorten)
                return pathName;
            pathToKeep = Path.Combine(GetLastPart(pathToShorten), pathToKeep);
            pathToShorten = parentPath;
        }

        if (pathToShorten.Length <= 10) return pathName;

        var buffer = new char[260];
        var result = GetShortPathNameW(ForApi(pathToShorten), buffer, buffer.Length);
        if (result == 0) return pathName;

        var shortPath = new string(buffer, 0, result);
        return RemoveSlashSuffix(RemoveExtendedPrefix(Path.Combine(shortPath, pathToKeep)));
    }

    #region 内部辅助方法

    private static string ForCompare(string pathName)
    {
        return RemoveSlashSuffix(RemoveExtendedPrefix(Path.GetFullPath(pathName))
            .Replace('/', '\\'));
    }

    private static string ForApi(string pathName)
    {
        pathName = ToExtendedFormat(pathName);
        if (pathName.EndsWith(':')) pathName += @"\";
        if (pathName.EndsWith(@":\", StringComparison.Ordinal)) return pathName;
        return RemoveSlashSuffix(pathName);
    }

    private static string ToExtendedFormat(string pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName)) return pathName;
        if (!pathName.Contains(':')) return pathName;
        if (pathName.StartsWith(@"\\?\", StringComparison.Ordinal)) return pathName;

        pathName = RemoveSlashSuffix(pathName).Replace('/', '\\');
        if (pathName.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + pathName[2..];
        return @"\\?\" + pathName;
    }

    private static string RemoveExtendedPrefix(string pathName)
    {
        if (pathName.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            return @"\\" + pathName[7..];  // skip \\?\UNC\
        if (pathName.StartsWith(@"\\?\", StringComparison.Ordinal))
            return pathName[4..];  // skip \\?\
        return pathName;
    }

    private static string GetLastPart(string pathName)
    {
        // 网络路径去除参数
        if (pathName.Contains("://"))
            pathName = RemoveSlashSuffix(pathName.Split('?', '#')[0]);
        else
            pathName = RemoveSlashSuffix(pathName);

        var lastSlash = pathName.LastIndexOfAny(['\\', '/']);
        return lastSlash < 0 ? pathName : pathName[(lastSlash + 1)..];
    }

    private static string RemoveLastPart(string pathName)
    {
        if (pathName.Contains("://"))
            pathName = RemoveSlashSuffix(pathName.Split('?', '#')[0]);
        pathName = RemoveSlashSuffix(pathName);
        var lastSlash = pathName.LastIndexOfAny(['\\', '/']);
        return lastSlash < 0 ? pathName : pathName[..lastSlash];
    }

    private static string RemoveSlashSuffix(string folder)
    {
        return folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string AddSlashSuffix(string folder)
    {
        return folder.EndsWith(Path.DirectorySeparatorChar) || folder.EndsWith(Path.AltDirectorySeparatorChar)
            ? folder
            : folder + Path.DirectorySeparatorChar;
    }

    private static bool FileExists(string path)
    {
        try { return File.Exists(path); }
        catch { return false; }
    }

    private static bool DirectoryExists(string path)
    {
        try { return Directory.Exists(path); }
        catch { return false; }
    }

    #endregion
}
