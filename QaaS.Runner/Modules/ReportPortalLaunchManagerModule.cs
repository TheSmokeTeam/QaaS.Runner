using Autofac;
using QaaS.Runner.Services;

namespace QaaS.Runner.Modules;

/// <summary>
/// Registers the shared ReportPortal launch manager.
/// </summary>
public class ReportPortalLaunchManagerModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ReportPortalLaunchManager>().SingleInstance();
    }
}
