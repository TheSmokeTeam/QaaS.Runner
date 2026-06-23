using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using ReportPortal.Client.Abstractions.Models;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

/// <summary>
/// Immutable runtime settings used by the ReportPortal reporter, validator, and launch manager.
/// </summary>
/// <remarks>
/// This type resolves raw <see cref="ReportPortalConfig" /> values together with runner metadata such as team, system,
/// sessions, execution mode, and launch attributes. Keep raw configuration defaults in <see cref="ReportPortalConfig" />
/// and use this type only after the runner has enough context to publish.
/// </remarks>
public sealed class ReportPortalSettings
{
    private const string DefaultLaunchName = "QaaS Run";
    private const string DefaultDescription = "QaaS captured this run directly from the runner pipeline.";
    private const string UnknownSystem = "Unknown System";
    private readonly DateTimeOffset? _startedAtLocal;
    
    public bool Enabled { get; }
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

    /// <summary>
    /// Creates settings from raw ReportPortal configuration without runner launch context.
    /// </summary>
    /// <param name="config">The raw ReportPortal reporter configuration.</param>
    public ReportPortalSettings(ReportPortalConfig config)
        : this(config, null, null, [], "run", null)
    {
    }

    /// <summary>
    /// Creates settings from raw ReportPortal configuration and runner launch context.
    /// </summary>
    /// <param name="config">The raw ReportPortal reporter configuration.</param>
    /// <param name="team">The team metadata associated with the execution group.</param>
    /// <param name="system">The system metadata associated with the execution group.</param>
    /// <param name="sessionNames">The sessions represented by this launch group.</param>
    /// <param name="executionMode">The execution mode represented by this launch group.</param>
    /// <param name="startedAtLocal">The local timestamp used in generated launch descriptions.</param>
    /// <param name="launchAttributes">Additional runner-derived launch attributes.</param>
    public ReportPortalSettings(
        ReportPortalConfig config,
        string? team,
        string? system,
        IReadOnlyList<string> sessionNames,
        string executionMode,
        DateTimeOffset? startedAtLocal,
        IReadOnlyDictionary<string, string>? launchAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        Enabled = config.Enabled == true;
        Endpoint = Clean(config.Endpoint);
        ApiKey = Clean(config.ApiKey);
        Team = Clean(team);
        Project = Clean(config.Project) ?? Team;
        System = Clean(system) ?? UnknownSystem;
        SessionNames = CleanSessionNames(sessionNames);
        ExecutionMode = Clean(executionMode) ?? "run";
        _startedAtLocal = startedAtLocal;
        Attributes = MergeAttributes(launchAttributes, config.Attributes);
        LaunchName = Clean(config.LaunchName) ?? BuildDefaultLaunchName();
        Description = Clean(config.Description) ?? BuildDefaultDescription();
        DebugMode = config.DebugMode == true;
    }

    /// <summary>
    /// Normalizes and validates the configured ReportPortal endpoint.
    /// </summary>
    /// <param name="endpointUri">The normalized API endpoint when validation succeeds.</param>
    /// <param name="failureReason">The validation failure reason when validation fails.</param>
    /// <returns><see langword="true" /> when the endpoint can be used for ReportPortal API calls.</returns>
    public bool TryGetEndpointUri(out Uri? endpointUri, out string? failureReason)
    {
        endpointUri = null;

        if (Endpoint is null)
            return Fail("ReportPortal.Endpoint must be configured when ReportPortal reporting is enabled.", out failureReason);

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var rawUri))
            return Fail($"ReportPortal endpoint `{Endpoint}` is not a valid absolute URI.", out failureReason);

        endpointUri = NormalizeEndpoint(rawUri);
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Builds the ReportPortal launch attributes for the resolved launch group.
    /// </summary>
    /// <returns>The attributes to attach to the ReportPortal launch.</returns>
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

    /// <summary>
    /// Builds the key used by the launch manager to reuse launches within one runner invocation.
    /// </summary>
    /// <param name="resolvedProjectName">The project name returned by ReportPortal access validation.</param>
    /// <param name="endpointUri">The normalized ReportPortal API endpoint.</param>
    /// <returns>A stable endpoint/project/system launch grouping key.</returns>
    public string BuildLaunchGroupKey(string resolvedProjectName, Uri endpointUri) =>
        string.Join(".",
            endpointUri.AbsoluteUri.ToLowerInvariant(),
            resolvedProjectName.ToLowerInvariant(),
            System.ToLowerInvariant());

    private static Dictionary<string, string> MergeAttributes(
        IReadOnlyDictionary<string, string>? launchAttributes,
        IReadOnlyDictionary<string, string>? configAttributes)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddCleanAttributes(attributes, launchAttributes);
        AddCleanAttributes(attributes, configAttributes);
        return attributes;
    }

    private string BuildDefaultLaunchName()
    {
        if (_startedAtLocal is null)
            return DefaultLaunchName;

        var sessionSummary = BuildSessionSummary();
        return Team is null
            ? $"QaaS Run | {System} | {sessionSummary}"
            : $"QaaS Run | {Team} | {System} | {sessionSummary}";
    }

    private string BuildDefaultDescription()
    {
        if (_startedAtLocal is null)
            return DefaultDescription;

        var launchAttributeSummary = Attributes.Count == 0
            ? "No additional launch attributes."
            : string.Join(", ",
                Attributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(attribute => $"{attribute.Key}={attribute.Value}"));
        return
            $"QaaS captured this {ExecutionMode} directly from the runner pipeline: live sessions, real assertion outcomes, and the exact shape of {System} at {_startedAtLocal:yyyy-MM-dd HH:mm:ss}. Sessions=[{string.Join(", ", SessionNames)}]. LaunchAttributes=[{launchAttributeSummary}]";
    }

    private string BuildSessionSummary() =>
        SessionNames.Count switch
        {
            0 => "No Sessions",
            <= 2 => string.Join(", ", SessionNames),
            _ => $"{SessionNames[0]}, {SessionNames[1]}(+{SessionNames.Count - 2})"
        };

    private static IReadOnlyList<string> CleanSessionNames(IEnumerable<string?> sessionNames)
    {
        return sessionNames
            .Select(Clean)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sessionName => sessionName, StringComparer.Ordinal)
            .ToArray();
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
