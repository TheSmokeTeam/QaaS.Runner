using System;
using NUnit.Framework;
using QaaS.Runner.Assertions.ConfigurationObjects;

namespace QaaS.Runner.Assertions.Tests.ConfigurationObjectsTests;

[TestFixture]
public class ReportPortalConfigTests
{
    [Test]
    public void Resolve_WithGatewayEndpoint_NormalizesEndpointToApiPath()
    {
        var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Enabled = true,
            Endpoint = "http://localhost:8080",
            Project = "QaaS",
            ApiKey = "local-api-key"
        });

        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:8080/api/"));
    }

    [Test]
    public void Resolve_WithEnvironmentOverrides_UsesEnvironmentValues()
    {
        var originalEnabled = Environment.GetEnvironmentVariable(ReportPortalConfig.EnabledEnvironmentVariable);
        var originalEndpoint = Environment.GetEnvironmentVariable(ReportPortalConfig.EndpointEnvironmentVariable);
        var originalProject = Environment.GetEnvironmentVariable(ReportPortalConfig.ProjectEnvironmentVariable);
        var originalApiKey = Environment.GetEnvironmentVariable(ReportPortalConfig.ApiKeyEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(ReportPortalConfig.EnabledEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable(ReportPortalConfig.EndpointEnvironmentVariable, "http://localhost:8080");
            Environment.SetEnvironmentVariable(ReportPortalConfig.ProjectEnvironmentVariable, "QaaS");
            Environment.SetEnvironmentVariable(ReportPortalConfig.ApiKeyEnvironmentVariable, "env-api-key");

            var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
            {
                Enabled = false,
                Endpoint = "http://ignored.local",
                Project = "Ignored",
                ApiKey = "ignored-api-key"
            });

            Assert.That(settings.Enabled, Is.True);
            Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:8080/api/"));
            Assert.That(settings.Project, Is.EqualTo("QaaS"));
            Assert.That(settings.ApiKey, Is.EqualTo("env-api-key"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ReportPortalConfig.EnabledEnvironmentVariable, originalEnabled);
            Environment.SetEnvironmentVariable(ReportPortalConfig.EndpointEnvironmentVariable, originalEndpoint);
            Environment.SetEnvironmentVariable(ReportPortalConfig.ProjectEnvironmentVariable, originalProject);
            Environment.SetEnvironmentVariable(ReportPortalConfig.ApiKeyEnvironmentVariable, originalApiKey);
        }
    }

    [Test]
    public void Resolve_WhenEnabledAndApiKeyMissing_ThrowsInvalidOperationException()
    {
        var config = new ReportPortalConfig
        {
            Enabled = true,
            Endpoint = "http://localhost:8080",
            Project = "QaaS"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => ReportPortalConfig.Resolve(config));

        Assert.That(exception!.Message, Does.Contain("ReportPortal.ApiKey"));
    }
}
