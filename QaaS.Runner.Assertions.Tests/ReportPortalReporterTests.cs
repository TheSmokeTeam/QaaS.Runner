using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Reporters.ReportPortal;
using QaaS.Runner.Assertions.Tests.Mocks;
using QaaS.Runner.Infrastructure;
using ReportPortal.Client.Abstractions.Models;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class ReportPortalReporterTests
{
    [Test]
    public void ReporterHelpers_WithDifferentAssertionConfigurations_UseEachAssertionOptions()
    {
        var reporter = CreateReporter();
        var sessionData = new SessionData
        {
            Name = "session-a",
            UtcStartTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            UtcEndTime = new DateTime(2025, 1, 1, 10, 0, 1, DateTimeKind.Utc)
        };
        var firstResult = CreateAssertionResult("first", AssertionSeverity.Critical, false, false, "first-trace",
            sessionData);
        var secondResult = CreateAssertionResult("second", AssertionSeverity.Minor, true, true, "second-trace",
            sessionData);

        var firstSessionArtifact = BuildSessionArtifact(reporter, sessionData, firstResult.Assertion);
        var secondSessionArtifact = BuildSessionArtifact(reporter, sessionData, secondResult.Assertion);
        var firstTextDetails = BuildAssertionTextDetails(reporter, firstResult);
        var secondTextDetails = BuildAssertionTextDetails(reporter, secondResult);
        var firstSeverity = GetSeverityAttribute(reporter, firstResult);
        var secondSeverity = GetSeverityAttribute(reporter, secondResult);

        Assert.Multiple(() =>
        {
            Assert.That(firstSessionArtifact, Is.Null);
            Assert.That(secondSessionArtifact, Is.Not.Null);
            Assert.That(firstTextDetails.Trace,
                Is.EqualTo("Assertion configured to not display assertion trace"));
            Assert.That(secondTextDetails.Trace, Is.EqualTo("second-trace"));
            Assert.That(firstSeverity, Is.EqualTo("critical"));
            Assert.That(secondSeverity, Is.EqualTo("minor"));
        });
    }

    [Test]
    public void BuildSessionLogArtifact_WhenSaveLogsEnabled_ReturnsStoredSessionLog()
    {
        var reporter = CreateReporter();
        reporter.SaveLogs = true;
        reporter.Context.AppendSessionLog("session-a", "Starting session-a");
        reporter.Context.AppendSessionLog("session-a", "Completed session-a");
        var sessionData = new SessionData
        {
            Name = "session-a"
        };
        var assertion = new Assertion
        {
            SaveLogs = false
        };

        var artifact = BuildSessionLogArtifact(reporter, sessionData, assertion);

        Assert.Multiple(() =>
        {
            Assert.That(artifact, Is.Not.Null);
            Assert.That(artifact!.Name, Is.EqualTo("session-a.log"));
            Assert.That(artifact.RelativePath, Is.EqualTo(Path.Combine("SessionLogs", "session-a.log")));
            Assert.That(artifact.ContentType, Is.EqualTo("text/plain"));
            Assert.That(Encoding.UTF8.GetString(artifact.Content), Does.Contain("Starting session-a"));
            Assert.That(Encoding.UTF8.GetString(artifact.Content), Does.Contain("Completed session-a"));
        });
    }

    [Test]
    public void BuildSessionLogArtifact_WhenSaveLogsDisabled_ReturnsNull()
    {
        var reporter = CreateReporter();
        reporter.SaveLogs = false;
        reporter.Context.AppendSessionLog("session-a", "Starting session-a");
        var sessionData = new SessionData
        {
            Name = "session-a"
        };
        var assertion = new Assertion
        {
            SaveLogs = true
        };

        var artifact = BuildSessionLogArtifact(reporter, sessionData, assertion);

        Assert.That(artifact, Is.Null);
    }

    [Test]
    public void BuildSessionLogArtifact_WhenAssertionSaveLogsEnabled_ReturnsStoredSessionLog()
    {
        var reporter = CreateReporter();
        reporter.Context.AppendSessionLog("session-a", "Starting session-a");
        var sessionData = new SessionData
        {
            Name = "session-a"
        };
        var assertion = new Assertion
        {
            SaveLogs = true
        };

        var artifact = BuildSessionLogArtifact(reporter, sessionData, assertion);

        Assert.That(artifact, Is.Not.Null);
    }

    [Test]
    public void WriteTestResults_QueuesResultsWithoutPublishing()
    {
        var reporter = CreateReporter();
        var result = CreateAssertionResult("queued", AssertionSeverity.Normal, false, true, "trace",
            new SessionData { Name = "session-a" });

        reporter.WriteTestResults(result);

        Assert.That(reporter.GetQueuedResultsSnapshot(), Is.EqualTo(new[] { result }));
    }

    private static ReportPortalReporter CreateReporter()
    {
        ReportPortalConfig.RegisterDefaults(enabled: false);
        return new ReportPortalReporter
        {
            Config = new ReportPortalConfig
            {
                Enabled = true,
                Endpoint = "https://reportportal.local/api/",
                ApiKey = "api-key"
            },
            Context = new Context
            {
                Logger = Globals.Logger,
                RootConfiguration = new ConfigurationBuilder().Build()
            },
            Severity = AssertionSeverity.Normal
        };
    }

    private static AssertionResult CreateAssertionResult(string name, AssertionSeverity severity,
        bool saveSessionData, bool displayTrace, string trace, SessionData sessionData)
    {
        return new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = name,
                AssertionName = $"{name}-type",
                SaveSessionData = saveSessionData,
                DisplayTrace = displayTrace,
                Severity = severity,
                SessionDataList = ImmutableList.Create(sessionData),
                AssertionHook = new AssertionHookMock
                {
                    AssertionMessage = $"{name}-message",
                    AssertionTrace = trace
                }
            },
            AssertionStatus = AssertionStatus.Passed,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };
    }

    private static ReportArtifact? BuildSessionArtifact(ReportPortalReporter reporter, SessionData sessionData,
        Assertion assertion)
    {
        var method = typeof(BaseReporter)
            .GetMethod("BuildSessionArtifact", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (ReportArtifact?)method.Invoke(reporter, [sessionData, assertion]);
    }

    private static ReportArtifact? BuildSessionLogArtifact(ReportPortalReporter reporter, SessionData sessionData,
        Assertion assertion)
    {
        var method = typeof(BaseReporter)
            .GetMethod("BuildSessionLogArtifact", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (ReportArtifact?)method.Invoke(reporter, [sessionData, assertion]);
    }

    private static AssertionTextDetails BuildAssertionTextDetails(ReportPortalReporter reporter,
        AssertionResult assertionResult)
    {
        var method = typeof(BaseReporter)
            .GetMethod("BuildAssertionTextDetails", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (AssertionTextDetails)method.Invoke(reporter, [assertionResult])!;
    }

    private static string? GetSeverityAttribute(ReportPortalReporter reporter, AssertionResult assertionResult)
    {
        var method = typeof(ReportPortalReporter)
            .GetMethod("BuildItemAttributes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        reporter.WriteTestResults(assertionResult);
        var launchPlan = ReportPortalLaunchPlan.Build(
            [reporter],
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
            requireQueuedResults: true).Single();
        var attributes = (IList<ItemAttribute>)method.Invoke(reporter, [assertionResult, launchPlan])!;
        return attributes.Single(attribute => attribute.Key == "severity").Value;
    }
}
