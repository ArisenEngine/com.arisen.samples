using ArisenKernel.Diagnostics;
using ArisenKernel.Packages;
using ArisenKernel.Services;

namespace ArisenEngine.Samples;

/// <summary>
/// The capability gallery's content package. It owns the catalogue the shell resolves sample ids
/// through, the hub's entry scene, and the drivers a sample needs: a sample whose content is data
/// alone adds nothing here, and a sample whose content has to move brings the subsystem that moves
/// it, so a driver ships beside the content it owns instead of in a shell every product carries.
/// </summary>
public sealed class SamplesPackage : IPackageEntry
{
    public void OnLoad(IServiceRegistry services)
    {
        KernelLog.Info("[Samples] Package loaded.");
    }

    public void OnUnload(IServiceRegistry services)
    {
        KernelLog.Info("[Samples] Package unloaded.");
    }
}
