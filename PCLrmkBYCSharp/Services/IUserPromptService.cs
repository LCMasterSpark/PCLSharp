using System.Windows;
using System.Windows.Threading;

namespace PCLrmkBYCSharp.Services;

public interface IUserPromptService
{
    bool Confirm(string title, string message);

    string? Prompt(string title, string message, string defaultValue);
}

public sealed class UserPromptService : IUserPromptService
{
    public event EventHandler<UserPromptRequest>? PromptRequested;

    public bool Confirm(string title, string message)
    {
        return ShowPrompt(title, message, defaultValue: "", acceptsInput: false).Confirmed == true;
    }

    public string? Prompt(string title, string message, string defaultValue)
    {
        var result = ShowPrompt(title, message, defaultValue, acceptsInput: true);
        return result.Confirmed == true ? result.InputText : null;
    }

    private UserPromptRequest ShowPrompt(string title, string message, string defaultValue, bool acceptsInput)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return UserPromptRequest.CreateCompleted(title, message, defaultValue, acceptsInput, confirmed: false);
        }

        return dispatcher.CheckAccess()
            ? ShowPromptOnUiThread(title, message, defaultValue, acceptsInput)
            : dispatcher.Invoke(() => ShowPromptOnUiThread(title, message, defaultValue, acceptsInput));
    }

    private UserPromptRequest ShowPromptOnUiThread(string title, string message, string defaultValue, bool acceptsInput)
    {
        var request = new UserPromptRequest(title, message, defaultValue, acceptsInput);
        if (PromptRequested is null)
        {
            request.Complete(false);
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

public sealed class UserPromptRequest
{
    public UserPromptRequest(string title, string message, string inputText, bool acceptsInput)
    {
        Title = title;
        Message = message;
        InputText = inputText;
        AcceptsInput = acceptsInput;
    }

    public event EventHandler? Completed;

    public string Title { get; }

    public string Message { get; }

    public string InputText { get; set; }

    public bool AcceptsInput { get; }

    public Visibility InputVisibility => AcceptsInput ? Visibility.Visible : Visibility.Collapsed;

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

    public static UserPromptRequest CreateCompleted(string title, string message, string inputText, bool acceptsInput, bool confirmed)
    {
        var request = new UserPromptRequest(title, message, inputText, acceptsInput);
        request.Complete(confirmed);
        return request;
    }
}
