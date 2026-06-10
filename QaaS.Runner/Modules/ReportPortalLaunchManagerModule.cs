using Autofac;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

namespace QaaS.Runner.Modules;

/// <summary>
/// Registers a single <see cref="ReportPortalLaunchManagerModule" /> instance for the Autofac root container.
/// </summary>
public class ReportPortalLaunchManagerModule : Module
{
    /// <inheritdoc />
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ReportPortalLaunchManager>().SingleInstance();
    }
}
