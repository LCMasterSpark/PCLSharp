using System.Windows;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

public sealed class UserPromptServiceTests
{
    [Fact]
    public void ConfirmCancelsWhenNoUiDispatcherIsAvailable()
    {
        if (Application.Current is not null)
        {
            return;
        }

        var service = new UserPromptService();

        var confirmed = service.Confirm("删除确认", "是否继续？");

        Assert.False(confirmed);
    }

    [Fact]
    public void PromptReturnsNullWhenNoUiDispatcherIsAvailable()
    {
        if (Application.Current is not null)
        {
            return;
        }

        var service = new UserPromptService();

        var value = service.Prompt("重命名", "请输入名称。", "默认");

        Assert.Null(value);
    }
}
