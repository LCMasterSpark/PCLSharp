namespace PCLrmkBYCSharp.Models;

public sealed record JavaRequirement(
    Version MinVersion,
    Version MaxVersion)
{
    public static JavaRequirement Any { get; } = new(new Version(0, 0, 0, 0), new Version(999, 999, 999, 999));

    public bool Allows(JavaEntry entry) => entry.Version >= MinVersion && entry.Version <= MaxVersion;
}
