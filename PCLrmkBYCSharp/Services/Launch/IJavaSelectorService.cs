using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IJavaSelectorService
{
    JavaRequirement GetRequirement(MinecraftInstance instance);

    JavaEntry? SelectBest(MinecraftInstance instance, IEnumerable<JavaEntry> candidates, string? preferredJavaPath);
}
