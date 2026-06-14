using System.Windows;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class WpfYggdrasilProfileSelector(IUserPromptService? prompts = null) : IYggdrasilProfileSelector
{
    public Task<YggdrasilProfileOption?> SelectAsync(
        string title,
        IReadOnlyList<YggdrasilProfileOption> profiles,
        string cachedProfileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (profiles.Count == 0)
        {
            return Task.FromResult<YggdrasilProfileOption?>(null);
        }

        if (profiles.Count == 1)
        {
            return Task.FromResult<YggdrasilProfileOption?>(profiles[0]);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher?.CheckAccess() == false)
        {
            return dispatcher.InvokeAsync(() => SelectCore(title, profiles, cachedProfileName)).Task;
        }

        return Task.FromResult(SelectCore(title, profiles, cachedProfileName));
    }

    private YggdrasilProfileOption? SelectCore(
        string title,
        IReadOnlyList<YggdrasilProfileOption> profiles,
        string cachedProfileName)
    {
        var defaultIndex = FindDefaultIndex(profiles, cachedProfileName);
        if (prompts is null)
        {
            return profiles[defaultIndex];
        }

        var choices = profiles
            .Select(profile => string.IsNullOrWhiteSpace(profile.Uuid)
                ? profile.Name
                : $"{profile.Name}  ({profile.Uuid})")
            .ToArray();
        var selectedIndex = prompts.Select(
            title,
            "该账号包含多个角色，请选择本次启动使用的角色。",
            choices,
            defaultIndex);
        return selectedIndex is >= 0 && selectedIndex < profiles.Count
            ? profiles[selectedIndex.Value]
            : null;
    }

    private static int FindDefaultIndex(IReadOnlyList<YggdrasilProfileOption> profiles, string cachedProfileName)
    {
        for (var i = 0; i < profiles.Count; i++)
        {
            if (string.Equals(profiles[i].Name, cachedProfileName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }
}
