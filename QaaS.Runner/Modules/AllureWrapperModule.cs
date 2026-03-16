using Autofac;
using QaaS.Runner.WrappedExternals;

namespace QaaS.Runner.Modules;

/// <summary>
/// Registers the runner's Allure wrapper as a shared lifecycle service.
/// </summary>
public class AllureWrapperModule : Module
{
    /// <inheritdoc />
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AllureWrapper>().SingleInstance();
    }
}
