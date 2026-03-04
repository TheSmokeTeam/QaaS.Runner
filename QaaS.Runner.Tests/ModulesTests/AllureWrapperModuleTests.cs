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
        using var scope = container.BeginLifetimeScope();

        var firstResolution = scope.Resolve<AllureWrapper>();
        var secondResolution = scope.Resolve<AllureWrapper>();

        Assert.That(firstResolution, Is.SameAs(secondResolution));
    }
}
