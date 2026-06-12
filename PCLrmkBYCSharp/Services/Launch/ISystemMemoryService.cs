namespace PCLrmkBYCSharp.Services.Launch;

public interface ISystemMemoryService
{
    long AvailablePhysicalMemoryBytes { get; }
}
