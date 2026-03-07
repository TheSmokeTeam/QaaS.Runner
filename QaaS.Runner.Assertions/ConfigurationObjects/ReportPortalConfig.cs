using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ReportPortal.Client.Abstractions.Models;

namespace QaaS.Runner.Assertions.ConfigurationObjects;

/// <summary>
/// Defines ReportPortal settings for publishing runner-produced assertion results alongside Allure.
/// Values can come from YAML, environment variables, or defaults derived from runner metadata.
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
    public const string BootstrapUsernameEnvironmentVariable = "QAAS_REPORTPORTAL_BOOTSTRAP_USERNAME";
    public const string BootstrapPasswordEnvironmentVariable = "QAAS_REPORTPORTAL_BOOTSTRAP_PASSWORD";
    public const string BootstrapClientIdEnvironmentVariable = "QAAS_REPORTPORTAL_BOOTSTRAP_CLIENT_ID";
    public const string BootstrapClientSecretEnvironmentVariable = "QAAS_REPORTPORTAL_BOOTSTRAP_CLIENT_SECRET";
    public const string BotPasswordSeedEnvironmentVariable = "QAAS_REPORTPORTAL_BOT_PASSWORD_SEED";

    [Description("Whether to publish runner assertion results to ReportPortal in addition to Allure. Defaults to true.")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [Description("ReportPortal base URL. Accepts either the gateway URL or the API URL and normalizes it to /api/. Defaults to http://localhost:8080.")]
    public string? Endpoint { get; set; }

    [Description("Optional target ReportPortal project override. When omitted the runner derives the project name from MetaData.Team.")]
    public string? Project { get; set; }

    [Description("Optional ReportPortal API key override. When omitted QaaS auto-provisions a managed project bot user and API key.")]
    public string? ApiKey { get; set; }

    [Description("Optional launch name override. When omitted the runner builds a run-scoped launch name from the system, sessions, and local time.")]
    public string? LaunchName { get; set; }

    [Description("Optional launch description override. When omitted the runner generates a run-scoped description from the system and start time.")]
    public string? Description { get; set; }

    [Description("Whether to create the launch in debug mode.")]
    [DefaultValue(false)]
    public bool DebugMode { get; set; }

    [Description("Static launch attributes to add to every launch in addition to the default QaaS team/system/session/source attributes.")]
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Description("Bootstrap administrator username used to auto-provision ReportPortal projects, users, and filters. Defaults to superadmin.")]
    public string? BootstrapUsername { get; set; }

    [Description("Bootstrap administrator password used to auto-provision ReportPortal projects, users, and filters. Defaults to erebus.")]
    public string? BootstrapPassword { get; set; }

    [Description("OAuth client id used for ReportPortal bootstrap logins. Defaults to ui.")]
    public string? BootstrapClientId { get; set; }

    [Description("OAuth client secret used for ReportPortal bootstrap logins. Defaults to uiman.")]
    public string? BootstrapClientSecret { get; set; }

    [Description("Seed used to derive a deterministic password for the QaaS-managed per-project ReportPortal bot user. Defaults to qaas-reportportal-local.")]
    public string? BotPasswordSeed { get; set; }

    /// <summary>
    /// Resolves the effective ReportPortal settings for one runner invocation by applying environment overrides,
    /// deriving the project from the run descriptor when no explicit override exists, generating launch naming defaults,
    /// and validating that the final runtime contract is coherent.
    /// </summary>
    /// <param name="config">The YAML-bound configuration, if present.</param>
    /// <param name="runDescriptor">The runner-scoped launch descriptor used for project derivation and launch defaults.</param>
    /// <returns>The immutable runtime settings consumed by the launch manager and reporter.</returns>
    internal static ReportPortalSettings Resolve(ReportPortalConfig? config, ReportPortalRunDescriptor? runDescriptor)
    {
        var enabled = ReadBooleanValue(EnabledEnvironmentVariable) ?? config?.Enabled ?? true;
        var endpoint = NormalizeEndpoint(ReadStringValue(EndpointEnvironmentVariable) ?? config?.Endpoint ?? "http://localhost:8080");
        var explicitProject = ReadStringValue(ProjectEnvironmentVariable) ?? config?.Project;
        var explicitApiKey = ReadStringValue(ApiKeyEnvironmentVariable) ?? config?.ApiKey;
        var explicitLaunchName = ReadStringValue(LaunchNameEnvironmentVariable) ?? config?.LaunchName;
        var explicitDescription = ReadStringValue(DescriptionEnvironmentVariable) ?? config?.Description;
        var debugMode = ReadBooleanValue(DebugModeEnvironmentVariable) ?? config?.DebugMode ?? false;
        var bootstrapUsername = ReadStringValue(BootstrapUsernameEnvironmentVariable) ?? config?.BootstrapUsername ?? "superadmin";
        var bootstrapPassword = ReadStringValue(BootstrapPasswordEnvironmentVariable) ?? config?.BootstrapPassword ?? "erebus";
        var bootstrapClientId = ReadStringValue(BootstrapClientIdEnvironmentVariable) ?? config?.BootstrapClientId ?? "ui";
        var bootstrapClientSecret = ReadStringValue(BootstrapClientSecretEnvironmentVariable) ?? config?.BootstrapClientSecret ?? "uiman";
        var botPasswordSeed = ReadStringValue(BotPasswordSeedEnvironmentVariable) ?? config?.BotPasswordSeed ?? "qaas-reportportal-local";
        var attributes = config?.Attributes?.ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var resolvedProject = enabled || !string.IsNullOrWhiteSpace(explicitProject) || !string.IsNullOrWhiteSpace(runDescriptor?.TeamName)
            ? ResolveProjectName(explicitProject, runDescriptor)
            : string.Empty;
        var launchName = string.IsNullOrWhiteSpace(explicitLaunchName)
            ? runDescriptor?.BuildDefaultLaunchName()
            : explicitLaunchName.Trim();
        var description = string.IsNullOrWhiteSpace(explicitDescription)
            ? runDescriptor?.BuildDefaultDescription()
            : explicitDescription.Trim();

        var settings = new ReportPortalSettings(
            enabled,
            endpoint,
            resolvedProject,
            explicitApiKey?.Trim(),
            launchName,
            description,
            debugMode,
            attributes,
            runDescriptor?.TeamName,
            runDescriptor?.SystemName,
            runDescriptor?.SessionNames ?? [],
            bootstrapUsername.Trim(),
            bootstrapPassword.Trim(),
            bootstrapClientId.Trim(),
            bootstrapClientSecret.Trim(),
            botPasswordSeed.Trim());

        settings.Validate();
        return settings;
    }

    private static string ResolveProjectName(string? explicitProject, ReportPortalRunDescriptor? runDescriptor)
    {
        if (!string.IsNullOrWhiteSpace(explicitProject))
            return SanitizeProjectName(explicitProject);

        if (!string.IsNullOrWhiteSpace(runDescriptor?.TeamName))
            return SanitizeProjectName(runDescriptor.TeamName);

        throw new InvalidOperationException(
            "ReportPortal reporting requires a target project. Configure `ReportPortal.Project`, " +
            $"set {ProjectEnvironmentVariable}, or provide `MetaData.Team`.");
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

    private static string NormalizeEndpoint(string endpoint)
    {
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

    internal static string SanitizeProjectName(string value)
    {
        var sanitized = Regex.Replace(value.Trim(), "[^A-Za-z0-9_-]+", "-")
            .Trim('-', '_');

        if (sanitized.Length < 3)
            throw new InvalidOperationException(
                $"ReportPortal project name `{value}` is invalid after sanitization. The final name must contain at least 3 valid characters.");

        return sanitized;
    }
}

/// <summary>
/// Converts the raw configuration into immutable runtime settings that can be reused safely across all executions
/// inside one runner invocation. The settings expose both reporting details and bootstrap details used to provision
/// the target ReportPortal project when QaaS manages the project bot user itself.
/// </summary>
public sealed class ReportPortalSettings(
    bool enabled,
    string endpoint,
    string project,
    string? apiKey,
    string? launchName,
    string? description,
    bool debugMode,
    IReadOnlyDictionary<string, string> attributes,
    string? team,
    string? system,
    IReadOnlyList<string> sessionNames,
    string bootstrapUsername,
    string bootstrapPassword,
    string bootstrapClientId,
    string bootstrapClientSecret,
    string botPasswordSeed)
{
    public bool Enabled { get; } = enabled;
    public string Endpoint { get; } = endpoint;
    public string Project { get; } = project;
    public string? ApiKey { get; } = apiKey;
    public string LaunchName { get; } = string.IsNullOrWhiteSpace(launchName) ? "QaaS Run" : launchName;
    public string Description { get; } = string.IsNullOrWhiteSpace(description)
        ? "QaaS captured this run directly from the runner pipeline."
        : description;
    public bool DebugMode { get; } = debugMode;
    public IReadOnlyDictionary<string, string> Attributes { get; } = attributes;
    public string Team { get; } = string.IsNullOrWhiteSpace(team) ? "Unknown Team" : team.Trim();
    public string System { get; } = string.IsNullOrWhiteSpace(system) ? "Unknown System" : system.Trim();
    public IReadOnlyList<string> SessionNames { get; } = sessionNames
        .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
        .Select(sessionName => sessionName.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(sessionName => sessionName, StringComparer.Ordinal)
        .ToArray();
    public string BootstrapUsername { get; } = bootstrapUsername;
    public string BootstrapPassword { get; } = bootstrapPassword;
    public string BootstrapClientId { get; } = bootstrapClientId;
    public string BootstrapClientSecret { get; } = bootstrapClientSecret;
    public string BotPasswordSeed { get; } = botPasswordSeed;

    public Uri EndpointUri => new(Endpoint, UriKind.Absolute);

    public Uri GatewayUri
    {
        get
        {
            var builder = new UriBuilder(EndpointUri);
            builder.Path = "/";
            builder.Query = string.Empty;
            builder.Fragment = string.Empty;
            return builder.Uri;
        }
    }

    public bool UsesManagedProjectBot => string.IsNullOrWhiteSpace(ApiKey);

    public string ManagedBotLogin => $"qaas-rp-{Project.ToLowerInvariant()}";

    public string ManagedBotFullName => $"QaaS ReportPortal {Project} Bot";

    public string ManagedBotEmail => $"qaas.reportportal.{Project.ToLowerInvariant()}@example.com";

    public string ManagedBotExternalId => $"qaas-reportportal/{Project}";

    /// <summary>
    /// Validates that all data required for reporting and provisioning is present after defaults and overrides are applied.
    /// </summary>
    public void Validate()
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(Endpoint))
            throw new InvalidOperationException(
                $"ReportPortal reporting is enabled but neither `ReportPortal.Endpoint` nor {ReportPortalConfig.EndpointEnvironmentVariable} produced a usable endpoint.");

        if (string.IsNullOrWhiteSpace(Project))
            throw new InvalidOperationException(
                $"ReportPortal reporting is enabled but neither `ReportPortal.Project` nor {ReportPortalConfig.ProjectEnvironmentVariable} produced a usable project name.");

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
            throw new InvalidOperationException($"Resolved ReportPortal endpoint `{Endpoint}` is not a valid absolute URI.");

        if (UsesManagedProjectBot)
        {
            if (string.IsNullOrWhiteSpace(BootstrapUsername) ||
                string.IsNullOrWhiteSpace(BootstrapPassword) ||
                string.IsNullOrWhiteSpace(BootstrapClientId) ||
                string.IsNullOrWhiteSpace(BootstrapClientSecret) ||
                string.IsNullOrWhiteSpace(BotPasswordSeed))
            {
                throw new InvalidOperationException(
                    "ReportPortal managed-bot provisioning requires bootstrap credentials and a deterministic bot password seed.");
            }
        }
    }

    /// <summary>
    /// Builds the launch-level attributes that will be attached once when the runner opens the launch.
    /// </summary>
    /// <returns>The launch attributes in ReportPortal's request model.</returns>
    public IList<ItemAttribute> BuildLaunchAttributes()
    {
        var launchAttributes = new List<ItemAttribute>
        {
            new() { Key = "tool", Value = "QaaS" },
            new() { Key = "team", Value = Team },
            new() { Key = "system", Value = System },
            new() { Key = "source", Value = "runner" }
        };

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
    /// Builds the deterministic password for the QaaS-managed per-project ReportPortal bot user.
    /// The runner reuses this password on later runs instead of storing credentials locally.
    /// </summary>
    public string BuildManagedBotPassword()
    {
        var input = $"{Project}::{BotPasswordSeed}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"QaaS!{Convert.ToHexString(hashBytes)}";
    }

    /// <summary>
    /// Verifies that another settings instance targets the same ReportPortal launch contract.
    /// This prevents a single runner invocation from mixing multiple endpoints, projects, or launch shapes.
    /// </summary>
    /// <param name="other">The other settings instance to compare.</param>
    /// <returns><see langword="true" /> when both settings are launch-compatible; otherwise <see langword="false" />.</returns>
    public bool IsCompatibleWith(ReportPortalSettings other)
    {
        return Enabled == other.Enabled &&
               string.Equals(Endpoint, other.Endpoint, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Project, other.Project, StringComparison.Ordinal) &&
               string.Equals(ApiKey, other.ApiKey, StringComparison.Ordinal) &&
               string.Equals(LaunchName, other.LaunchName, StringComparison.Ordinal) &&
               string.Equals(Description, other.Description, StringComparison.Ordinal) &&
               DebugMode == other.DebugMode &&
               string.Equals(Team, other.Team, StringComparison.Ordinal) &&
               string.Equals(System, other.System, StringComparison.Ordinal) &&
               string.Equals(BootstrapUsername, other.BootstrapUsername, StringComparison.Ordinal) &&
               string.Equals(BootstrapPassword, other.BootstrapPassword, StringComparison.Ordinal) &&
               string.Equals(BootstrapClientId, other.BootstrapClientId, StringComparison.Ordinal) &&
               string.Equals(BootstrapClientSecret, other.BootstrapClientSecret, StringComparison.Ordinal) &&
               string.Equals(BotPasswordSeed, other.BotPasswordSeed, StringComparison.Ordinal) &&
               BuildAttributesSignature() == other.BuildAttributesSignature() &&
               BuildSessionSignature() == other.BuildSessionSignature();
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

    private string BuildSessionSignature()
    {
        return string.Join(";", SessionNames);
    }
}
