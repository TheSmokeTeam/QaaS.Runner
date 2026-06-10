using System.ComponentModel;
using ReportPortal.Client.Abstractions.Models;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

namespace QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;

/// <summary>
/// Defines the passive ReportPortal settings used by QaaS to publish assertion results without provisioning or mutating
/// ReportPortal resources.
/// </summary>
public class ReportPortalConfig : IReporterConfig
{
    private sealed class StaticReportPortalDefaultsProvider(ReportPortalConfigurationDefaults defaults)
        : IReportPortalConfigurationDefaultsProvider
    {
        public ReportPortalConfigurationDefaults GetDefaults() => defaults;
    }

    private bool _enabled;
    private bool _enabledConfigured;
    private static IReportPortalConfigurationDefaultsProvider? _defaultsProvider;

    [Description("Whether to publish runner assertion results to ReportPortal in addition to Allure. Defaults to the registered QaaS.Configuration value.")]
    [DefaultValue(false)]
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _enabledConfigured = true;
        }
    }

    [Description("ReportPortal base URL. Accepts either the gateway URL or the API URL and normalizes it to /api/.")]
    public string? Endpoint { get; set; }

    [Description("Ignored at runtime. QaaS always routes ReportPortal results by MetaData.Team.")]
    public string? Project { get; set; }

    [Description("ReportPortal API key used for best-effort publishing when reporting is enabled.")]
    public string? ApiKey { get; set; }

    [Description("Optional launch name override. When omitted the runner derives the launch name from the grouped run descriptor.")]
    public string? LaunchName { get; set; }

    [Description("Optional launch description override. When omitted the runner derives the launch description from the grouped run descriptor.")]
    public string? Description { get; set; }

    [Description("Whether to create the launch in debug mode.")]
    [DefaultValue(false)]
    public bool DebugMode { get; set; }

    [Description("Static launch attributes to add to every launch in addition to the default QaaS team/system/session/source attributes.")]
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterDefaultsProvider(IReportPortalConfigurationDefaultsProvider defaultsProvider)
    {
        ArgumentNullException.ThrowIfNull(defaultsProvider);
        _defaultsProvider = defaultsProvider;
    }

    public static IReportPortalConfigurationDefaultsProvider? GetDefaultsProvider() => _defaultsProvider;

    public static void RegisterDefaults(
        bool enabled,
        string? reportPortalUri = null,
        string? reportPortalApiKey = null) =>
        RegisterDefaultsProvider(new StaticReportPortalDefaultsProvider(new ReportPortalConfigurationDefaults
        {
            Enabled = enabled,
            ReportPortalUri = reportPortalUri,
            ReportPortalApiKey = reportPortalApiKey
        }));

    /// <summary>
    /// Resolves the effective runtime settings from YAML and registered defaults. This method is intentionally lenient:
    /// missing endpoint, missing API key, or missing team metadata are handled later as warnings so QaaS can continue the
    /// run without affecting the exit code.
    /// </summary>
    internal ReportPortalSettings Resolve(ReportPortalLaunchDescriptor? runDescriptor)
    {
        var defaults = GetDefaultsProvider()?.GetDefaults() ?? ReportPortalConfigurationDefaults.Empty;
        var enabled = _enabledConfigured ? Enabled : defaults.Enabled;
        var endpoint = FirstNonWhiteSpace(Endpoint, defaults.ReportPortalUri);
        var apiKey = FirstNonWhiteSpace(ApiKey, defaults.ReportPortalApiKey);
        var ignoredProjectOverride = Project;
        var launchName = string.IsNullOrWhiteSpace(LaunchName)
            ? runDescriptor?.BuildDefaultLaunchName()
            : LaunchName.Trim();
        var description = string.IsNullOrWhiteSpace(Description)
            ? runDescriptor?.BuildDefaultDescription()
            : Description.Trim();
        var attributes = new Dictionary<string, string>(runDescriptor?.LaunchAttributes ??
                                                        new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in Attributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key)))
            attributes[attribute.Key] = attribute.Value;

        return new ReportPortalSettings(
            enabled,
            endpoint?.Trim(),
            apiKey?.Trim(),
            string.IsNullOrWhiteSpace(runDescriptor?.TeamName) ? null : runDescriptor.TeamName.Trim(),
            string.IsNullOrWhiteSpace(runDescriptor?.SystemName) ? "Unknown System" : runDescriptor.SystemName.Trim(),
            runDescriptor?.SessionNames ?? [],
            launchName,
            description,
            DebugMode,
            attributes,
            ignoredProjectOverride?.Trim());
    }

    private static string? FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public sealed record ReportPortalConfigurationDefaults
{
    public static readonly ReportPortalConfigurationDefaults Empty = new();

    public bool Enabled { get; init; }

    public string? ReportPortalUri { get; init; }

    public string? ReportPortalApiKey { get; init; }
}

public interface IReportPortalConfigurationDefaultsProvider
{
    ReportPortalConfigurationDefaults GetDefaults();
}

/// <summary>
/// Immutable runtime settings shared by all ReportPortal reporters in one QaaS invocation. The settings are designed for
/// passive publishing only; they never imply project creation or any other instance mutation.
/// </summary>
public sealed class ReportPortalSettings(
    bool enabled,
    string? endpoint,
    string? apiKey,
    string? team,
    string system,
    IReadOnlyList<string> sessionNames,
    string? launchName,
    string? description,
    bool debugMode,
    IReadOnlyDictionary<string, string> attributes,
    string? ignoredProjectOverride)
{
    public bool Enabled { get; } = enabled;
    public string? Endpoint { get; } = endpoint;
    public string? ApiKey { get; } = apiKey;
    public string? Team { get; } = team;
    public string System { get; } = string.IsNullOrWhiteSpace(system) ? "Unknown System" : system.Trim();
    public IReadOnlyList<string> SessionNames { get; } = sessionNames
        .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
        .Select(sessionName => sessionName.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(sessionName => sessionName, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    public string LaunchName { get; } = string.IsNullOrWhiteSpace(launchName) ? "QaaS Run" : launchName.Trim();
    public string Description { get; } = string.IsNullOrWhiteSpace(description)
        ? "QaaS captured this run directly from the runner pipeline."
        : description.Trim();
    public bool DebugMode { get; } = debugMode;
    public IReadOnlyDictionary<string, string> Attributes { get; } = attributes;
    public string? IgnoredProjectOverride { get; } = ignoredProjectOverride;

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
        return string.Join("::",
            endpointUri.AbsoluteUri.ToLowerInvariant(),
            resolvedProjectName.ToLowerInvariant(),
            System.ToLowerInvariant());
    }
}
