using System.Runtime.InteropServices;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class SystemMemoryService : ISystemMemoryService
{
    public long AvailablePhysicalMemoryBytes
    {
        get
        {
            var status = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(status))
            {
                return unchecked((long)Math.Min(status.AvailPhys, (ulong)long.MaxValue));
            }

            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
