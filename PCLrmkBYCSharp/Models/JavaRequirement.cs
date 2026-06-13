namespace PCLrmkBYCSharp.Models;

public sealed record JavaRequirement(
    Version MinVersion,
    Version MaxVersion)
{
    public static JavaRequirement Any { get; } = new(new Version(0, 0, 0, 0), new Version(999, 999, 999, 999));

    public string DisplayText
    {
        get
        {
            if (Equals(this, Any))
            {
                return "任意 Java";
            }

            var min = FormatJavaVersion(MinVersion);
            if (MaxVersion.Major >= 999)
            {
                return $"Java {min} 或更高";
            }

            var max = FormatJavaVersion(MaxVersion);
            return min == max ? $"Java {min}" : $"Java {min} - {max}";
        }
    }

    public bool Allows(JavaEntry entry) => entry.Version >= MinVersion && entry.Version <= MaxVersion;

    private static string FormatJavaVersion(Version version)
    {
        return version.Major == 1 ? version.Minor.ToString() : version.Major.ToString();
    }
}
