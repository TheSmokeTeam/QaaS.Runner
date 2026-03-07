using System.ComponentModel;
using System.Text;
using ReportPortal.Client.Abstractions.Models;

namespace QaaS.Runner.Assertions.ConfigurationObjects;

/// <summary>
/// Optional ReportPortal publishing configuration for runner assertion results.
/// </summary>
public class ReportPortalConfig
{
    public const string EnabledEnvironmentVariable = "QAAS_REPORTPORTAL_ENABLED";
    public const string EndpointEnvironmentVariable = "QAAS_REPORTPORTAL_ENDPOINT";
    public const string ProjectEnvironmentVariable = "QAAS_REPORTPORTAL_PROJECT";
    public const string ApiKeyEnvironmentVariable = "QAAS_REPORTPORTAL_API_KEY";
    public const string LaunchNameEnvironmentVariable = "QAAS_REPORTPORTAL_LAUNCH_NAME";
    public const string DescriptionEnvironmentVariable = "QAAS_REPORTPORTAL_DESCRIPTION";
    public const string DebugModeEnvironmentVariable = "QAAS_REPORTPORTAL_DEBUG_MODE";

    [Description("Whether to publish runner assertion results to ReportPortal in addition to Allure.")]
    [DefaultValue(false)]
    public bool Enabled { get; set; }

    [Description("ReportPortal base URL. The runner accepts either the gateway URL or the API URL and normalizes it to /api/.")]
    public string? Endpoint { get; set; }

    [Description("The target ReportPortal project name.")]
    public string? Project { get; set; }

    [Description("The ReportPortal API key used to authenticate the run.")]
    public string? ApiKey { get; set; }

    [Description("Optional launch name override. Defaults to `QaaS Runner`.")]
    public string? LaunchName { get; set; }

    [Description("Optional launch description override.")]
    public string? Description { get; set; }

    [Description("Whether to create the launch in debug mode.")]
    [DefaultValue(false)]
    public bool DebugMode { get; set; }

    [Description("Static launch attributes to add to every launch.")]
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal static ReportPortalSettings Resolve(ReportPortalConfig? config)
    {
        var enabled = ReadBooleanValue(EnabledEnvironmentVariable) ?? config?.Enabled ?? false;
        var endpoint = ReadStringValue(EndpointEnvironmentVariable) ?? config?.Endpoint;
        var project = ReadStringValue(ProjectEnvironmentVariable) ?? config?.Project;
        var apiKey = ReadStringValue(ApiKeyEnvironmentVariable) ?? config?.ApiKey;
        var launchName = ReadStringValue(LaunchNameEnvironmentVariable) ?? config?.LaunchName;
        var description = ReadStringValue(DescriptionEnvironmentVariable) ?? config?.Description;
        var debugMode = ReadBooleanValue(DebugModeEnvironmentVariable) ?? config?.DebugMode ?? false;
        var attributes = config?.Attributes?.ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var settings = new ReportPortalSettings(
            enabled,
            NormalizeEndpoint(endpoint),
            project?.Trim(),
            apiKey?.Trim(),
            launchName?.Trim(),
            description?.Trim(),
            debugMode,
            attributes);

        settings.Validate();
        return settings;
    }

    private static string? ReadStringValue(string environmentVariableName)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariableName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool? ReadBooleanValue(string environmentVariableName)
    {
        var value = ReadStringValue(environmentVariableName);
        if (value is null)
            return null;

        return bool.TryParse(value, out var parsedValue)
            ? parsedValue
            : throw new InvalidOperationException(
                $"Environment variable {environmentVariableName} must be set to `true` or `false`.");
    }

    private static string? NormalizeEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return null;

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                $"ReportPortal endpoint `{endpoint}` is not a valid absolute URI.");

        var builder = new UriBuilder(uri);
        var absolutePath = builder.Path.TrimEnd('/');
        builder.Path = absolutePath.ToLowerInvariant() switch
        {
            "" or "/" => "/api/",
            "/api" => "/api/",
            "/api/v1" => "/api/",
            _ => builder.Path.EndsWith("/", StringComparison.Ordinal) ? builder.Path : $"{builder.Path}/"
        };

        return builder.Uri.AbsoluteUri;
    }
}

public sealed class ReportPortalSettings(
    bool enabled,
    string? endpoint,
    string? project,
    string? apiKey,
    string? launchName,
    string? description,
    bool debugMode,
    IReadOnlyDictionary<string, string> attributes)
{
    public bool Enabled { get; } = enabled;
    public string? Endpoint { get; } = endpoint;
    public string? Project { get; } = project;
    public string? ApiKey { get; } = apiKey;
    public string LaunchName { get; } = string.IsNullOrWhiteSpace(launchName) ? "QaaS Runner" : launchName;
    public string Description { get; } = string.IsNullOrWhiteSpace(description)
        ? "Generated by QaaS.Runner."
        : description;
    public bool DebugMode { get; } = debugMode;
    public IReadOnlyDictionary<string, string> Attributes { get; } = attributes;

    public void Validate()
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(Endpoint))
            throw new InvalidOperationException(
                $"ReportPortal reporting is enabled but neither `ReportPortal.Endpoint` nor {ReportPortalConfig.EndpointEnvironmentVariable} was provided.");

        if (string.IsNullOrWhiteSpace(Project))
            throw new InvalidOperationException(
                $"ReportPortal reporting is enabled but neither `ReportPortal.Project` nor {ReportPortalConfig.ProjectEnvironmentVariable} was provided.");

        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                $"ReportPortal reporting is enabled but neither `ReportPortal.ApiKey` nor {ReportPortalConfig.ApiKeyEnvironmentVariable} was provided.");
    }

    public IList<ItemAttribute> BuildLaunchAttributes()
    {
        var launchAttributes = new List<ItemAttribute>
        {
            new()
            {
                Key = "tool",
                Value = "QaaS"
            }
        };

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

    public bool IsCompatibleWith(ReportPortalSettings other)
    {
        return Enabled == other.Enabled &&
               string.Equals(Endpoint, other.Endpoint, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Project, other.Project, StringComparison.Ordinal) &&
               string.Equals(ApiKey, other.ApiKey, StringComparison.Ordinal) &&
               string.Equals(LaunchName, other.LaunchName, StringComparison.Ordinal) &&
               string.Equals(Description, other.Description, StringComparison.Ordinal) &&
               DebugMode == other.DebugMode &&
               BuildAttributesSignature() == other.BuildAttributesSignature();
    }

    private string BuildAttributesSignature()
    {
        var builder = new StringBuilder();
        foreach (var attribute in Attributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(attribute.Key);
            builder.Append('=');
            builder.Append(attribute.Value);
            builder.Append(';');
        }

        return builder.ToString();
    }
}
