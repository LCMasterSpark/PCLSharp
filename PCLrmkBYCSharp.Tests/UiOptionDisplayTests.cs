using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Launch;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Tests;

public sealed class UiOptionDisplayTests
{
    [Fact]
    public void ComboBoxOptionObjectsRenderAsDisplayNames()
    {
        object[] options =
        [
            new LaunchPageViewModel.IntOption(1, "默认窗口"),
            new LaunchPageViewModel.LoginTypeOption(LoginType.Legacy, "离线登录"),
            new InstancePageViewModel.IntOption(2, "按发布时间排序"),
            new InstancePageViewModel.BoolOption(true, "开启"),
            new InstancePageViewModel.DisplayTypeOption(MinecraftInstanceDisplayType.Auto, "自动分类"),
            new InstancePageViewModel.InstanceDetailSectionOption(1, "Mod", "本地 Mod 管理"),
            new SetupPageViewModel.IntOption(1, "优先使用官方源"),
            new SetupPageViewModel.SetupSectionOption(2, "启动", "Java、窗口、内存与 GC"),
            new LoaderVersionOption("Fabric", "0.16.14", true, "Meta")
        ];

        foreach (var option in options)
        {
            var text = option.ToString();

            Assert.NotNull(text);
            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Value =", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DisplayName =", text, StringComparison.Ordinal);
        }
    }
}
