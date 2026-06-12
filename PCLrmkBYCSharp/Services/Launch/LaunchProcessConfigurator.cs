using System.Diagnostics;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LaunchProcessConfigurator(
    IAppSettingsService settings,
    IAppLoggerService logger,
    IGpuPreferenceService? gpuPreferences = null) : ILaunchProcessConfigurator
{
    private readonly IGpuPreferenceService _gpuPreferences = gpuPreferences ?? new WindowsGpuPreferenceService(logger);

    public void PrepareStart(LaunchProfile profile)
    {
        if (!settings.Get(AppSettingKeys.LaunchAdvanceGraphicCard, true))
        {
            return;
        }

        TrySetGpuPreference(profile.Java.PathJava);
        TrySetGpuPreference(Environment.ProcessPath ?? "");
    }

    public void Configure(Process process)
    {
        try
        {
            process.PriorityBoostEnabled = true;
            var priority = MapPriority(settings.Get(AppSettingKeys.LaunchArgumentPriority, 1));
            if (priority is not null)
            {
                process.PriorityClass = priority.Value;
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "设置游戏进程优先级失败");
        }
    }

    private void TrySetGpuPreference(string executablePath)
    {
        try
        {
            _gpuPreferences.SetHighPerformance(executablePath);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "调整显卡设置失败，Minecraft 可能会使用默认显卡运行");
        }
    }

    public static ProcessPriorityClass? MapPriority(int setting)
    {
        return setting switch
        {
            0 => ProcessPriorityClass.AboveNormal,
            2 => ProcessPriorityClass.BelowNormal,
            _ => null
        };
    }
}
