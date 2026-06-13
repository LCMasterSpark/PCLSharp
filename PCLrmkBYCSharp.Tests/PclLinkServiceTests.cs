using PCLrmkBYCSharp.Services.Link;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Tests;

public sealed class PclLinkServiceTests
{
    [Fact]
    public void CreateHostInviteBuildsParseablePclCode()
    {
        var service = new PclLinkService();

        var invite = service.CreateHostInvite(25565);
        var code = service.BuildInviteCode(invite);
        var parsed = service.ParseInviteCode("【" + code + "】");

        Assert.True(parsed.Success);
        Assert.Equal(25565, parsed.Invite?.ServerPort);
        Assert.Equal(2, parsed.Invite?.Version);
        Assert.Contains(code, service.BuildShareText(invite), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseInviteCodeAcceptsLegacyV1Code()
    {
        var service = new PclLinkService();

        var result = service.ParseInviteCode("P04D2-ABCDE-12345");

        Assert.True(result.Success);
        Assert.Equal(1234, result.Invite?.ServerPort);
        Assert.Equal(1, result.Invite?.Version);
        Assert.Equal(0x05E, result.Invite?.DiscoverNodeId);
    }

    [Fact]
    public void ParseInviteCodeNormalizesLookalikeCharacters()
    {
        var service = new PclLinkService();

        var result = service.ParseInviteCode("[P04D2-ABCOI-1OIO1-02000]");

        Assert.True(result.Success);
        Assert.Equal("P04D2-ABC01", result.Invite?.NetworkName);
        Assert.Equal("10101", result.Invite?.NetworkSecret);
    }

    [Fact]
    public void ParseInviteCodeRejectsOtherLauncherRoomCode()
    {
        var service = new PclLinkService();

        var result = service.ParseInviteCode("U/example-room");

        Assert.False(result.Success);
        Assert.Contains("PCL", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateHostInviteRejectsInvalidServerPort()
    {
        var service = new PclLinkService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateHostInvite(80));
    }

    [Fact]
    public void LinkBackendServiceReportsMissingExecutable()
    {
        var service = new LinkBackendService();

        var status = service.GetStatus(LinkProviderKind.Terracotta, "");

        Assert.False(status.CanStart);
        Assert.Equal(LinkBackendReadiness.MissingExecutable, status.Readiness);
        Assert.Contains("尚未配置", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkBackendServiceBuildsReadyPlanWithMaskedSecret()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "terracotta.exe");
        File.WriteAllText(executable, "");
        var service = new LinkBackendService();
        var invite = new LinkInviteInfo(25565, "P63DD-ABCDE", "SECRET", 2, 0);

        var plan = service.CreatePlan(LinkRoomRole.Host, LinkProviderKind.Terracotta, invite, LinkLatencyMode.DirectFirst, "peer.example", executable);

        Assert.True(plan.CanStart);
        Assert.Contains("陶瓦联机", plan.DisplayName, StringComparison.Ordinal);
        Assert.Contains("network-secret=***", plan.PlannedOptions);
        Assert.DoesNotContain("SECRET", string.Join(" ", plan.PlannedOptions), StringComparison.Ordinal);
        Assert.Contains("custom-peer=peer.example", plan.PlannedOptions);
    }

    [Fact]
    public async Task LinkPageViewModelPicksBackendExecutableAndPersistsPath()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "terracotta.exe");
        File.WriteAllText(executable, "");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var viewModel = new LinkPageViewModel(
            new PclLinkService(),
            settings,
            new NullLoggerService(),
            linkBackend: new LinkBackendService(),
            fileDialogs: new ExecutableFileDialogService(executable));

        await viewModel.PickSelectedBackendExecutableCommand.ExecuteAsync(null);

        Assert.Equal(executable, viewModel.TerracottaExecutablePath);
        Assert.Equal(executable, settings.Get(AppSettingKeys.LinkTerracottaExecutablePath, ""));
        Assert.Contains("已就绪", viewModel.BackendStatusText, StringComparison.Ordinal);
    }

    private sealed class ExecutableFileDialogService(string executablePath) : IFileDialogService
    {
        public string? PickFolder(string title, string initialDirectory) => null;

        public string? PickJavaExecutable(string initialDirectory) => null;

        public string? PickExecutable(string title, string initialDirectory, string filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*") => executablePath;

        public string? PickSkinFile(string initialDirectory) => null;

        public string? PickModpackFile(string initialDirectory) => null;

        public IReadOnlyList<string> PickModFiles(string initialDirectory) => [];

        public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter) => null;
    }
}
