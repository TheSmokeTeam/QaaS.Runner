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
    private static readonly DateTimeOffset StartedAt = new(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FinishedAt = new(2025, 1, 1, 10, 5, 0, TimeSpan.Zero);

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
            out var failureReason
        );

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
        var reporter = CreateReporter(project: null);

        var launchPlan = BuildPlan(reporter);

        Assert.That(launchPlan.Project, Is.EqualTo("Smoke"));
        Assert.That(launchPlan.Team, Is.EqualTo("Smoke"));
    }

    [Test]
    public void Build_WithQueuedResults_GeneratesStableLaunchNameAndSummaryDescription()
    {
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        reporter.WriteTestResults(CreateResult("assertion-b", "Session B"));

        var launchPlan = BuildPlan(
            reporter,
            requireQueuedResults: true,
            finishedAtLocal: FinishedAt
        );

        Assert.That(
            launchPlan.LaunchName,
            Is.EqualTo("QaaS Run | Smoke | QaaS | Session A, Session B")
        );
        Assert.That(
            launchPlan.Description,
            Is.EqualTo(
                $"Start time: 2025-01-01 10:00:00 UTC | End time: 2025-01-01 10:05:00 UTC{Environment.NewLine}"
                    + "🟢 Passed 100% | 🔴 Failed 0% | 🟠 Broken 0% | 🔵 Unknown 0% | 🟡 Skipped 0%"
            )
        );
    }

    [Test]
    public void Build_WithMixedAssertionStatuses_CalculatesPercentageOfEveryStatusInLaunch()
    {
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("passed-a", "Session A", AssertionStatus.Passed));
        reporter.WriteTestResults(CreateResult("passed-b", "Session B", AssertionStatus.Passed));
        reporter.WriteTestResults(CreateResult("failed", "Session C", AssertionStatus.Failed));
        reporter.WriteTestResults(CreateResult("broken", "Session D", AssertionStatus.Broken));

        var launchPlan = BuildPlan(
            reporter,
            requireQueuedResults: true,
            finishedAtLocal: FinishedAt
        );
        var descriptionLines = launchPlan.Description.Split(Environment.NewLine);

        Assert.That(descriptionLines, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(
                descriptionLines[0],
                Is.EqualTo(
                    "Start time: 2025-01-01 10:00:00 UTC | End time: 2025-01-01 10:05:00 UTC"
                )
            );
            Assert.That(descriptionLines[1], Does.Contain("🟢 Passed 50%"));
            Assert.That(descriptionLines[1], Does.Contain("🔴 Failed 25%"));
            Assert.That(descriptionLines[1], Does.Contain("🟠 Broken 25%"));
            Assert.That(descriptionLines[1], Does.Contain("🔵 Unknown 0%"));
            Assert.That(descriptionLines[1], Does.Contain("🟡 Skipped 0%"));
            Assert.That(launchPlan.Description, Does.Not.Contain("Launch timing"));
        });
    }

    [Test]
    public void BuildLaunchAttributes_IncludesTeamProjectSystemSessionsConfigAttributesAndMetadataLabels()
    {
        var reporter = CreateReporter(
            project: "ConfiguredProject",
            attributes: new Dictionary<string, string>
            {
                ["Component"] = "Auth",
                ["Owner"] = "Smoke Team",
            },
            extraLabels: new Dictionary<string, string> { ["Area"] = "Checkout" }
        );
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        reporter.WriteTestResults(CreateResult("assertion-b", "Session B"));

        var attributes = BuildPlan(reporter, requireQueuedResults: true).BuildLaunchAttributes();

        Assert.That(
            attributes.Any(attribute => attribute.Key == "tool" && attribute.Value == "QaaS"),
            Is.True
        );
        Assert.That(
            attributes.Any(attribute => attribute.Key == "team" && attribute.Value == "Smoke"),
            Is.True
        );
        Assert.That(
            attributes.Any(attribute =>
                attribute.Key == "project" && attribute.Value == "ConfiguredProject"
            ),
            Is.True
        );
        Assert.That(
            attributes.Any(attribute => attribute.Key == "system" && attribute.Value == "QaaS"),
            Is.True
        );
        Assert.That(attributes.Count(attribute => attribute.Key == "session"), Is.EqualTo(2));
        Assert.That(
            attributes.Any(attribute => attribute.Key == "Component" && attribute.Value == "Auth"),
            Is.True
        );
        Assert.That(
            attributes.Any(attribute =>
                attribute.Key == "Owner" && attribute.Value == "Smoke Team"
            ),
            Is.True
        );
        Assert.That(
            attributes.Any(attribute => attribute.Key == "Area" && attribute.Value == "Checkout"),
            Is.True
        );
    }

    [Test]
    public void Build_WithManySessions_UsesCompactStableLaunchName()
    {
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        reporter.WriteTestResults(CreateResult("assertion-b", "Session B"));
        reporter.WriteTestResults(CreateResult("assertion-c", "Session C"));

        var launchPlan = BuildPlan(reporter, requireQueuedResults: true);

        Assert.That(
            launchPlan.LaunchName,
            Is.EqualTo("QaaS Run | Smoke | QaaS | Session A, Session B(+1)")
        );
    }

    [Test]
    public void Build_WithDifferentApiKeys_CreatesSeparateLaunchPlansAndPreservesApiKeys()
    {
        var plans = ReportPortalLaunchPlan.Build(
            [CreateReporter(apiKey: "api-key-a"), CreateReporter(apiKey: "api-key-b")],
            StartedAt,
            requireQueuedResults: false
        );

        Assert.Multiple(() =>
        {
            Assert.That(plans, Has.Count.EqualTo(2));
            Assert.That(
                plans.Select(plan => plan.ApiKey),
                Is.EquivalentTo(["api-key-a", "api-key-b"])
            );
        });
    }

    [Test]
    public void Build_WithDifferentLaunchNames_CreatesSeparateLaunchPlansAndPreservesLaunchNames()
    {
        var plans = ReportPortalLaunchPlan.Build(
            [CreateReporter(launchName: "Launch A"), CreateReporter(launchName: "Launch B")],
            StartedAt,
            requireQueuedResults: false
        );

        Assert.Multiple(() =>
        {
            Assert.That(plans, Has.Count.EqualTo(2));
            Assert.That(
                plans.Select(plan => plan.LaunchName),
                Is.EquivalentTo(["Launch A", "Launch B"])
            );
        });
    }

    [Test]
    public void Build_WithDifferentDescriptions_CreatesSeparateLaunchPlansAndPreservesDescriptions()
    {
        var plans = ReportPortalLaunchPlan.Build(
            [
                CreateReporter(description: "Description A"),
                CreateReporter(description: "Description B"),
            ],
            StartedAt,
            requireQueuedResults: false
        );

        Assert.Multiple(() =>
        {
            Assert.That(plans, Has.Count.EqualTo(2));
            Assert.That(
                plans.Select(plan => plan.Description),
                Is.EquivalentTo(["Description A", "Description B"])
            );
        });
    }

    [Test]
    public void Build_WithDifferentDebugModes_CreatesSeparateLaunchPlansAndPreservesDebugModes()
    {
        var plans = ReportPortalLaunchPlan.Build(
            [CreateReporter(debugMode: false), CreateReporter(debugMode: true)],
            StartedAt,
            requireQueuedResults: false
        );

        Assert.Multiple(() =>
        {
            Assert.That(plans, Has.Count.EqualTo(2));
            Assert.That(plans.Select(plan => plan.DebugMode), Is.EquivalentTo([false, true]));
        });
    }

    [Test]
    public void Build_WithDifferentStaticAttributes_CreatesSeparateLaunchPlansAndPreservesAttributes()
    {
        var plans = ReportPortalLaunchPlan.Build(
            [
                CreateReporter(attributes: new Dictionary<string, string> { ["Owner"] = "Team A" }),
                CreateReporter(attributes: new Dictionary<string, string> { ["Owner"] = "Team B" }),
            ],
            StartedAt,
            requireQueuedResults: false
        );

        Assert.Multiple(() =>
        {
            Assert.That(plans, Has.Count.EqualTo(2));
            Assert.That(
                plans.Select(plan => plan.Attributes["Owner"]),
                Is.EquivalentTo(["Team A", "Team B"])
            );
        });
    }

    private static ReportPortalLaunchPlan BuildPlan(
        ReportPortalReporter reporter,
        bool requireQueuedResults = false,
        DateTimeOffset? finishedAtLocal = null
    )
    {
        return ReportPortalLaunchPlan
            .Build([reporter], StartedAt, requireQueuedResults, finishedAtLocal)
            .Single();
    }

    private static ReportPortalReporter CreateReporter(
        string team = "Smoke",
        string system = "QaaS",
        string? project = null,
        string? endpoint = "http://localhost:8080",
        string? apiKey = "api-key",
        string? launchName = null,
        string? description = null,
        bool? debugMode = false,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyDictionary<string, string>? extraLabels = null
    )
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build(),
        };

        context.InsertValueIntoGlobalDictionary(
            context.GetMetaDataPath(),
            new MetaDataConfig
            {
                Team = team,
                System = system,
                ExtraLabels =
                    extraLabels?.ToDictionary(
                        label => label.Key,
                        label => (object)label.Value,
                        StringComparer.OrdinalIgnoreCase
                    ) ?? new Dictionary<string, object>(),
            }
        );

        return new ReportPortalReporter
        {
            Config = new ReportPortalConfig
            {
                Enabled = true,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Project = project,
                LaunchName = launchName,
                Description = description,
                DebugMode = debugMode,
                Attributes = attributes?.ToDictionary(
                    attribute => attribute.Key,
                    attribute => attribute.Value,
                    StringComparer.OrdinalIgnoreCase
                ),
            },
            Context = context,
            ExecutionMode = "run",
        };
    }

    private static AssertionResult CreateResult(
        string assertionName,
        string sessionName,
        AssertionStatus assertionStatus = AssertionStatus.Passed
    )
    {
        var sessionData = new SessionData
        {
            Name = sessionName,
            UtcStartTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            UtcEndTime = new DateTime(2025, 1, 1, 10, 0, 1, DateTimeKind.Utc),
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
                AssertionConfiguration = new ConfigurationBuilder().Build(),
            },
            AssertionStatus = assertionStatus,
            TestDurationMs = 1,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };
    }
}
