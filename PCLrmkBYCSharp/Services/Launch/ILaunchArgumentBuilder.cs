using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchArgumentBuilder
{
    LaunchArgumentBuildResult Build(LaunchRequest request, JavaEntry java, LoginSession login);
}
