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

    [Fact]
    public void MessagePromptUsesSingleButtonSemantics()
    {
        var request = UserPromptRequest.CreateCompleted(
            "提示",
            "操作已完成",
            "",
            UserPromptKind.Message,
            confirmed: true);

        Assert.Equal(UserPromptKind.Message, request.Kind);
        Assert.False(request.AcceptsInput);
        Assert.Equal(Visibility.Collapsed, request.InputVisibility);
        Assert.Equal(Visibility.Collapsed, request.CancelVisibility);
        Assert.Equal("知道了", request.PrimaryButtonText);
        Assert.True(request.Confirmed);
    }

    [Fact]
    public void ConfirmPromptKeepsCancelButton()
    {
        var request = UserPromptRequest.CreateCompleted(
            "删除确认",
            "是否继续？",
            "",
            UserPromptKind.Confirm,
            confirmed: false);

        Assert.Equal(UserPromptKind.Confirm, request.Kind);
        Assert.False(request.AcceptsInput);
        Assert.Equal(Visibility.Collapsed, request.InputVisibility);
        Assert.Equal(Visibility.Visible, request.CancelVisibility);
        Assert.Equal("确定", request.PrimaryButtonText);
        Assert.False(request.Confirmed);
    }

    [Fact]
    public void InputPromptShowsTextBoxAndCancelButton()
    {
        var request = UserPromptRequest.CreateCompleted(
            "重命名",
            "请输入名称。",
            "默认",
            UserPromptKind.Input,
            confirmed: true);

        Assert.Equal(UserPromptKind.Input, request.Kind);
        Assert.True(request.AcceptsInput);
        Assert.Equal(Visibility.Visible, request.InputVisibility);
        Assert.Equal(Visibility.Visible, request.CancelVisibility);
        Assert.Equal("确定", request.PrimaryButtonText);
        Assert.Equal("默认", request.InputText);
    }

    [Fact]
    public void ChoicePromptShowsListAndCancelButton()
    {
        var request = UserPromptRequest.CreateCompleted(
            "选择角色",
            "请选择本次启动使用的角色。",
            "",
            UserPromptKind.Choice,
            confirmed: true,
            choices: ["Alex", "Steve"],
            selectedChoiceIndex: 1);

        Assert.Equal(UserPromptKind.Choice, request.Kind);
        Assert.False(request.AcceptsInput);
        Assert.True(request.AcceptsChoice);
        Assert.Equal(Visibility.Collapsed, request.InputVisibility);
        Assert.Equal(Visibility.Visible, request.ChoiceVisibility);
        Assert.Equal(Visibility.Visible, request.CancelVisibility);
        Assert.Equal(1, request.SelectedChoiceIndex);
        Assert.Equal("Steve", request.Choices[request.SelectedChoiceIndex]);
    }
}
