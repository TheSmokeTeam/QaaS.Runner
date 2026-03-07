using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QaaS.Runner.Assertions;
using QaaS.Runner.Assertions.ConfigurationObjects;

namespace QaaS.Runner.Assertions.Tests.ConfigurationObjectsTests;

[TestFixture]
public class ReportPortalConfigTests
{
    private static readonly string[] ManagedEnvironmentVariables =
    [
        ReportPortalConfig.EnabledEnvironmentVariable,
        ReportPortalConfig.EndpointEnvironmentVariable,
        ReportPortalConfig.ProjectEnvironmentVariable,
        ReportPortalConfig.ApiKeyEnvironmentVariable,
        ReportPortalConfig.LaunchNameEnvironmentVariable,
        ReportPortalConfig.DescriptionEnvironmentVariable,
        ReportPortalConfig.DebugModeEnvironmentVariable,
        ReportPortalConfig.BootstrapUsernameEnvironmentVariable,
        ReportPortalConfig.BootstrapPasswordEnvironmentVariable,
        ReportPortalConfig.BootstrapClientIdEnvironmentVariable,
        ReportPortalConfig.BootstrapClientSecretEnvironmentVariable,
        ReportPortalConfig.BotPasswordSeedEnvironmentVariable
    ];

    private Dictionary<string, string?> _originalEnvironment = null!;

    [SetUp]
    public void SetUp()
    {
        _originalEnvironment = ManagedEnvironmentVariables.ToDictionary(
            environmentVariableName => environmentVariableName,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);

        foreach (var environmentVariableName in ManagedEnvironmentVariables)
            Environment.SetEnvironmentVariable(environmentVariableName, null);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var environmentVariable in _originalEnvironment)
            Environment.SetEnvironmentVariable(environmentVariable.Key, environmentVariable.Value);
    }

    [Test]
    public void Resolve_WithNoOverrides_DefaultsToEnabledLocalManagedConfiguration()
    {
        var settings = ReportPortalConfig.Resolve(null, CreateRunDescriptor());

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:8080/api/"));
        Assert.That(settings.Project, Is.EqualTo("Smoke"));
        Assert.That(settings.ApiKey, Is.Null);
        Assert.That(settings.UsesManagedProjectBot, Is.True);
        Assert.That(settings.LaunchName, Is.EqualTo("QaaS Run | QaaS | Session A, Session B | 2025-01-01 10:00:00"));
        Assert.That(settings.Description,
            Is.EqualTo(
                "QaaS captured this run directly from the runner pipeline: live sessions, real assertion outcomes, and the exact shape of QaaS at 2025-01-01 10:00:00."));
        Assert.That(settings.BootstrapUsername, Is.EqualTo("superadmin"));
        Assert.That(settings.BootstrapPassword, Is.EqualTo("erebus"));
        Assert.That(settings.BootstrapClientId, Is.EqualTo("ui"));
        Assert.That(settings.BootstrapClientSecret, Is.EqualTo("uiman"));
        Assert.That(settings.BotPasswordSeed, Is.EqualTo("qaas-reportportal-local"));
    }

    [Test]
    public void Resolve_WithGatewayEndpoint_NormalizesEndpointToApiPath()
    {
        var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Endpoint = "http://localhost:8080",
            Project = "QaaS",
            ApiKey = "local-api-key"
        }, CreateRunDescriptor());

        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:8080/api/"));
    }

    [Test]
    public void Resolve_WithEnvironmentOverrides_UsesEnvironmentValues()
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
        }, CreateRunDescriptor());

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:8080/api/"));
        Assert.That(settings.Project, Is.EqualTo("QaaS"));
        Assert.That(settings.ApiKey, Is.EqualTo("env-api-key"));
        Assert.That(settings.UsesManagedProjectBot, Is.False);
    }

    [Test]
    public void Resolve_WithExplicitDisable_ReturnsDisabledSettingsWithoutNeedingMetadata()
    {
        var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Enabled = false
        }, null);

        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:8080/api/"));
    }

    [Test]
    public void Resolve_WhenProjectCannotBeDerived_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReportPortalConfig.Resolve(
            new ReportPortalConfig
            {
                Enabled = true,
                Endpoint = "http://localhost:8080"
            },
            new ReportPortalRunDescriptor(null, "QaaS", ["Session A"], "run",
                new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero))));

        Assert.That(exception!.Message, Does.Contain("MetaData.Team"));
    }

    private static ReportPortalRunDescriptor CreateRunDescriptor()
    {
        return new ReportPortalRunDescriptor(
            "Smoke",
            "QaaS",
            ["Session A", "Session B"],
            "run",
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero));
    }
}
