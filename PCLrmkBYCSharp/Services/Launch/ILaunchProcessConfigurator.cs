using System.Diagnostics;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchProcessConfigurator
{
    void PrepareStart(LaunchProfile profile);

    void Configure(Process process);
}
