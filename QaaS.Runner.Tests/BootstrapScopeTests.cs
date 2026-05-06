using Autofac;
using NUnit.Framework;
using QaaS.Runner.Assertions;

namespace QaaS.Runner.Tests;

[TestFixture]
public class BootstrapScopeTests
{
    [Test]
    public void CreateRunnerScope_RegistersReportPortalLaunchManagerAsSingleInstance()
    {
        using var scope = Bootstrap.CreateRunnerScope();

        var firstResolution = scope.Resolve<ReportPortalLaunchManager>();
        var secondResolution = scope.Resolve<ReportPortalLaunchManager>();

        Assert.That(secondResolution, Is.SameAs(firstResolution));
    }
}
