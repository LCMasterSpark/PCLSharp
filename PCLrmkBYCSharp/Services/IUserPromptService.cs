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

    int? Select(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
    {
        if (options.Count == 0)
        {
            return null;
        }

        var safeIndex = Math.Clamp(defaultIndex, 0, options.Count - 1);
        var indexedOptions = options
            .Select((option, index) => $"{index + 1}. {option}")
            .ToArray();
        var input = Prompt(title, message + "\n\n" + string.Join("\n", indexedOptions), (safeIndex + 1).ToString());
        return int.TryParse(input, out var selected) && selected >= 1 && selected <= options.Count
            ? selected - 1
            : null;
    }
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

    public int? Select(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
    {
        var result = ShowPrompt(title, message, defaultValue: "", UserPromptKind.Choice, options, defaultIndex);
        return result.Confirmed == true && result.SelectedChoiceIndex >= 0 && result.SelectedChoiceIndex < options.Count
            ? result.SelectedChoiceIndex
            : null;
    }

    private UserPromptRequest ShowPrompt(
        string title,
        string message,
        string defaultValue,
        UserPromptKind kind,
        IReadOnlyList<string>? choices = null,
        int selectedChoiceIndex = 0)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return UserPromptRequest.CreateCompleted(title, message, defaultValue, kind, confirmed: kind == UserPromptKind.Message, choices, selectedChoiceIndex);
        }

        return dispatcher.CheckAccess()
            ? ShowPromptOnUiThread(title, message, defaultValue, kind, choices, selectedChoiceIndex)
            : dispatcher.Invoke(() => ShowPromptOnUiThread(title, message, defaultValue, kind, choices, selectedChoiceIndex));
    }

    private UserPromptRequest ShowPromptOnUiThread(
        string title,
        string message,
        string defaultValue,
        UserPromptKind kind,
        IReadOnlyList<string>? choices,
        int selectedChoiceIndex)
    {
        var request = new UserPromptRequest(title, message, defaultValue, kind, choices, selectedChoiceIndex);
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
    Input,
    Choice
}

public sealed class UserPromptRequest
{
    public UserPromptRequest(
        string title,
        string message,
        string inputText,
        UserPromptKind kind,
        IReadOnlyList<string>? choices = null,
        int selectedChoiceIndex = 0)
    {
        Title = title;
        Message = message;
        InputText = inputText;
        Kind = kind;
        Choices = choices ?? [];
        SelectedChoiceIndex = Choices.Count == 0 ? -1 : Math.Clamp(selectedChoiceIndex, 0, Choices.Count - 1);
    }

    public event EventHandler? Completed;

    public string Title { get; }

    public string Message { get; }

    public string InputText { get; set; }

    public UserPromptKind Kind { get; }

    public IReadOnlyList<string> Choices { get; }

    public int SelectedChoiceIndex { get; set; }

    public bool AcceptsInput => Kind == UserPromptKind.Input;

    public bool AcceptsChoice => Kind == UserPromptKind.Choice && Choices.Count > 0;

    public Visibility InputVisibility => AcceptsInput ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ChoiceVisibility => AcceptsChoice ? Visibility.Visible : Visibility.Collapsed;

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

    public static UserPromptRequest CreateCompleted(
        string title,
        string message,
        string inputText,
        UserPromptKind kind,
        bool confirmed,
        IReadOnlyList<string>? choices = null,
        int selectedChoiceIndex = 0)
    {
        var request = new UserPromptRequest(title, message, inputText, kind, choices, selectedChoiceIndex);
        request.Complete(confirmed);
        return request;
    }
}
