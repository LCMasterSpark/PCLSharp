namespace PCLrmkBYCSharp.Tests;

public sealed class XamlLayoutPolicyTests
{
    [Fact]
    public void HighDensityPagesDoNotUseWholePageScrollViewers()
    {
        var launchXaml = File.ReadAllText(GetSourceFile("PCLrmkBYCSharp", "Views", "Pages", "LaunchPage.xaml"));
        var setupXaml = File.ReadAllText(GetSourceFile("PCLrmkBYCSharp", "Views", "Pages", "SetupPage.xaml"));
        var instanceXaml = File.ReadAllText(GetSourceFile("PCLrmkBYCSharp", "Views", "Pages", "InstancePage.xaml"));

        Assert.DoesNotContain("<ScrollViewer", launchXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", setupXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", instanceXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TabControl", setupXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TabControl", instanceXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OriginalImagesAreEmbeddedInsteadOfReferencedFromOldProject()
    {
        var projectRoot = GetSourceDirectory("PCLrmkBYCSharp");
        var imageRoot = Path.Combine(projectRoot, "Resources", "Images");
        var imageCount = Directory.EnumerateFiles(imageRoot, "*.*", SearchOption.AllDirectories).Count();

        Assert.True(imageCount >= 49, $"原 PCL 图片资源应内置到新项目，当前只找到 {imageCount} 个。");

        foreach (var file in EnumerateSourceFiles(projectRoot))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(@"Plain Craft Launcher 2\Images", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"lcmcsharp\Plain Craft Launcher 2", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"_reference\PCL-original", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string GetSourceFile(params string[] relativeParts)
    {
        foreach (var root in GetSearchRoots())
        {
            var directory = root;
            while (directory is not null)
            {
                var candidate = Path.Combine([directory.FullName, .. relativeParts]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("未找到源文件：" + Path.Combine(relativeParts));
    }

    private static string GetSourceDirectory(params string[] relativeParts)
    {
        foreach (var root in GetSearchRoots())
        {
            var directory = root;
            while (directory is not null)
            {
                var candidate = Path.Combine([directory.FullName, .. relativeParts]);
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, relativeParts.Last() + ".csproj")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("未找到源目录：" + Path.Combine(relativeParts));
    }

    private static IEnumerable<DirectoryInfo> GetSearchRoots()
    {
        var callerDirectory = new FileInfo(GetCallerFilePath()).Directory;
        if (callerDirectory is not null)
        {
            yield return callerDirectory;
        }

        yield return new DirectoryInfo(Directory.GetCurrentDirectory());
        yield return new DirectoryInfo(AppContext.BaseDirectory);
    }

    private static string GetCallerFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
    {
        return path;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string projectRoot)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".xaml",
            ".csproj"
        };

        return Directory
            .EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories)
            .Where(file =>
            {
                var relative = Path.GetRelativePath(projectRoot, file);
                if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return extensions.Contains(Path.GetExtension(file));
            });
    }
}
