namespace PCLrmkBYCSharp.Services;

public interface IFileDialogService
{
    string? PickFolder(string title, string initialDirectory);

    string? PickJavaExecutable(string initialDirectory);

    string? PickSkinFile(string initialDirectory);

    string? PickModpackFile(string initialDirectory);

    IReadOnlyList<string> PickModFiles(string initialDirectory);

    string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter);
}
