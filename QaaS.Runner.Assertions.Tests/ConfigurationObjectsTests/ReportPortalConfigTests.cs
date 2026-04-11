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
        ReportPortalConfig.ApiKeyEnvironmentVariable
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
    public void Resolve_WithNoOverrides_DefaultsToPassiveEnabledConfiguration()
    {
        var settings = ReportPortalConfig.Resolve(null, CreateRunDescriptor());

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.Null);
        Assert.That(settings.RequestedProjectName, Is.EqualTo("Smoke"));
        Assert.That(settings.ApiKey, Is.Null);
        Assert.That(settings.LaunchName, Is.EqualTo("QaaS Run | Smoke | QaaS | Session A, Session B"));
        Assert.That(settings.Description,
            Is.EqualTo(
                "QaaS captured this run directly from the runner pipeline: live sessions, real assertion outcomes, and the exact shape of QaaS at 2025-01-01 10:00:00. Sessions=[Session A, Session B]. LaunchAttributes=[No additional launch attributes.]"));
    }

    [Test]
    public void TryGetEndpointUri_WithGatewayEndpoint_NormalizesEndpointToApiPath()
    {
        Environment.SetEnvironmentVariable(ReportPortalConfig.EndpointEnvironmentVariable, "http://localhost:8080");

        var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Project = "QaaS",
            ApiKey = "local-api-key"
        }, CreateRunDescriptor());

        var succeeded = settings.TryGetEndpointUri(out var endpointUri, out var failureReason);

        Assert.That(succeeded, Is.True);
        Assert.That(failureReason, Is.Null);
        Assert.That(endpointUri, Is.Not.Null);
        Assert.That(endpointUri!.AbsoluteUri, Is.EqualTo("http://localhost:8080/api/"));
    }

    [Test]
    public void Resolve_WithEndpointAndApiKeyOnlyInYaml_IgnoresThemBecauseRuntimeUsesEnvironmentVariables()
    {
        var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Endpoint = "http://from-yaml.local",
            ApiKey = "yaml-api-key"
        }, CreateRunDescriptor());

        Assert.That(settings.Endpoint, Is.Null);
        Assert.That(settings.ApiKey, Is.Null);
    }

    [Test]
    public void Resolve_WithEnvironmentOverrides_UsesEnvironmentValuesButStillRoutesByTeam()
    {
        Environment.SetEnvironmentVariable(ReportPortalConfig.EnabledEnvironmentVariable, "true");
        Environment.SetEnvironmentVariable(ReportPortalConfig.EndpointEnvironmentVariable, "http://localhost:8080");
        Environment.SetEnvironmentVariable(ReportPortalConfig.ProjectEnvironmentVariable, "IgnoredProject");
        Environment.SetEnvironmentVariable(ReportPortalConfig.ApiKeyEnvironmentVariable, "env-api-key");

        var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Enabled = false,
            Endpoint = "http://ignored.local",
            Project = "Ignored",
            ApiKey = "ignored-api-key"
        }, CreateRunDescriptor());

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:8080"));
        Assert.That(settings.RequestedProjectName, Is.EqualTo("Smoke"));
        Assert.That(settings.ApiKey, Is.EqualTo("env-api-key"));
        Assert.That(settings.IgnoredProjectOverride, Is.EqualTo("IgnoredProject"));
    }

    [Test]
    public void Resolve_WithExplicitDisable_ReturnsDisabledSettingsWithoutNeedingMetadata()
    {
        var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Enabled = false
        }, null);

        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.Endpoint, Is.Null);
        Assert.That(settings.RequestedProjectName, Is.Null);
    }

    [Test]
    public void Resolve_WhenProjectCannotBeDerived_DoesNotThrowAndLeavesRequestedProjectNameNull()
    {
        var settings = ReportPortalConfig.Resolve(
            new ReportPortalConfig
            {
                Enabled = true,
                Endpoint = "http://localhost:8080"
            },
            new ReportPortalRunDescriptor(null, "QaaS", ["Session A"], "run",
                new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero)));

        Assert.That(settings.RequestedProjectName, Is.Null);
        Assert.That(settings.System, Is.EqualTo("QaaS"));
    }

    [Test]
    public void TryGetEndpointUri_WithMissingEndpoint_ReturnsFailureReason()
    {
        var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Enabled = true
        }, CreateRunDescriptor());

        var succeeded = settings.TryGetEndpointUri(out var endpointUri, out var failureReason);

        Assert.That(succeeded, Is.False);
        Assert.That(endpointUri, Is.Null);
        Assert.That(failureReason, Does.Contain(ReportPortalConfig.EndpointEnvironmentVariable));
    }

    [Test]
    public void BuildLaunchAttributes_IncludesTeamSystemSessionsAndStaticAttributes()
    {
        var settings = ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Attributes = new Dictionary<string, string>
            {
                ["Component"] = "Auth",
                ["Owner"] = "Smoke Team"
            }
        }, CreateRunDescriptor());

        var attributes = settings.BuildLaunchAttributes();

        Assert.That(attributes.Any(attribute => attribute.Key == "tool" && attribute.Value == "QaaS"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "team" && attribute.Value == "Smoke"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "system" && attribute.Value == "QaaS"), Is.True);
        Assert.That(attributes.Count(attribute => attribute.Key == "session"), Is.EqualTo(2));
        Assert.That(attributes.Any(attribute => attribute.Key == "Component" && attribute.Value == "Auth"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "Owner" && attribute.Value == "Smoke Team"), Is.True);
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
