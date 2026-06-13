using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public interface ILinkProcessService
{
    event EventHandler<LinkProcessSnapshot>? SnapshotChanged;

    LinkProcessSnapshot Current { get; }

    LinkProcessSnapshot Start(LinkBackendLaunchPlan plan);

    LinkProcessSnapshot Stop();
}
