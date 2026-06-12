using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface INativesExtractor
{
    Task<string> ExtractAsync(MinecraftInstance instance, CancellationToken cancellationToken = default);
}
