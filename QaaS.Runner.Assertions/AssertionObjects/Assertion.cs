using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK.ConfigurationObjectFilters;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions;
using QaaS.Runner.Assertions.LinkBuilders;

namespace QaaS.Runner.Assertions.AssertionObjects;

public class Assertion
{
    public string Name { get; set; } = string.Empty;

    public string AssertionName { get; set; } = string.Empty;


    public IAssertion AssertionHook { get; set; } = default!;
    
    public IList<AssertionStatus> StatussesToReport { get; set; } = [];

    public IConfiguration AssertionConfiguration { get; set; } = new ConfigurationBuilder().Build();

    public List<BaseLink>? Links { get; set; }

    public bool? SaveSessionData { get; set; }

    public bool? SaveLogs { get; set; }

    public bool? SaveAttachments { get; set; }

    public bool? SaveTemplate { get; set; }

    public bool? DisplayTrace { get; set; }

    public AssertionSeverity? Severity { get; set; }

    /// <summary>
    /// Reporter implementation type that should receive this assertion result.
    /// </summary>
    public Type ReporterType { get; set; } = typeof(AllureReporter);

    /// <summary>
    ///     All Session data that might be relevant to the session according to its configuration
    /// </summary>
    public IImmutableList<SessionData> SessionDataList { get; set; } = null!;

    /// <summary>
    ///     All data that might be relevant to the session according to its configuration
    /// </summary>
    public IImmutableList<DataSource>? DataSourceList { get; set; }

    public string[]? _dataSourceNames { get; set; }
    public string[]? _dataSourcePatterns { get; set; }
    public string[]? _sessionNames { get; set; }
    public string[]? _sessionPatterns { get; set; }

    public virtual AssertionResult Execute(IImmutableList<SessionData?> sessionDataList,
        IImmutableList<DataSource>? dataSourceList)
    {
        var nonNullSessionDataList = sessionDataList.Where(sessionData => sessionData != null).Select(sd => sd!)
            .ToImmutableList();
        var availableDataSources = dataSourceList ?? ImmutableList<DataSource>.Empty;

        // Set assertion's dataSources and sessions based on provided names & patterns
        DataSourceList = EnumerableExtensions.GetFilteredConfigurationObjectList(
                availableDataSources,
                _dataSourcePatterns, RegexFilters.DataSource,
                "DataSource List")
            .Union(EnumerableExtensions.GetFilteredConfigurationObjectList(
                availableDataSources,
                _dataSourceNames, NameFilters.DataSource,
                "DataSource List")).ToImmutableList();
        SessionDataList = EnumerableExtensions.GetFilteredConfigurationObjectList(nonNullSessionDataList,
                _sessionPatterns!, RegexFilters.SessionData,
                "SessionData List")
            .Union(EnumerableExtensions.GetFilteredConfigurationObjectList(nonNullSessionDataList,
                _sessionNames!, NameFilters.SessionData,
                "SessionData List")).ToImmutableList();

        var sessionTimesList = SessionDataList
            .Select(s => new KeyValuePair<DateTime, DateTime>(s.UtcStartTime, s.UtcEndTime)).ToList();

        AssertionStatus assertionStatus;
        Exception? brokenAssertionStringException = null;
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        try
        {
            var assertionBooleanResult =
                AssertionHook.Assert(SessionDataList,
                    DataSourceList ?? ImmutableList<DataSource>.Empty);

            // Initialized AssertionStatus overrides Assertion return value
            assertionStatus = AssertionHook.AssertionStatus ??
                              (assertionBooleanResult ? AssertionStatus.Passed : AssertionStatus.Failed);
        }
        catch (Exception e)
        {
            brokenAssertionStringException = e;
            assertionStatus = AssertionStatus.Broken;
        }

        stopwatch.Stop();

        var testDuration = stopwatch.ElapsedMilliseconds + SessionDataList.Sum(s =>
            (long)(s.UtcEndTime - s.UtcStartTime).TotalMilliseconds);

        // if any failures, mark as flaky
        var flaky = SessionDataList.Any(sessionData => sessionData.SessionFailures.Count > 0);

        return new AssertionResult
        {
            Assertion = this,
            AssertionStatus = assertionStatus,
            BrokenAssertionException = brokenAssertionStringException,
            TestDurationMs = testDuration,
            Links = Links?.Select(link => link.GetLink(sessionTimesList)),
            Flaky = new Flaky
            {
                IsFlaky = flaky,
                FlakinessReasons = SessionDataList.Select(sessionData =>
                        new KeyValuePair<string, List<ActionFailure>>(sessionData.Name, sessionData.SessionFailures))
                    .ToList()
            }
        };
    }
}
