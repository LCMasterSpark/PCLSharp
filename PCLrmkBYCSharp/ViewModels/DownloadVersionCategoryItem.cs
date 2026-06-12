namespace PCLrmkBYCSharp.ViewModels;

public sealed record DownloadVersionCategoryItem(
    string Title,
    string Description,
    int Count)
{
    public string CountText => Count + " 个";
}
