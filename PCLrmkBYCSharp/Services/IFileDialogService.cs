namespace PCLrmkBYCSharp.Services;

public interface IFileDialogService
{
    string? PickFolder(string title, string initialDirectory);

    string? PickJavaExecutable(string initialDirectory);

    string? PickExecutable(string title, string initialDirectory, string filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*")
    {
        return null;
    }

    string? PickSkinFile(string initialDirectory);

    string? PickModpackFile(string initialDirectory);

    IReadOnlyList<string> PickModFiles(string initialDirectory);

    string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter);
}
