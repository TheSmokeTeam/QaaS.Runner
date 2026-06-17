using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using ReportPortal.Client.Abstractions.Models;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

public sealed class ReportPortalSettings
{
    private const string DefaultLaunchName = "QaaS Run";
    private const string DefaultDescription = "QaaS captured this run directly from the runner pipeline.";
    private const string UnknownSystem = "Unknown System";

    public ReportPortalSettings(ReportPortalLaunchDescriptor? descriptor, ReportPortalConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Enabled = config.Enabled == true;
        Endpoint = Clean(config.Endpoint);
        ApiKey = Clean(config.ApiKey);
        Team = descriptor?.TeamName;
        System = descriptor?.SystemName ?? UnknownSystem;
        SessionNames = descriptor?.SessionNames ?? [];
        LaunchName = Clean(config.LaunchName) ?? descriptor?.BuildDefaultLaunchName() ?? DefaultLaunchName;
        Description = Clean(config.Description) ?? descriptor?.BuildDefaultDescription() ?? DefaultDescription;
        DebugMode = config.DebugMode == true;
        Attributes = MergeAttributes(descriptor?.LaunchAttributes, config.Attributes);
    }

    public bool Enabled { get; }
    public string? Endpoint { get; }
    public string? ApiKey { get; }
    public string? Team { get; }
    public string System { get; }
    public IReadOnlyList<string> SessionNames { get; }
    public string LaunchName { get; }
    public string Description { get; }
    public bool DebugMode { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }

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

    public IList<ItemAttribute> BuildLaunchAttributes()
    {
        var attributes = new List<ItemAttribute>
        {
            Attr("tool", "QaaS"),
            Attr("source", "runner")
        };

        Add(attributes, "team", Team);
        Add(attributes, "system", System);

        attributes.AddRange(SessionNames.Select(session => Attr("session", session)));
        attributes.AddRange(Attributes.Select(attribute => Attr(attribute.Key, attribute.Value)));

        return attributes;
    }

    public string BuildLaunchGroupKey(string resolvedProjectName, Uri endpointUri) =>
        string.Join(".",
            endpointUri.AbsoluteUri.ToLowerInvariant(),
            resolvedProjectName.ToLowerInvariant(),
            System.ToLowerInvariant());

    private static IReadOnlyDictionary<string, string> MergeAttributes(
        IReadOnlyDictionary<string, string>? descriptorAttributes,
        IReadOnlyDictionary<string, string>? configAttributes)
    {
        var attributes = new Dictionary<string, string>(
            descriptorAttributes ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in configAttributes ?? new Dictionary<string, string>())
        {
            if (Clean(key) is { } cleanKey)
                attributes[cleanKey] = Clean(value) ?? string.Empty;
        }

        return attributes;
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
