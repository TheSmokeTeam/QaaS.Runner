using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

namespace QaaS.Runner.Assertions.Tests.ConfigurationObjectsTests;

[TestFixture]
public class ReportPortalLaunchPlanTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp()
    {
        ReportPortalConfig.RegisterDefaults(enabled: false);
    }

    [Test]
    public void TryNormalizeEndpoint_WithGatewayEndpoint_NormalizesEndpointToApiPath()
    {
        var succeeded = ReportPortalLaunchPlan.TryNormalizeEndpoint(
            "http://localhost:8080",
            out var endpointUri,
            out var failureReason);

        Assert.That(succeeded, Is.True);
        Assert.That(failureReason, Is.Null);
        Assert.That(endpointUri!.AbsoluteUri, Is.EqualTo("http://localhost:8080/api/"));
    }

    [Test]
    public void Build_WithProjectOverride_UsesProjectForPublishingButKeepsTeamContext()
    {
        var reporter = CreateReporter(project: "ExplicitProject");

        var launchPlan = BuildPlan(reporter);

        Assert.That(launchPlan.Project, Is.EqualTo("ExplicitProject"));
        Assert.That(launchPlan.Team, Is.EqualTo("Smoke"));
    }

    [Test]
    public void Build_WithMissingProject_FallsBackToMetadataTeam()
    {
        var reporter = CreateReporter(project: " ");

        var launchPlan = BuildPlan(reporter);

        Assert.That(launchPlan.Project, Is.EqualTo("Smoke"));
        Assert.That(launchPlan.Team, Is.EqualTo("Smoke"));
    }

    [Test]
    public void Build_WhenProjectCannotBeDerived_DoesNotThrowAndLeavesProjectNull()
    {
        var reporter = CreateReporter(team: null, system: null);

        var launchPlan = BuildPlan(reporter);

        Assert.That(launchPlan.Team, Is.Null);
        Assert.That(launchPlan.Project, Is.Null);
        Assert.That(launchPlan.System, Is.EqualTo(ReportPortalLaunchPlan.UnknownSystem));
    }

    [Test]
    public void Build_WithQueuedResults_GeneratesStableLaunchNameAndDescription()
    {
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        reporter.WriteTestResults(CreateResult("assertion-b", "Session B"));

        var launchPlan = BuildPlan(reporter, requireQueuedResults: true);

        Assert.That(launchPlan.LaunchName, Is.EqualTo("QaaS Run | Smoke | QaaS | Session A, Session B"));
        Assert.That(launchPlan.Description, Does.Contain("this run directly from the runner pipeline"));
        Assert.That(launchPlan.Description, Does.Contain("Sessions=[Session A, Session B]"));
    }

    [Test]
    public void BuildLaunchAttributes_IncludesTeamProjectSystemSessionsConfigAttributesAndMetadataLabels()
    {
        var reporter = CreateReporter(
            project: "ConfiguredProject",
            attributes: new Dictionary<string, string>
            {
                ["Component"] = "Auth",
                ["Owner"] = "Smoke Team"
            },
            extraLabels: new Dictionary<string, string>
            {
                ["Area"] = "Checkout"
            });
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        reporter.WriteTestResults(CreateResult("assertion-b", "Session B"));

        var attributes = BuildPlan(reporter, requireQueuedResults: true).BuildLaunchAttributes();

        Assert.That(attributes.Any(attribute => attribute.Key == "tool" && attribute.Value == "QaaS"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "team" && attribute.Value == "Smoke"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "project" && attribute.Value == "ConfiguredProject"),
            Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "system" && attribute.Value == "QaaS"), Is.True);
        Assert.That(attributes.Count(attribute => attribute.Key == "session"), Is.EqualTo(2));
        Assert.That(attributes.Any(attribute => attribute.Key == "Component" && attribute.Value == "Auth"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "Owner" && attribute.Value == "Smoke Team"), Is.True);
        Assert.That(attributes.Any(attribute => attribute.Key == "Area" && attribute.Value == "Checkout"), Is.True);
    }

    [Test]
    public void Build_WithManySessions_UsesCompactStableLaunchName()
    {
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        reporter.WriteTestResults(CreateResult("assertion-b", "Session B"));
        reporter.WriteTestResults(CreateResult("assertion-c", "Session C"));

        var launchPlan = BuildPlan(reporter, requireQueuedResults: true);

        Assert.That(launchPlan.LaunchName, Is.EqualTo("QaaS Run | Smoke | QaaS | Session A, Session B(+1)"));
    }

    private static ReportPortalLaunchPlan BuildPlan(ReportPortalReporter reporter, bool requireQueuedResults = false)
    {
        return ReportPortalLaunchPlan.Build([reporter], StartedAt, requireQueuedResults).Single();
    }

    private static ReportPortalReporter CreateReporter(
        string? team = "Smoke",
        string? system = "QaaS",
        string? project = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyDictionary<string, string>? extraLabels = null)
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build()
        };

        if (team is not null || system is not null || extraLabels is not null)
        {
            context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig
            {
                Team = team,
                System = system,
                ExtraLabels = extraLabels?.ToDictionary(
                    label => label.Key,
                    label => (object)label.Value,
                    StringComparer.OrdinalIgnoreCase)
                              ?? new Dictionary<string, object>()
            });
        }

        return new ReportPortalReporter
        {
            Config = new ReportPortalConfig
            {
                Enabled = true,
                Endpoint = "http://localhost:8080",
                ApiKey = "api-key",
                Project = project,
                Attributes = attributes?.ToDictionary(
                    attribute => attribute.Key,
                    attribute => attribute.Value,
                    StringComparer.OrdinalIgnoreCase)
            },
            Context = context,
            ExecutionMode = "run"
        };
    }

    private static AssertionResult CreateResult(string assertionName, string sessionName)
    {
        var sessionData = new SessionData
        {
            Name = sessionName,
            UtcStartTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            UtcEndTime = new DateTime(2025, 1, 1, 10, 0, 1, DateTimeKind.Utc)
        };

        return new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = assertionName,
                AssertionName = assertionName,
                SessionDataList = ImmutableList.Create(sessionData),
                DisplayTrace = true,
                AssertionHook = null,
                AssertionConfiguration = new ConfigurationBuilder().Build()
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 1,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };
    }
}
