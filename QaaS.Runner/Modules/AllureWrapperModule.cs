using Autofac;
using QaaS.Runner.WrappedExternals;

namespace QaaS.Runner.Modules;

/// <summary>
/// Registers a single <see cref="AllureWrapper" /> instance for the Autofac root container.
/// </summary>
public class AllureWrapperModule : Module
{
    /// <inheritdoc />
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AllureWrapper>().SingleInstance();
    }
}
