using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface ICommunityResourceSearchService
{
    Task<CommunityResourceSearchResult> SearchAsync(CommunityResourceSearchQuery query, CancellationToken cancellationToken = default);
}
