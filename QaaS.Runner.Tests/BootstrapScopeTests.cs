using Autofac;
using NUnit.Framework;
using QaaS.Runner.WrappedExternals;

namespace QaaS.Runner.Tests;

[TestFixture]
public class BootstrapScopeTests
{
    [Test]
    public void CreateRunnerScope_DoesNotRegisterReportPortalServices()
    {
        using var scope = Bootstrap.CreateRunnerScope();

        var reportPortalRegistrationExists = scope.ComponentRegistry.Registrations.Any(registration =>
            registration.Activator.LimitType.FullName?.Contains("ReportPortal", StringComparison.Ordinal) == true);

        Assert.Multiple(() =>
        {
            Assert.That(scope.IsRegistered<AllureWrapper>(), Is.True);
            Assert.That(reportPortalRegistrationExists, Is.False);
        });
    }
}
