using System;
using System.Collections.Generic;
using NUnit.Framework;
using QaaS.Runner.Assertions;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class ReportPortalRunDescriptorTests
{
    [Test]
    public void BuildDefaultLaunchName_UsesStableTeamSystemAndSessionIdentity()
    {
        var descriptor = new ReportPortalRunDescriptor(
            "Smoke",
            "QaaS",
            ["Session A", "Session B"],
            "run",
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero));

        var launchName = descriptor.BuildDefaultLaunchName();

        Assert.That(launchName, Is.EqualTo("QaaS Run | Smoke | QaaS | Session A, Session B"));
    }

    [Test]
    public void BuildDefaultLaunchName_FallsBackToSystemAndSessionsWhenTeamIsMissing()
    {
        var descriptor = new ReportPortalRunDescriptor(
            null,
            "Crawler",
            ["Session A"],
            "run",
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero));

        var launchName = descriptor.BuildDefaultLaunchName();

        Assert.That(launchName, Is.EqualTo("QaaS Run | Crawler | Session A"));
    }

    [Test]
    public void BuildDefaultDescription_IncludesExecutionModeSessionsAndLaunchAttributes()
    {
        var descriptor = new ReportPortalRunDescriptor(
            "Smoke",
            "QaaS",
            ["Session A", "Session B"],
            "run",
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>
            {
                ["Component"] = "Gateway",
                ["Scenario"] = "Baseline"
            });

        var description = descriptor.BuildDefaultDescription();

        Assert.That(description, Does.Contain("this run directly from the runner pipeline"));
        Assert.That(description, Does.Contain("Sessions=[Session A, Session B]"));
        Assert.That(description, Does.Contain("LaunchAttributes=[Component=Gateway, Scenario=Baseline]"));
    }
}
