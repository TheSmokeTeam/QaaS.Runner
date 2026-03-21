using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.LinkBuilders;

namespace QaaS.Runner.Assertions.Tests.AssertionObjectsTests;

[TestFixture]
public class AssertionExecutionTests
{
    private sealed class StaticLink(string linkName, string linkValue) : BaseLink(linkName)
    {
        protected override string BuildLink(IList<KeyValuePair<DateTime, DateTime>> startEndTimesKeyValuePairs)
        {
            return linkValue;
        }
    }

    private sealed class DelegateAssertionHook(
        Func<IImmutableList<SessionData>, IImmutableList<DataSource>, bool> execute,
        AssertionStatus? forcedStatus = null) : BaseAssertion<object>
    {
        public DelegateAssertionHook() : this((_, _) => true)
        {
        }

        public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
        {
            AssertionStatus = forcedStatus;
            return execute(sessionDataList, dataSourceList);
        }
    }

    [Test]
    public void Execute_WhenHookReturnsTrue_ReturnsPassedAndFilteredData()
    {
        var assertion = CreateAssertion(new DelegateAssertionHook((sessions, dataSources) =>
                sessions.Count == 2 && dataSources.Count == 0),
            sessionNames: ["session-1"],
            sessionPatterns: ["^session-2$"]);

        var sessionInput = new SessionData { Name = "session-1", UtcStartTime = DateTime.UtcNow, UtcEndTime = DateTime.UtcNow };
        var sessionRegex = new SessionData
            { Name = "session-2", UtcStartTime = DateTime.UtcNow, UtcEndTime = DateTime.UtcNow };
        var sessionIgnored = new SessionData
            { Name = "ignored-session", UtcStartTime = DateTime.UtcNow, UtcEndTime = DateTime.UtcNow };
        var sessionList = new List<SessionData?> { sessionInput, sessionRegex, sessionIgnored, null }.ToImmutableList();

        var result = assertion.Execute(sessionList, ImmutableList<DataSource>.Empty);

        Assert.That(result.AssertionStatus, Is.EqualTo(AssertionStatus.Passed));
        Assert.That(result.BrokenAssertionException, Is.Null);
        Assert.That(result.Assertion.DataSourceList, Is.Empty);
        Assert.That(result.Assertion.SessionDataList, Has.Count.EqualTo(2));
        Assert.That(result.TestDurationMs, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.Flaky.IsFlaky, Is.False);
    }

    [Test]
    public void Execute_WhenHookReturnsFalse_ReturnsFailed()
    {
        var assertion = CreateAssertion(new DelegateAssertionHook((_, _) => false));
        var sessionList = new List<SessionData?> { new() { Name = "session-1" } }.ToImmutableList();

        var result = assertion.Execute(sessionList, ImmutableList<DataSource>.Empty);

        Assert.That(result.AssertionStatus, Is.EqualTo(AssertionStatus.Failed));
    }

    [Test]
    public void Execute_WhenHookOverridesStatus_UsesOverriddenStatus()
    {
        var assertion = CreateAssertion(new DelegateAssertionHook((_, _) => false, AssertionStatus.Skipped));
        var sessionList = new List<SessionData?> { new() { Name = "session-1" } }.ToImmutableList();

        var result = assertion.Execute(sessionList, ImmutableList<DataSource>.Empty);

        Assert.That(result.AssertionStatus, Is.EqualTo(AssertionStatus.Skipped));
    }

    [Test]
    public void Execute_WhenHookThrows_ReturnsBrokenAndCapturesException()
    {
        var assertion = CreateAssertion(new DelegateAssertionHook((_, _) =>
            throw new InvalidOperationException("expected failure")));
        var sessionList = new List<SessionData?> { new() { Name = "session-1" } }.ToImmutableList();

        var result = assertion.Execute(sessionList, ImmutableList<DataSource>.Empty);

        Assert.That(result.AssertionStatus, Is.EqualTo(AssertionStatus.Broken));
        Assert.That(result.BrokenAssertionException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Execute_WhenSessionHasFailures_ReturnsFlaky()
    {
        var assertion = CreateAssertion(new DelegateAssertionHook((_, _) => true),
            sessionNames: ["session-1"]);
        var flakySession = new SessionData
        {
            Name = "session-1",
            SessionFailures = [new ActionFailure()]
        };
        var sessionList = new List<SessionData?> { flakySession }.ToImmutableList();

        var result = assertion.Execute(sessionList, ImmutableList<DataSource>.Empty);

        Assert.That(result.Flaky.IsFlaky, Is.True);
        Assert.That(result.Flaky.FlakinessReasons, Has.Count.EqualTo(1));
    }

    [Test]
    public void Execute_WhenDataSourcesAreNull_BuildsLinksAndUsesEmptyDataSourceList()
    {
        var assertion = CreateAssertion(new DelegateAssertionHook((_, dataSources) => dataSources.Count == 0),
            sessionNames: ["session-1"]);
        assertion.Links = [new StaticLink("grafana", "https://grafana.local/d/test")];
        var session = new SessionData
        {
            Name = "session-1",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow
        };

        var result = assertion.Execute(new List<SessionData?> { session }.ToImmutableList(), null);
        var links = result.Links;
        var link = links?.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.AssertionStatus, Is.EqualTo(AssertionStatus.Passed));
            Assert.That(result.Assertion.DataSourceList, Is.Empty);
            Assert.That(links, Is.Not.Null);
            Assert.That(link, Is.Not.Null);
            Assert.That(link!.Value.Key, Is.EqualTo("grafana"));
            Assert.That(link.Value.Value, Is.EqualTo("https://grafana.local/d/test"));
        });
    }

    private static Assertion CreateAssertion(IAssertion hook,
        string[]? sessionNames = null,
        string[]? sessionPatterns = null)
    {
        return new Assertion
        {
            Name = "assertion-name",
            AssertionName = "assertion-type",
            AssertionHook = hook,
            StatussesToReport = Enum.GetValues<AssertionStatus>().ToList(),
            _dataSourceNames = [],
            _dataSourcePatterns = [],
            _sessionNames = sessionNames ?? [],
            _sessionPatterns = sessionPatterns ?? []
        };
    }
}
