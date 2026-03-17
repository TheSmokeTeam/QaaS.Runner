using Autofac;
using NUnit.Framework;
using QaaS.Runner.Modules;
using QaaS.Runner.WrappedExternals;

namespace QaaS.Runner.Tests.ModulesTests;

[TestFixture]
public class AllureWrapperModuleTests
{
    [Test]
    public void Load_RegistersAllureWrapperAsSingleInstance()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<AllureWrapperModule>();
        using var container = builder.Build();
        using var firstScope = container.BeginLifetimeScope();
        using var secondScope = container.BeginLifetimeScope();

        var rootResolution = container.Resolve<AllureWrapper>();
        var firstScopeResolution = firstScope.Resolve<AllureWrapper>();
        var secondScopeResolution = secondScope.Resolve<AllureWrapper>();

        Assert.That(firstScopeResolution, Is.SameAs(rootResolution));
        Assert.That(secondScopeResolution, Is.SameAs(rootResolution));
    }
}
