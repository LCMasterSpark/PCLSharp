using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchWindowTitleService
{
    string ResolveTitle(LaunchProfile profile);
}
