using System.Reflection;
using NUnit.Framework;
using QaaS.Runner.ConfigurationObjects;
using QaaS.Runner.Services;

namespace QaaS.Runner.Tests.ServicesTests;

[TestFixture]
public class ReportPortalLaunchManagerTests
{
    [TestCase("localhost:8080")]
    [TestCase("/reportportal")]
    [TestCase("ftp://reportportal.local")]
    public void ValidateConfiguration_WithNonAbsoluteHttpUrl_ThrowsInvalidOperationException(string url)
    {
        var configuration = new ReportPortalConfig
        {
            Enabled = true,
            Server = new ReportPortalServerConfig
            {
                Url = url,
                Project = "demo",
                ApiKey = "token"
            }
        };

        var validateConfigurationMethod = typeof(ReportPortalLaunchManager).GetMethod("ValidateConfiguration",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var exception = Assert.Throws<TargetInvocationException>(() =>
            validateConfigurationMethod.Invoke(null, [configuration]));

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("absolute http/https URL"));
    }

    [Test]
    public void ValidateConfiguration_WithAbsoluteHttpUrl_ReturnsResolvedUri()
    {
        var configuration = new ReportPortalConfig
        {
            Enabled = true,
            Server = new ReportPortalServerConfig
            {
                Url = "http://localhost:8080",
                Project = "demo",
                ApiKey = "token"
            }
        };

        var validateConfigurationMethod = typeof(ReportPortalLaunchManager).GetMethod("ValidateConfiguration",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var result = validateConfigurationMethod.Invoke(null, [configuration]);

        Assert.That(result, Is.TypeOf<Uri>());
        Assert.That(((Uri)result!).AbsoluteUri, Is.EqualTo("http://localhost:8080/"));
    }
}
