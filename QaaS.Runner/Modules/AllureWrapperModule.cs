using Autofac;
using QaaS.Runner.WrappedExternals;

namespace QaaS.Runner.Modules;

/// <summary>
///     Module that provides the allureWrapper
/// </summary>
public class AllureWrapperModule : Module
{
    /// <inheritdoc />
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterInstance(new AllureWrapper()).SingleInstance();
    }
}