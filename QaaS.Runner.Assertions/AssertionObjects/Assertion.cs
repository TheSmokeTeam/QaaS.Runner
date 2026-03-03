using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK.ConfigurationObjectFilters;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.LinkBuilders;

namespace QaaS.Runner.Assertions.AssertionObjects;

/// <summary>
/// Represents an assertion to be executed
/// </summary>
public class Assertion
{
    /// <summary>
    /// The display name of the assertion in test results
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The name of the assertion hook implementation
    /// </summary>
    public required string AssertionName { get; set; }

    /// <summary>
    /// The assertion hook implementation
    /// </summary>
    public required IAssertion AssertionHook { get; set; }
    
    /// <summary>
    /// List of assertion statuses to report
    /// </summary>
    public required IList<AssertionStatus> StatussesToReport { get; set; }

    /// <summary>
    /// Configuration for the assertion
    /// </summary>
    public IConfiguration AssertionConfiguration { get; set; } = new ConfigurationBuilder().Build();

    /// <summary>
    /// Links to attach to assertion results
    /// </summary>
    public List<BaseLink>? Links { get; set; }

    /// <summary>
    ///     All Session data that might be relevant to the session according to its configuration
    /// </summary>
    public IImmutableList<SessionData> SessionDataList { get; set; } = null!;

    /// <summary>
    ///     All data that might be relevant to the session according to its configuration
    /// </summary>
    public IImmutableList<DataSource>? DataSourceList { get; set; }

    /// <summary>
    /// Data source names filter
    /// </summary>
    public string[]? _dataSourceNames { get; set; }
    
    /// <summary>
    /// Data source patterns filter
    /// </summary>
    public string[]? _dataSourcePatterns { get; set; }
    
    /// <summary>
    /// Session names filter
    /// </summary>
    public string[]? _sessionNames { get; set; }
    
    /// <summary>
    /// Session patterns filter
    /// </summary>
    public string[]? _sessionPatterns { get; set; }

    public virtual AssertionResult Execute(IImmutableList<SessionData?> sessionDataList,
        IImmutableList<DataSource>? dataSourceList)
    {
        var nonNullSessionDataList = sessionDataList.Where(sessionData => sessionData != null).Select(sd => sd!)
            .ToImmutableList();

        // Set assertion's dataSources and sessions based on provided names & patterns
        DataSourceList = EnumerableExtensions.GetFilteredConfigurationObjectList(
                dataSourceList ?? new ImmutableArray<DataSource>().ToImmutableList(),
                _dataSourcePatterns, RegexFilters.DataSource,
                "DataSource List")
            .Union(EnumerableExtensions.GetFilteredConfigurationObjectList(
                dataSourceList ?? new ImmutableArray<DataSource>(),
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
                    DataSourceList ?? new ImmutableArray<DataSource>());

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