using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

namespace QaaS.Runner.Assertions.Tests.ConfigurationObjectsTests;

[TestFixture]
public class ReportPortalSettingsTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp()
    {
        ReportPortalConfig.RegisterDefaults(enabled: false);
    }

    [Test]
    public void Constructor_WithNoOverrides_DefaultsToRegisteredDisabledConfigurationAndMetadataProject()
    {
        var settings = CreateSettings(new ReportPortalConfig());

        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.Endpoint, Is.Null);
        Assert.That(settings.ApiKey, Is.Null);
        Assert.That(settings.Team, Is.EqualTo("Smoke"));
        Assert.That(settings.Project, Is.EqualTo("Smoke"));
        Assert.That(settings.System, Is.EqualTo("QaaS"));
        Assert.That(settings.LaunchName, Is.EqualTo("QaaS Run | Smoke | QaaS | Session A, Session B"));
        Assert.That(settings.Description,
            Is.EqualTo(
                "QaaS captured this run directly from the runner pipeline: live sessions, real assertion outcomes, and the exact shape of QaaS at 2025-01-01 10:00:00. Sessions=[Session A, Session B]. LaunchAttributes=[No additional launch attributes.]"));
    }

    [Test]
    public void Constructor_WithProjectOverride_UsesProjectForPublishingButKeepsTeamContext()
    {
        var settings = CreateSettings(new ReportPortalConfig
        {
            Project = "ExplicitProject"
        });

        Assert.That(settings.Project, Is.EqualTo("ExplicitProject"));
        Assert.That(settings.Team, Is.EqualTo("Smoke"));
    }

    [Test]
    public void Constructor_WithMissingProject_FallsBackToMetadataTeam()
    {
        var settings = CreateSettings(new ReportPortalConfig
        {
            Project = " "
        });

        Assert.That(settings.Project, Is.EqualTo("Smoke"));
        Assert.That(settings.Team, Is.EqualTo("Smoke"));
    }

    [Test]
    public void TryGetEndpointUri_WithGatewayEndpoint_NormalizesEndpointToApiPath()
    {
        var settings = CreateSettings(new ReportPortalConfig
        {
            Endpoint = "http://localhost:8080",
            Project = "QaaS",
            ApiKey = "local-api-key"
        });

        var succeeded = settings.TryGetEndpointUri(out var endpointUri, out var failureReason);

        Assert.That(succeeded, Is.True);
        Assert.That(failureReason, Is.Null);
        Assert.That(endpointUri, Is.Not.Null);
        Assert.That(endpointUri!.AbsoluteUri, Is.EqualTo("http://localhost:8080/api/"));
    }

    [Test]
    public void Constructor_WithEndpointAndApiKeyOnlyInYaml_UsesYamlValues()
    {
        var settings = CreateSettings(new ReportPortalConfig
        {
            Enabled = true,
            Endpoint = "http://from-yaml.local",
            ApiKey = "yaml-api-key"
        });

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.EqualTo("http://from-yaml.local"));
        Assert.That(settings.ApiKey, Is.EqualTo("yaml-api-key"));
    }

    [Test]
    public void Constructor_WithRegisteredDefaults_UsesDefaultsButStillRoutesByResolvedProject()
    {
        ReportPortalConfig.RegisterDefaults(
            enabled: true,
            reportPortalUri: "http://localhost:8080",
            reportPortalApiKey: "default-api-key");

        var settings = CreateSettings(new ReportPortalConfig
        {
            Project = "ConfiguredProject"
        });

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:8080"));
        Assert.That(settings.Project, Is.EqualTo("ConfiguredProject"));
        Assert.That(settings.Team, Is.EqualTo("Smoke"));
        Assert.That(settings.ApiKey, Is.EqualTo("default-api-key"));
    }

    [Test]
    public void Constructor_WithYamlOverrides_UsesYamlValuesBeforeRegisteredDefaults()
    {
        ReportPortalConfig.RegisterDefaults(
            enabled: true,
            reportPortalUri: "http://default.local",
            reportPortalApiKey: "default-api-key");

        var settings = CreateSettings(new ReportPortalConfig
        {
            Endpoint = "http://from-yaml.local",
            ApiKey = "yaml-api-key"
        });

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Endpoint, Is.EqualTo("http://from-yaml.local"));
        Assert.That(settings.ApiKey, Is.EqualTo("yaml-api-key"));
    }

    [Test]
    public void Constructor_WithExplicitDisable_ReturnsDisabledSettingsWithoutNeedingMetadata()
    {
        ReportPortalConfig.RegisterDefaults(
            enabled: true,
            reportPortalUri: "http://default.local",
            reportPortalApiKey: "default-api-key");

        var settings = new ReportPortalSettings(new ReportPortalConfig
        {
            Enabled = false
        });

        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.Endpoint, Is.EqualTo("http://default.local"));
        Assert.That(settings.Team, Is.Null);
        Assert.That(settings.Project, Is.Null);
    }

    [Test]
    public void Constructor_WhenProjectCannotBeDerived_DoesNotThrowAndLeavesProjectNull()
    {
        var settings = new ReportPortalSettings(
            new ReportPortalConfig
            {
                Enabled = true,
                Endpoint = "http://localhost:8080"
            },
            null,
            "QaaS",
            ["Session A"],
            "run",
            StartedAt);

        Assert.That(settings.Team, Is.Null);
        Assert.That(settings.Project, Is.Null);
        Assert.That(settings.System, Is.EqualTo("QaaS"));
    }

    [Test]
    public void TryGetEndpointUri_WithMissingEndpoint_ReturnsFailureReason()
    {
        var settings = CreateSettings(new ReportPortalConfig
        {
            Enabled = true
        });

        var succeeded = settings.TryGetEndpointUri(out var endpointUri, out var failureReason);

        Assert.That(succeeded, Is.False);
        Assert.That(endpointUri, Is.Null);
        Assert.That(failureReason, Does.Contain("ReportPortal.Endpoint"));
    }

    [Test]
    public void BuildLaunchAttributes_IncludesTeamProjectSystemSessionsAndStaticAttributes()
    {
        var settings = CreateSettings(new ReportPortalConfig
        {
            Project = "ConfiguredProject",
            Attributes = new Dictionary<string, string>
            {
                ["Component"] = "Auth",
                ["Owner"] = "Smoke Team"
            }
        });

        var attributes = settings.BuildLaunchAttributes();

        Assert.That(attributes.Any(attribute => attribute.Key == "tool" && attribute.Value == "QaaS"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "team" && attribute.Value == "Smoke"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "project" && attribute.Value == "ConfiguredProject"),
            Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "system" && attribute.Value == "QaaS"), Is.True);
        Assert.That(attributes.Count(attribute => attribute.Key == "session"), Is.EqualTo(2));
        Assert.That(attributes.Any(attribute => attribute.Key == "Component" && attribute.Value == "Auth"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "Owner" && attribute.Value == "Smoke Team"), Is.True);
    }

    [Test]
    public void Constructor_WithManySessions_UsesCompactStableLaunchName()
    {
        var settings = new ReportPortalSettings(
            new ReportPortalConfig(),
            "Smoke",
            "QaaS",
            ["Session A", "Session B", "Session C"],
            "run",
            StartedAt);

        Assert.That(settings.LaunchName, Is.EqualTo("QaaS Run | Smoke | QaaS | Session A, Session B(+1)"));
    }

    [Test]
    public void Constructor_WithLaunchAttributes_AddsThemToDefaultDescription()
    {
        var settings = new ReportPortalSettings(
            new ReportPortalConfig(),
            "Smoke",
            "QaaS",
            ["Session A", "Session B"],
            "run",
            StartedAt,
            new Dictionary<string, string>
            {
                ["Component"] = "Gateway",
                ["Scenario"] = "Baseline"
            });

        Assert.That(settings.Description, Does.Contain("this run directly from the runner pipeline"));
        Assert.That(settings.Description, Does.Contain("Sessions=[Session A, Session B]"));
        Assert.That(settings.Description, Does.Contain("LaunchAttributes=[Component=Gateway, Scenario=Baseline]"));
    }

    private static ReportPortalSettings CreateSettings(ReportPortalConfig config) =>
        new(
            config,
            "Smoke",
            "QaaS",
            ["Session A", "Session B"],
            "run",
            StartedAt);
}
