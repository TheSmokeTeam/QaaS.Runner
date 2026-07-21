using System.Globalization;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Infrastructure;
using ReportPortal.Client.Abstractions.Models;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

/// <summary>
/// Ephemeral ReportPortal launch plan rebuilt from enabled reporters for validation and final publishing.
/// </summary>
internal sealed class ReportPortalLaunchPlan
{
    private ReportPortalLaunchPlan(
        string groupKey,
        string? endpoint,
        string? apiKey,
        string project,
        string team,
        string system,
        IReadOnlyList<string> sessionNames,
        string launchName,
        string description,
        DateTime launchStartTimeUtc,
        DateTime launchEndTimeUtc,
        bool debugMode,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<ReportPortalReporterResults> reporterResults
    )
    {
        GroupKey = groupKey;
        Endpoint = endpoint;
        ApiKey = apiKey;
        Project = project;
        Team = team;
        System = system;
        SessionNames = sessionNames;
        LaunchName = launchName;
        Description = description;
        LaunchStartTimeUtc = launchStartTimeUtc;
        LaunchEndTimeUtc = launchEndTimeUtc;
        DebugMode = debugMode;
        Attributes = attributes;
        ReporterResults = reporterResults;
    }

    public string GroupKey { get; }
    public string? Endpoint { get; }
    public string? ApiKey { get; }
    public string Project { get; }
    public string Team { get; }
    public string System { get; }
    public string LaunchName { get; }
    public string Description { get; }
    public DateTime LaunchStartTimeUtc { get; }
    public DateTime LaunchEndTimeUtc { get; }
    public bool DebugMode { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
    public IReadOnlyList<ReportPortalReporterResults> ReporterResults { get; }
    private IReadOnlyList<string> SessionNames { get; }

    /// <summary>
    /// Builds one launch plan per normalized ReportPortal endpoint/project/system group.
    /// </summary>
    /// <param name="reporters">The ReportPortal reporters built for this runner invocation.</param>
    /// <param name="startedAtLocal">The fallback launch start time when no assertion results are queued.</param>
    /// <param name="requireQueuedResults">
    /// <see langword="true" /> when final publishing should skip reporters with no queued assertion results.
    /// </param>
    /// <param name="finishedAtLocal">
    /// The fallback launch end time when no assertion results are queued. Defaults to the current time.
    /// </param>
    /// <returns>The grouped launch plans to validate or publish.</returns>
    public static IReadOnlyList<ReportPortalLaunchPlan> Build(
        IEnumerable<ReportPortalReporter> reporters,
        DateTimeOffset startedAtLocal,
        bool requireQueuedResults,
        DateTimeOffset? finishedAtLocal = null
    )
    {
        var effectiveFinishedAtLocal = finishedAtLocal ?? DateTimeOffset.Now;
        if (effectiveFinishedAtLocal < startedAtLocal)
            effectiveFinishedAtLocal = startedAtLocal;

        var reporterResults = reporters
            .Select(reporter =>
            {
                var assertions = requireQueuedResults
                    ? reporter.GetQueuedResultsSnapshot()
                        .Select(result => BuildAssertionPlan(reporter, result))
                        .ToList()
                    : [];
                return new ReportPortalReporterResults(reporter, reporter.Config, assertions);
            })
            .Where(reporter => reporter.Config.Enabled == true)
            .Where(reporter => !requireQueuedResults || reporter.Assertions.Count > 0)
            .ToList();

        return reporterResults
            .GroupBy(BuildGroupKey, StringComparer.Ordinal)
            .Select(group =>
                BuildGroupPlan(group.Key, group.ToList(), startedAtLocal, effectiveFinishedAtLocal)
            )
            .ToList();
    }

    /// <summary>
    /// Resolves the configured endpoint for this launch plan into the ReportPortal API base URI.
    /// </summary>
    public bool TryGetEndpointUri(out Uri? endpointUri, out string? failureReason)
    {
        return TryNormalizeEndpoint(Endpoint, out endpointUri, out failureReason);
    }

    /// <summary>
    /// Builds ReportPortal launch-level attributes from runner metadata, grouped sessions, and configured attributes.
    /// </summary>
    /// <returns>The ReportPortal attributes sent with the launch start request.</returns>
    public IList<ItemAttribute> BuildLaunchAttributes()
    {
        var attributes = new List<ItemAttribute> { Attr("tool", "QaaS"), Attr("source", "runner") };

        attributes.Add(Attr("team", Team));
        attributes.Add(Attr("project", Project));
        attributes.Add(Attr("system", System));

        attributes.AddRange(SessionNames.Select(session => Attr("session", session)));
        attributes.AddRange(Attributes.Select(attribute => Attr(attribute.Key, attribute.Value)));

        return attributes;
    }

    /// <summary>
    /// Normalizes ReportPortal gateway/API endpoints to the API base URI used by validation and publishing.
    /// </summary>
    /// <param name="endpoint">The configured ReportPortal endpoint.</param>
    /// <param name="endpointUri">The normalized API endpoint URI when normalization succeeds.</param>
    /// <param name="failureReason">The validation failure when the endpoint is missing or invalid.</param>
    /// <returns><see langword="true" /> when the endpoint can be used for ReportPortal requests.</returns>
    internal static bool TryNormalizeEndpoint(
        string? endpoint,
        out Uri? endpointUri,
        out string? failureReason
    )
    {
        endpointUri = null;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            failureReason =
                "ReportPortal.Endpoint must be configured when ReportPortal reporting is enabled.";
            return false;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var rawUri))
        {
            failureReason = $"ReportPortal endpoint `{endpoint}` is not a valid absolute URI.";
            return false;
        }

        endpointUri = NormalizeEndpoint(rawUri);
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Builds the concrete launch plan for one already-grouped endpoint/project/system set of reporters.
    /// </summary>
    private static ReportPortalLaunchPlan BuildGroupPlan(
        string groupKey,
        IReadOnlyList<ReportPortalReporterResults> reporterResults,
        DateTimeOffset startedAtLocal,
        DateTimeOffset finishedAtLocal
    )
    {
        var firstConfig = reporterResults[0].Config;
        var metadataList = reporterResults
            .Select(reporter => GetMetadata(reporter.Reporter.Context))
            .ToList();

        var team = GetMetadataValue(
            metadataList,
            metadata => metadata.Team,
            nameof(MetaDataConfig.Team)
        );
        var project = string.IsNullOrWhiteSpace(firstConfig.Project) ? team : firstConfig.Project;
        var system = GetMetadataValue(
            metadataList,
            metadata => metadata.System,
            nameof(MetaDataConfig.System)
        );
        var sessionNames = reporterResults
            .SelectMany(reporter => reporter.Assertions)
            .SelectMany(assertion => assertion.Result.Assertion.SessionDataList)
            .Select(session => session.Name)
            .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
            .Select(sessionName => sessionName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sessionName => sessionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var executionModes = reporterResults
            .Select(reporter =>
                string.IsNullOrWhiteSpace(reporter.Reporter.ExecutionMode)
                    ? "run"
                    : reporter.Reporter.ExecutionMode
            )
            .Distinct(StringComparer.Ordinal)
            .OrderBy(mode => mode, StringComparer.Ordinal)
            .ToList();
        var executionMode = executionModes.Count == 1 ? executionModes[0] : "mixed";
        var attributes = BuildAttributes(
            reporterResults,
            sessionNames,
            executionMode,
            firstConfig.Attributes
        );
        var assertions = reporterResults
            .SelectMany(reporter => reporter.Assertions)
            .ToList();
        var launchStartTimeUtc = assertions.Count == 0
            ? startedAtLocal.UtcDateTime
            : assertions.Min(assertion => assertion.StartTimeUtc);
        var launchEndTimeUtc = assertions.Count == 0
            ? finishedAtLocal.UtcDateTime
            : assertions.Max(assertion => assertion.EndTimeUtc);

        return new ReportPortalLaunchPlan(
            groupKey,
            firstConfig.Endpoint,
            firstConfig.ApiKey,
            project,
            team,
            system,
            sessionNames,
            firstConfig.LaunchName ?? BuildDefaultLaunchName(team, system),
            firstConfig.Description
                ?? BuildDefaultDescription(launchStartTimeUtc, launchEndTimeUtc, reporterResults),
            launchStartTimeUtc,
            launchEndTimeUtc,
            firstConfig.DebugMode == true,
            attributes,
            reporterResults
        );
    }

    /// <summary>
    /// Builds de-duplicated launch metadata attributes from grouped reporter context and ReportPortal configuration.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildAttributes(
        IReadOnlyList<ReportPortalReporterResults> reporterResults,
        IReadOnlyList<string> sessionNames,
        string executionMode,
        IReadOnlyDictionary<string, string>? configAttributes
    )
    {
        var attributes = reporterResults
            .SelectMany(reporter =>
                BaseReporter.ExtractMetadataAttributes(GetMetadata(reporter.Reporter.Context))
            )
            .Where(attribute =>
                !string.IsNullOrWhiteSpace(attribute.Key)
                && !string.IsNullOrWhiteSpace(attribute.Value)
            )
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                    string.Join(
                        ", ",
                        group
                            .Select(attribute => attribute.Value)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    ),
                StringComparer.OrdinalIgnoreCase
            );

        attributes["executionMode"] = executionMode;
        attributes["builderCount"] = reporterResults.Count.ToString();
        attributes["sessionCount"] = sessionNames.Count.ToString();

        var caseNames = reporterResults
            .Select(reporter => reporter.Reporter.Context.CaseName)
            .Where(caseName => !string.IsNullOrWhiteSpace(caseName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(caseName => caseName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (caseNames.Length > 0)
            attributes["caseName"] = string.Join(", ", caseNames);

        var executionIds = reporterResults
            .Select(reporter => reporter.Reporter.Context.ExecutionId)
            .Where(executionId => !string.IsNullOrWhiteSpace(executionId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(executionId => executionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (executionIds.Length > 0)
            attributes["executionId"] = string.Join(", ", executionIds);

        AddConfiguredAttributes(attributes, configAttributes);
        return attributes;
    }

    /// <summary>
    /// Builds the fallback launch name when the ReportPortal configuration does not provide one.
    /// </summary>
    private static string BuildDefaultLaunchName(string team, string system) =>
        $"QaaS run | {team} | {system}";

    /// <summary>
    /// Builds the fallback launch description from launch timing and assertion-status percentages.
    /// </summary>
    private static string BuildDefaultDescription(
        DateTime launchStartTimeUtc,
        DateTime launchEndTimeUtc,
        IReadOnlyList<ReportPortalReporterResults> reporterResults
    )
    {
        var assertionStatuses = reporterResults
            .SelectMany(reporter => reporter.Assertions)
            .Select(assertion => assertion.Result.AssertionStatus)
            .ToList();
        var timingDescription =
            $"Start time: {FormatLaunchTime(launchStartTimeUtc)} | End time: {FormatLaunchTime(launchEndTimeUtc)}";
        var statusDescription = string.Join(
            " | ",
            Enum.GetValues<AssertionStatus>()
                .Select(status =>
                    $"{GetStatusColor(status)} {status} {FormatStatusPercentage(status, assertionStatuses)}%"
                )
        );

        return string.Join(Environment.NewLine, timingDescription, statusDescription);
    }

    private static string FormatLaunchTime(DateTime timestamp) =>
        timestamp
            .ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static ReportPortalAssertionPlan BuildAssertionPlan(
        ReportPortalReporter reporter,
        AssertionResult assertionResult
    )
    {
        var startTimeUtc = DateTimeOffset
            .FromUnixTimeMilliseconds(reporter.EpochTestSuiteStartTime)
            .UtcDateTime;
        var endTimeUtc = startTimeUtc.AddMilliseconds(assertionResult.TestDurationMs);

        return new ReportPortalAssertionPlan(assertionResult, startTimeUtc, endTimeUtc);
    }

    private static string FormatStatusPercentage(
        AssertionStatus status,
        IReadOnlyCollection<AssertionStatus> assertionStatuses
    )
    {
        var percentage =
            assertionStatuses.Count == 0
                ? 0
                : assertionStatuses.Count(resultStatus => resultStatus == status)
                    * 100d
                    / assertionStatuses.Count;
        return percentage.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string GetStatusColor(AssertionStatus status) =>
        status switch
        {
            AssertionStatus.Passed => "🟢",
            AssertionStatus.Failed => "🔴",
            AssertionStatus.Broken => "🟠",
            AssertionStatus.Unknown => "🔵",
            AssertionStatus.Skipped => "🟡",
            _ => "⚪",
        };

    /// <summary>
    /// Builds the stable grouping key that keeps compatible reporters in the same ReportPortal launch.
    /// </summary>
    private static string BuildGroupKey(ReportPortalReporterResults reporterResults)
    {
        var reporter = reporterResults.Reporter;
        var config = reporterResults.Config;
        var metadata = GetMetadata(reporter.Context);
        var endpointKey = TryNormalizeEndpoint(config.Endpoint, out var endpointUri, out _)
            ? endpointUri!.AbsoluteUri.ToLowerInvariant()
            : config.Endpoint?.ToLowerInvariant() ?? "<missing-endpoint>";
        var team = GetRequiredMetadataValue(metadata.Team, nameof(MetaDataConfig.Team));
        var project = string.IsNullOrWhiteSpace(config.Project) ? team : config.Project;
        var system = GetRequiredMetadataValue(metadata.System, nameof(MetaDataConfig.System));
        var attributesKey = config.Attributes is null
            ? "<null-attributes>"
            : string.Join(
                ";;",
                config
                    .Attributes.OrderBy(
                        attribute => attribute.Key,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ThenBy(attribute => attribute.Value, StringComparer.Ordinal)
                    .Select(attribute =>
                        $"{attribute.Key.ToLowerInvariant()}={attribute.Value ?? "<null>"}"
                    )
            );

        return string.Join(
            "::",
            endpointKey,
            project.ToLowerInvariant(),
            system.ToLowerInvariant(),
            config.ApiKey ?? "<missing-api-key>",
            config.LaunchName ?? "<default-launch-name>",
            config.Description ?? "<default-description>",
            (config.DebugMode == true).ToString(),
            attributesKey
        );
    }

    /// <summary>
    /// Reads runner metadata from the internal context when it is available.
    /// </summary>
    private static MetaDataConfig GetMetadata(Context context)
    {
        return context is InternalContext internalContext
            ? internalContext.GetMetaDataOrDefault()
            : throw new InvalidOperationException(
                "ReportPortal reporting requires an internal context with configured metadata."
            );
    }

    private static string GetMetadataValue(
        IEnumerable<MetaDataConfig> metadataList,
        Func<MetaDataConfig, string?> valueSelector,
        string propertyName
    )
    {
        var value = metadataList
            .Select(valueSelector)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return GetRequiredMetadataValue(value, propertyName);
    }

    private static string GetRequiredMetadataValue(string? value, string propertyName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException(
            $"ReportPortal reporting requires MetaData.{propertyName}."
        );
    }

    /// <summary>
    /// Converts a ReportPortal host or API URL into a canonical API base URI.
    /// </summary>
    private static Uri NormalizeEndpoint(Uri uri)
    {
        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };

        builder.Path = builder.Path.TrimEnd('/').ToLowerInvariant() switch
        {
            "" or "/" or "/api" or "/api/v1" => "/api/",
            _ => $"{builder.Path.TrimEnd('/')}/",
        };

        return builder.Uri;
    }

    /// <summary>
    /// Adds configured launch attributes without trimming; configuration values are expected to be normalized earlier.
    /// </summary>
    private static void AddConfiguredAttributes(
        IDictionary<string, string> destination,
        IEnumerable<KeyValuePair<string, string>>? source
    )
    {
        foreach (var (key, value) in source ?? [])
        {
            if (!string.IsNullOrWhiteSpace(key))
                destination[key] = value ?? string.Empty;
        }
    }

    private static ItemAttribute Attr(string key, string value) =>
        new() { Key = key, Value = value };
}

/// <summary>
/// Couples a passive ReportPortal reporter with the assertion results it queued for final publishing.
/// </summary>
internal sealed record ReportPortalReporterResults(
    ReportPortalReporter Reporter,
    ReportPortalConfig Config,
    IReadOnlyList<ReportPortalAssertionPlan> Assertions
);

/// <summary>
/// Couples an assertion result with its finalized ReportPortal timestamps.
/// </summary>
internal sealed record ReportPortalAssertionPlan(
    AssertionResult Result,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc
);
