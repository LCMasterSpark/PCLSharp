using Microsoft.Win32;

namespace PCLrmkBYCSharp.Services;

public sealed class FileDialogService : IFileDialogService
{
    public string? PickFolder(string title, string initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) : initialDirectory,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickJavaExecutable(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "Java 可执行文件 (java.exe)|java.exe|可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) : initialDirectory,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickExecutable(string title, string initialDirectory, string filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*")
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = string.IsNullOrWhiteSpace(filter) ? "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*" : filter,
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) : initialDirectory,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSkinFile(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择离线皮肤",
            Filter = "Minecraft 皮肤 (*.png)|*.png|所有文件 (*.*)|*.*",
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : initialDirectory,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickModpackFile(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "\u9009\u62e9\u6574\u5408\u5305",
            Filter = "Modrinth \u6574\u5408\u5305 (*.mrpack)|*.mrpack|\u6240\u6709\u6587\u4ef6 (*.*)|*.*",
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : initialDirectory,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> PickModFiles(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要安装的 Mod",
            Filter = "Mod 文件 (*.jar;*.zip;*.litemod;*.disabled;*.old)|*.jar;*.zip;*.litemod;*.disabled;*.old|所有文件 (*.*)|*.*",
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : initialDirectory,
            CheckFileExists = true,
            Multiselect = true
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : initialDirectory,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
