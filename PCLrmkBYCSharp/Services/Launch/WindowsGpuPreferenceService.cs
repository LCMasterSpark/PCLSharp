using Microsoft.Win32;
using System.IO;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class WindowsGpuPreferenceService(IAppLoggerService logger) : IGpuPreferenceService
{
    private const string RegistryKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string HighPerformanceValue = "GpuPreference=2;";

    public void SetHighPerformance(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(executablePath);
        using var readKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
        if (string.Equals(readKey?.GetValue(normalizedPath)?.ToString(), HighPerformanceValue, StringComparison.Ordinal))
        {
            logger.Info("无需调整显卡设置：" + normalizedPath);
            return;
        }

        using var writeKey = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true);
        if (writeKey is null)
        {
            throw new InvalidOperationException("无法创建显卡设置注册表项。");
        }

        writeKey.SetValue(normalizedPath, HighPerformanceValue);
        logger.Info("已调整显卡设置：" + normalizedPath);
    }
}
