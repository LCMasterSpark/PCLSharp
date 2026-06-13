using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public interface ILinkProcessService
{
    LinkProcessSnapshot Current { get; }

    LinkProcessSnapshot Start(LinkBackendLaunchPlan plan);

    LinkProcessSnapshot Stop();
}
