using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Infrastructure;
using ReportPortal.Client.Abstractions.Models;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

/// <summary>
/// Ephemeral ReportPortal launch plan rebuilt from enabled reporters for validation and final publishing.
/// </summary>
internal sealed class ReportPortalLaunchPlan
{
    internal const string UnknownSystem = "Unknown System";

    private ReportPortalLaunchPlan(
        string groupKey,
        string? endpoint,
        string? apiKey,
        string? project,
        string? team,
        string system,
        IReadOnlyList<string> sessionNames,
        string executionMode,
        string launchName,
        string description,
        bool debugMode,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<ReportPortalReporterResults> reporterResults)
    {
        GroupKey = groupKey;
        Endpoint = endpoint;
        ApiKey = apiKey;
        Project = project;
        Team = team;
        System = system;
        SessionNames = sessionNames;
        ExecutionMode = executionMode;
        LaunchName = launchName;
        Description = description;
        DebugMode = debugMode;
        Attributes = attributes;
        ReporterResults = reporterResults;
    }

    public string GroupKey { get; }
    public string? Endpoint { get; }
    public string? ApiKey { get; }
    public string? Project { get; }
    public string? Team { get; }
    public string System { get; }
    public IReadOnlyList<string> SessionNames { get; }
    public string ExecutionMode { get; }
    public string LaunchName { get; }
    public string Description { get; }
    public bool DebugMode { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
    public IReadOnlyList<ReportPortalReporterResults> ReporterResults { get; }
    public IEnumerable<AssertionResult> QueuedResults => ReporterResults.SelectMany(reporter => reporter.Results);

    public static IReadOnlyList<ReportPortalLaunchPlan> Build(
        IEnumerable<ReportPortalReporter> reporters,
        DateTimeOffset startedAtLocal,
        bool requireQueuedResults)
    {
        var reporterResults = reporters
            .Where(reporter => reporter.Config.Enabled == true)
            .Select(reporter => new ReportPortalReporterResults(
                reporter,
                requireQueuedResults ? reporter.GetQueuedResultsSnapshot() : []))
            .Where(reporter => !requireQueuedResults || reporter.Results.Count > 0)
            .ToList();

        return reporterResults
            .GroupBy(reporter => BuildGroupKey(reporter.Reporter), StringComparer.Ordinal)
            .Select(group => BuildGroupPlan(group.Key, group.ToList(), startedAtLocal))
            .ToList();
    }

    public bool TryGetEndpointUri(out Uri? endpointUri, out string? failureReason) =>
        TryNormalizeEndpoint(Endpoint, out endpointUri, out failureReason);

    public IList<ItemAttribute> BuildLaunchAttributes()
    {
        var attributes = new List<ItemAttribute>
        {
            Attr("tool", "QaaS"),
            Attr("source", "runner")
        };

        Add(attributes, "team", Team);
        Add(attributes, "project", Project);
        Add(attributes, "system", System);

        attributes.AddRange(SessionNames.Select(session => Attr("session", session)));
        attributes.AddRange(Attributes.Select(attribute => Attr(attribute.Key, attribute.Value)));

        return attributes;
    }

    internal static bool TryNormalizeEndpoint(string? endpoint, out Uri? endpointUri, out string? failureReason)
    {
        endpointUri = null;

        if (Clean(endpoint) is not { } cleanEndpoint)
            return Fail("ReportPortal.Endpoint must be configured when ReportPortal reporting is enabled.",
                out failureReason);

        if (!Uri.TryCreate(cleanEndpoint, UriKind.Absolute, out var rawUri))
            return Fail($"ReportPortal endpoint `{cleanEndpoint}` is not a valid absolute URI.", out failureReason);

        endpointUri = NormalizeEndpoint(rawUri);
        failureReason = null;
        return true;
    }

    private static ReportPortalLaunchPlan BuildGroupPlan(
        string groupKey,
        IReadOnlyList<ReportPortalReporterResults> reporterResults,
        DateTimeOffset startedAtLocal)
    {
        var firstReporter = reporterResults[0].Reporter;
        var firstConfig = firstReporter.Config;
        var metadataByReporter = reporterResults
            .Select(reporter => new
            {
                reporter.Reporter,
                Metadata = GetMetadata(reporter.Reporter.Context)
            })
            .ToList();

        var team = metadataByReporter
            .Select(item => Clean(item.Metadata.Team))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var project = Clean(firstConfig.Project) ?? team;
        var system = metadataByReporter
            .Select(item => Clean(item.Metadata.System))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? UnknownSystem;
        var sessionNames = reporterResults
            .SelectMany(reporter => reporter.Results)
            .SelectMany(result => result.Assertion.SessionDataList)
            .Select(session => Clean(session.Name))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sessionName => sessionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var executionModes = reporterResults
            .Select(reporter => Clean(reporter.Reporter.ExecutionMode) ?? "run")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(mode => mode, StringComparer.Ordinal)
            .ToList();
        var executionMode = executionModes.Count == 1 ? executionModes[0] : "mixed";
        var attributes = BuildAttributes(reporterResults, sessionNames, executionMode, firstConfig.Attributes);
        var temporaryPlan = new ReportPortalLaunchPlan(
            groupKey,
            Clean(firstConfig.Endpoint),
            Clean(firstConfig.ApiKey),
            project,
            team,
            system,
            sessionNames,
            executionMode,
            string.Empty,
            string.Empty,
            firstConfig.DebugMode == true,
            attributes,
            reporterResults);

        return new ReportPortalLaunchPlan(
            groupKey,
            temporaryPlan.Endpoint,
            temporaryPlan.ApiKey,
            temporaryPlan.Project,
            temporaryPlan.Team,
            temporaryPlan.System,
            temporaryPlan.SessionNames,
            temporaryPlan.ExecutionMode,
            Clean(firstConfig.LaunchName) ?? temporaryPlan.BuildDefaultLaunchName(),
            Clean(firstConfig.Description) ?? temporaryPlan.BuildDefaultDescription(startedAtLocal),
            temporaryPlan.DebugMode,
            temporaryPlan.Attributes,
            temporaryPlan.ReporterResults);
    }

    private static IReadOnlyDictionary<string, string> BuildAttributes(
        IReadOnlyList<ReportPortalReporterResults> reporterResults,
        IReadOnlyList<string> sessionNames,
        string executionMode,
        IReadOnlyDictionary<string, string>? configAttributes)
    {
        var attributes = reporterResults
            .SelectMany(reporter => GetMetadata(reporter.Reporter.Context) is { } metadata
                ? BaseReporter.ExtractMetadataAttributes(metadata)
                : Enumerable.Empty<KeyValuePair<string, string>>())
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key) &&
                                !string.IsNullOrWhiteSpace(attribute.Value))
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(", ", group.Select(attribute => attribute.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        attributes["executionMode"] = executionMode;
        attributes["builderCount"] = reporterResults.Count.ToString();
        attributes["sessionCount"] = sessionNames.Count.ToString();

        var caseNames = reporterResults
            .Select(reporter => Clean(reporter.Reporter.Context.CaseName))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(caseName => caseName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (caseNames.Length > 0)
            attributes["caseName"] = string.Join(", ", caseNames);

        var executionIds = reporterResults
            .Select(reporter => Clean(reporter.Reporter.Context.ExecutionId))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(executionId => executionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (executionIds.Length > 0)
            attributes["executionId"] = string.Join(", ", executionIds);

        AddCleanAttributes(attributes, configAttributes);
        return attributes;
    }

    private string BuildDefaultLaunchName()
    {
        var sessionSummary = BuildSessionSummary();
        return Team is null
            ? $"QaaS Run | {System} | {sessionSummary}"
            : $"QaaS Run | {Team} | {System} | {sessionSummary}";
    }

    private string BuildDefaultDescription(DateTimeOffset startedAtLocal)
    {
        var launchAttributeSummary = Attributes.Count == 0
            ? "No additional launch attributes."
            : string.Join(", ",
                Attributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(attribute => $"{attribute.Key}={attribute.Value}"));
        return
            $"QaaS captured this {ExecutionMode} directly from the runner pipeline: live sessions, real assertion outcomes, and the exact shape of {System} at {startedAtLocal:yyyy-MM-dd HH:mm:ss}. Sessions=[{string.Join(", ", SessionNames)}]. LaunchAttributes=[{launchAttributeSummary}]";
    }

    private string BuildSessionSummary() =>
        SessionNames.Count switch
        {
            0 => "No Sessions",
            <= 2 => string.Join(", ", SessionNames),
            _ => $"{SessionNames[0]}, {SessionNames[1]}(+{SessionNames.Count - 2})"
        };

    private static string BuildGroupKey(ReportPortalReporter reporter)
    {
        var metadata = GetMetadata(reporter.Context);
        var endpointKey = TryNormalizeEndpoint(reporter.Config.Endpoint, out var endpointUri, out _)
            ? endpointUri!.AbsoluteUri.ToLowerInvariant()
            : Clean(reporter.Config.Endpoint)?.ToLowerInvariant() ?? "<missing-endpoint>";
        var project = Clean(reporter.Config.Project) ?? Clean(metadata.Team) ?? "<missing-project>";
        var system = Clean(metadata.System) ?? UnknownSystem;

        return string.Join("::",
            endpointKey,
            project.ToLowerInvariant(),
            system.ToLowerInvariant());
    }

    private static MetaDataConfig GetMetadata(Context context)
    {
        return context is InternalContext internalContext
            ? internalContext.GetMetaDataOrDefault()
            : new MetaDataConfig();
    }

    private static Uri NormalizeEndpoint(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        builder.Path = builder.Path.TrimEnd('/').ToLowerInvariant() switch
        {
            "" or "/" or "/api" or "/api/v1" => "/api/",
            _ => $"{builder.Path.TrimEnd('/')}/"
        };

        return builder.Uri;
    }

    private static void AddCleanAttributes(IDictionary<string, string> destination,
        IEnumerable<KeyValuePair<string, string>>? source)
    {
        foreach (var (key, value) in source ?? [])
        {
            if (Clean(key) is { } cleanKey)
                destination[cleanKey] = Clean(value) ?? string.Empty;
        }
    }

    private static void Add(ICollection<ItemAttribute> attributes, string key, string? value)
    {
        if (value is not null)
            attributes.Add(Attr(key, value));
    }

    private static ItemAttribute Attr(string key, string value) =>
        new()
        {
            Key = key,
            Value = value
        };

    private static bool Fail(string reason, out string? failureReason)
    {
        failureReason = reason;
        return false;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record ReportPortalReporterResults(
    ReportPortalReporter Reporter,
    IReadOnlyList<AssertionResult> Results);
