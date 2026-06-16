using System.IO;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.Tests;

public sealed class FileCheckServiceTests
{
    [Fact]
    public void Check_ReturnsNullWhenHashMatches()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "t.bin");
        File.WriteAllText(path, "hello world");
        var svc = new FileCheckService(new NullLoggerService());
        var c = new DownloadFileCheck(Hash: "5eb63bbbe01eeed093cb22bb8f5acdc3", ActualSize: 11, MinSize: 1);
        Assert.Null(svc.Check(path, c));
    }

    [Fact]
    public void Check_ReturnsErrorWhenHashMismatches()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "t.bin");
        File.WriteAllText(path, "hello world");
        var svc = new FileCheckService(new NullLoggerService());
        var c = new DownloadFileCheck(Hash: "00000000000000000000000000000000", ActualSize: 11);
        Assert.NotNull(svc.Check(path, c));
    }

    [Fact]
    public void Check_ReturnsErrorWhenSizeBelowMinimum()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "t.bin");
        File.WriteAllText(path, "h");
        var svc = new FileCheckService(new NullLoggerService());
        Assert.NotNull(svc.Check(path, new DownloadFileCheck(MinSize: 10)));
    }

    [Fact]
    public void Check_ReturnsNullWhenNoChecksSpecified()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "t.bin");
        File.WriteAllText(path, "");
        var svc = new FileCheckService(new NullLoggerService());
        Assert.Null(svc.Check(path, new DownloadFileCheck()));
    }

    [Fact]
    public void Check_ReturnsErrorWhenFileMissing()
    {
        var svc = new FileCheckService(new NullLoggerService());
        Assert.NotNull(svc.Check(@"C:\no\file.bin", new DownloadFileCheck(MinSize: 1)));
    }
}
