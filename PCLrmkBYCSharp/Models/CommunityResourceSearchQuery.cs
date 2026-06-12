namespace PCLrmkBYCSharp.Models;

public sealed record CommunityResourceSearchQuery(
    CommunityResourceType Type,
    string SearchText,
    string GameVersion,
    string Loader,
    int Offset = 0,
    int Limit = 40);
