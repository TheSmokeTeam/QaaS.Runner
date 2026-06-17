using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using ReportPortal.Client.Abstractions.Models;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

/// <summary>
/// Immutable runtime settings shared by all ReportPortal reporters in one QaaS invocation. The settings are designed for
/// passive publishing only; they never imply project creation or any other instance mutation.
/// </summary>
public sealed class ReportPortalSettings
{
    public ReportPortalSettings(ReportPortalLaunchDescriptor? descriptor, ReportPortalConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var defaults = ReportPortalConfig.GetDefaultsProvider()?.GetDefaults() ?? ReportPortalConfigurationDefaults.Empty;
        var endpoint = FirstNonWhiteSpace(config.Endpoint, defaults.ReportPortalUri);
        var apiKey = FirstNonWhiteSpace(config.ApiKey, defaults.ReportPortalApiKey);
        var launchName = string.IsNullOrWhiteSpace(config.LaunchName)
            ? descriptor?.BuildDefaultLaunchName()
            : config.LaunchName.Trim();
        var description = string.IsNullOrWhiteSpace(config.Description)
            ? descriptor?.BuildDefaultDescription()
            : config.Description.Trim();
        var attributes = new Dictionary<string, string>(
            descriptor?.LaunchAttributes ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in config.Attributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key)))
            attributes[attribute.Key] = attribute.Value;

        Enabled = config.EnabledConfigured ? config.Enabled : defaults.Enabled;
        Endpoint = endpoint?.Trim();
        ApiKey = apiKey?.Trim();
        Team = string.IsNullOrWhiteSpace(descriptor?.TeamName) ? null : descriptor.TeamName.Trim();
        System = string.IsNullOrWhiteSpace(descriptor?.SystemName) ? "Unknown System" : descriptor.SystemName.Trim();
        SessionNames = (descriptor?.SessionNames ?? [])
            .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
            .Select(sessionName => sessionName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sessionName => sessionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        LaunchName = string.IsNullOrWhiteSpace(launchName) ? "QaaS Run" : launchName.Trim();
        Description = string.IsNullOrWhiteSpace(description)
            ? "QaaS captured this run directly from the runner pipeline."
            : description.Trim();
        DebugMode = config.DebugMode;
        Attributes = attributes;
        IgnoredProjectOverride = config.Project?.Trim();
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
    public string? IgnoredProjectOverride { get; }

    /// <summary>
    /// The project name requested by runtime routing. QaaS derives this from <see cref="Team" /> and then validates it
    /// against the actual accessible projects returned by ReportPortal.
    /// </summary>
    public string? RequestedProjectName => string.IsNullOrWhiteSpace(Team) ? null : Team.Trim();

    /// <summary>
    /// Parses and normalizes the configured endpoint into a ReportPortal API URI.
    /// </summary>
    public bool TryGetEndpointUri(out Uri? endpointUri, out string? failureReason)
    {
        endpointUri = null;
        failureReason = null;

        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            failureReason = "ReportPortal.Endpoint must be configured when ReportPortal reporting is enabled.";
            return false;
        }

        if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var rawUri))
        {
            failureReason = $"ReportPortal endpoint `{Endpoint}` is not a valid absolute URI.";
            return false;
        }

        var builder = new UriBuilder(rawUri);
        var absolutePath = builder.Path.TrimEnd('/');
        builder.Path = absolutePath.ToLowerInvariant() switch
        {
            "" or "/" => "/api/",
            "/api" => "/api/",
            "/api/v1" => "/api/",
            _ => builder.Path.EndsWith("/", StringComparison.Ordinal) ? builder.Path : $"{builder.Path}/"
        };
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;

        endpointUri = builder.Uri;
        return true;
    }

    /// <summary>
    /// Builds the launch-level attributes attached when QaaS opens a ReportPortal launch.
    /// </summary>
    public IList<ItemAttribute> BuildLaunchAttributes()
    {
        var launchAttributes = new List<ItemAttribute>
        {
            new() { Key = "tool", Value = "QaaS" },
            new() { Key = "source", Value = "runner" }
        };

        if (!string.IsNullOrWhiteSpace(Team))
        {
            launchAttributes.Add(new ItemAttribute
            {
                Key = "team",
                Value = Team
            });
        }

        if (!string.IsNullOrWhiteSpace(System))
        {
            launchAttributes.Add(new ItemAttribute
            {
                Key = "system",
                Value = System
            });
        }

        foreach (var sessionName in SessionNames)
        {
            launchAttributes.Add(new ItemAttribute
            {
                Key = "session",
                Value = sessionName
            });
        }

        foreach (var attribute in Attributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key)))
        {
            launchAttributes.Add(new ItemAttribute
            {
                Key = attribute.Key.Trim(),
                Value = attribute.Value?.Trim() ?? string.Empty
            });
        }

        return launchAttributes;
    }

    /// <summary>
    /// Builds the launch grouping key used by the launch manager to share a single launch per team project and system.
    /// </summary>
    public string BuildLaunchGroupKey(string resolvedProjectName, Uri endpointUri)
    {
        return string.Join(".",
            endpointUri.AbsoluteUri.ToLowerInvariant(),
            resolvedProjectName.ToLowerInvariant(),
            System.ToLowerInvariant());
    }

    private static string? FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
