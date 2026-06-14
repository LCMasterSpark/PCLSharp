using System.Windows;
using System.Windows.Threading;

namespace PCLrmkBYCSharp.Services;

public interface IUserPromptService
{
    void Alert(string title, string message)
    {
        Confirm(title, message);
    }

    bool Confirm(string title, string message);

    string? Prompt(string title, string message, string defaultValue);
}

public sealed class UserPromptService : IUserPromptService
{
    public event EventHandler<UserPromptRequest>? PromptRequested;

    public void Alert(string title, string message)
    {
        ShowPrompt(title, message, defaultValue: "", UserPromptKind.Message);
    }

    public bool Confirm(string title, string message)
    {
        return ShowPrompt(title, message, defaultValue: "", UserPromptKind.Confirm).Confirmed == true;
    }

    public string? Prompt(string title, string message, string defaultValue)
    {
        var result = ShowPrompt(title, message, defaultValue, UserPromptKind.Input);
        return result.Confirmed == true ? result.InputText : null;
    }

    private UserPromptRequest ShowPrompt(string title, string message, string defaultValue, UserPromptKind kind)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return UserPromptRequest.CreateCompleted(title, message, defaultValue, kind, confirmed: kind == UserPromptKind.Message);
        }

        return dispatcher.CheckAccess()
            ? ShowPromptOnUiThread(title, message, defaultValue, kind)
            : dispatcher.Invoke(() => ShowPromptOnUiThread(title, message, defaultValue, kind));
    }

    private UserPromptRequest ShowPromptOnUiThread(string title, string message, string defaultValue, UserPromptKind kind)
    {
        var request = new UserPromptRequest(title, message, defaultValue, kind);
        if (PromptRequested is null)
        {
            request.Complete(kind == UserPromptKind.Message);
            return request;
        }

        PromptRequested.Invoke(this, request);
        if (request.IsCompleted)
        {
            return request;
        }

        var frame = new DispatcherFrame();
        void CompleteFrame(object? sender, EventArgs args)
        {
            frame.Continue = false;
            request.Completed -= CompleteFrame;
        }

        request.Completed += CompleteFrame;
        Dispatcher.PushFrame(frame);
        return request;
    }
}

public enum UserPromptKind
{
    Message,
    Confirm,
    Input
}

public sealed class UserPromptRequest
{
    public UserPromptRequest(string title, string message, string inputText, UserPromptKind kind)
    {
        Title = title;
        Message = message;
        InputText = inputText;
        Kind = kind;
    }

    public event EventHandler? Completed;

    public string Title { get; }

    public string Message { get; }

    public string InputText { get; set; }

    public UserPromptKind Kind { get; }

    public bool AcceptsInput => Kind == UserPromptKind.Input;

    public Visibility InputVisibility => AcceptsInput ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CancelVisibility => Kind == UserPromptKind.Message ? Visibility.Collapsed : Visibility.Visible;

    public string PrimaryButtonText => Kind == UserPromptKind.Message ? "知道了" : "确定";

    public bool IsCompleted { get; private set; }

    public bool? Confirmed { get; private set; }

    public void Complete(bool confirmed)
    {
        if (IsCompleted)
        {
            return;
        }

        Confirmed = confirmed;
        IsCompleted = true;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    public static UserPromptRequest CreateCompleted(string title, string message, string inputText, UserPromptKind kind, bool confirmed)
    {
        var request = new UserPromptRequest(title, message, inputText, kind);
        request.Complete(confirmed);
        return request;
    }
}
