using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Reporters.ReportPortal;
using QaaS.Runner.Assertions.Tests.Mocks;
using ReportPortal.Client.Abstractions.Models;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class ReportPortalReporterTests
{
    [Test]
    public void ReporterHelpers_WithDifferentAssertionConfigurations_UseEachAssertionOptions()
    {
        using var launchManager = new ReportPortalLaunchManager();
        var reporter = CreateReporter(launchManager);
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

    private static ReportPortalReporter CreateReporter(ReportPortalLaunchManager launchManager)
    {
        return new ReportPortalReporter
        {
            Settings = CreateReportPortalSettings(),
            LaunchManager = launchManager,
            Context = new Context
            {
                Logger = Globals.Logger,
                RootConfiguration = new ConfigurationBuilder().Build()
            },
            SaveSessionData = true,
            SaveLogs = true,
            SaveAttachments = true,
            SaveTemplate = true,
            DisplayTrace = true,
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
        var attributes = (IList<ItemAttribute>)method.Invoke(reporter, [assertionResult])!;
        return attributes.Single(attribute => attribute.Key == "severity").Value;
    }

    private static ReportPortalSettings CreateReportPortalSettings()
    {
        return new ReportPortalSettings(
            true,
            "https://reportportal.local/api/",
            "api-key",
            "Smoke",
            "QaaS",
            [],
            null,
            null,
            false,
            new Dictionary<string, string>(),
            null);
    }
}
