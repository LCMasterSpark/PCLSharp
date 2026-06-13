using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public interface ILinkPortAllocator
{
    LinkPortAllocation Allocate(int minecraftPort);
}
