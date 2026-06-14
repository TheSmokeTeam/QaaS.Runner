using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QaaS.Runner.Assertions;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

namespace QaaS.Runner.Assertions.Tests.ConfigurationObjectsTests;

[TestFixture]
public class ReportPortalConfigTests
{
    [SetUp]
    public void SetUp()
    {
        ReportPortalConfig.RegisterDefaults(enabled: false);
    }

    [Test]
    public void Resolve_WithNoOverrides_DefaultsToRegisteredDisabledConfiguration()
    {
        var settings = new ReportPortalConfig().Resolve(CreateRunDescriptor());

        Assert.That(settings.Enabled, Is.False);
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
        var settings = new ReportPortalConfig
        {
            Endpoint = "http://localhost:8080",
            Project = "QaaS",
            ApiKey = "local-api-key"
        }.Resolve(CreateRunDescriptor());

        var succeeded = settings.TryGetEndpointUri(out var endpointUri, out var failureReason);

        Assert.That(succeeded, Is.True);
        Assert.That(failureReason, Is.Null);
        Assert.That(endpointUri, Is.Not.Null);
        Assert.That(endpointUri!.AbsoluteUri, Is.EqualTo("http://localhost:8080/api/"));
    }

    [Test]
    public void Resolve_WithEndpointAndApiKeyOnlyInYaml_UsesYamlValues()
    {
        var settings = new ReportPortalConfig
        {
            Enabled = true,
            Endpoint = "http://from-yaml.local",
            ApiKey = "yaml-api-key"
        }.Resolve(CreateRunDescriptor());

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.EqualTo("http://from-yaml.local"));
        Assert.That(settings.ApiKey, Is.EqualTo("yaml-api-key"));
    }

    [Test]
    public void Resolve_WithRegisteredDefaults_UsesDefaultsButStillRoutesByTeam()
    {
        ReportPortalConfig.RegisterDefaults(
            enabled: true,
            reportPortalUri: "http://localhost:8080",
            reportPortalApiKey: "default-api-key");

        var settings = new ReportPortalConfig
        {
            Project = "IgnoredProject"
        }.Resolve(CreateRunDescriptor());

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:8080"));
        Assert.That(settings.RequestedProjectName, Is.EqualTo("Smoke"));
        Assert.That(settings.ApiKey, Is.EqualTo("default-api-key"));
        Assert.That(settings.IgnoredProjectOverride, Is.EqualTo("IgnoredProject"));
    }

    [Test]
    public void Resolve_WithYamlOverrides_UsesYamlValuesBeforeRegisteredDefaults()
    {
        ReportPortalConfig.RegisterDefaults(
            enabled: true,
            reportPortalUri: "http://default.local",
            reportPortalApiKey: "default-api-key");

        var settings = new ReportPortalConfig
        {
            Endpoint = "http://from-yaml.local",
            ApiKey = "yaml-api-key"
        }.Resolve(CreateRunDescriptor());

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.EqualTo("http://from-yaml.local"));
        Assert.That(settings.ApiKey, Is.EqualTo("yaml-api-key"));
    }

    [Test]
    public void Resolve_WithExplicitDisable_ReturnsDisabledSettingsWithoutNeedingMetadata()
    {
        ReportPortalConfig.RegisterDefaults(
            enabled: true,
            reportPortalUri: "http://default.local",
            reportPortalApiKey: "default-api-key");

        var settings = new ReportPortalConfig
        {
            Enabled = false
        }.Resolve(null);

        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.Endpoint, Is.EqualTo("http://default.local"));
        Assert.That(settings.RequestedProjectName, Is.Null);
    }

    [Test]
    public void Resolve_WhenProjectCannotBeDerived_DoesNotThrowAndLeavesRequestedProjectNameNull()
    {
        var settings = new ReportPortalConfig
        {
            Enabled = true,
            Endpoint = "http://localhost:8080"
        }.Resolve(new ReportPortalLaunchDescriptor(null, "QaaS", ["Session A"], "run",
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero)));

        Assert.That(settings.RequestedProjectName, Is.Null);
        Assert.That(settings.System, Is.EqualTo("QaaS"));
    }

    [Test]
    public void TryGetEndpointUri_WithMissingEndpoint_ReturnsFailureReason()
    {
        var settings = new ReportPortalConfig
        {
            Enabled = true
        }.Resolve(CreateRunDescriptor());

        var succeeded = settings.TryGetEndpointUri(out var endpointUri, out var failureReason);

        Assert.That(succeeded, Is.False);
        Assert.That(endpointUri, Is.Null);
        Assert.That(failureReason, Does.Contain("ReportPortal.Endpoint"));
    }

    [Test]
    public void BuildLaunchAttributes_IncludesTeamSystemSessionsAndStaticAttributes()
    {
        var settings = new ReportPortalConfig
        {
            Attributes = new Dictionary<string, string>
            {
                ["Component"] = "Auth",
                ["Owner"] = "Smoke Team"
            }
        }.Resolve(CreateRunDescriptor());

        var attributes = settings.BuildLaunchAttributes();

        Assert.That(attributes.Any(attribute => attribute.Key == "tool" && attribute.Value == "QaaS"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "team" && attribute.Value == "Smoke"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "system" && attribute.Value == "QaaS"), Is.True);
        Assert.That(attributes.Count(attribute => attribute.Key == "session"), Is.EqualTo(2));
        Assert.That(attributes.Any(attribute => attribute.Key == "Component" && attribute.Value == "Auth"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "Owner" && attribute.Value == "Smoke Team"), Is.True);
    }

    private static ReportPortalLaunchDescriptor CreateRunDescriptor()
    {
        return new ReportPortalLaunchDescriptor(
            "Smoke",
            "QaaS",
            ["Session A", "Session B"],
            "run",
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero));
    }
}
