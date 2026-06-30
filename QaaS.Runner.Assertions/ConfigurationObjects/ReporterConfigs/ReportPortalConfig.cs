using System.ComponentModel;

namespace QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;

/// <summary>
/// Configuration for ReportPortal reporting. Each field is optional and will fall back to the default value provided by the registered defaults provider when not set.
/// </summary>
public class ReportPortalConfig : IReporterConfig
{
    [Description("Whether to enable ReportPortal reporting")]
    [DefaultValue("QaaS.Configuration defaults")]
    public bool? Enabled { get; set; }

    [Description("ReportPortal endpoint URI. Accepts either the gateway URL or the API URL and normalizes it to /api/. Defaults to the global API URL.")]
    [DefaultValue("Global URL in QaaS.Configuration")]
    public string? Endpoint { get; set; }

    [Description("ReportPortal project where the launch will be published. Default is MetaData.Team value.")]
    [DefaultValue("MetaData.Team value")]
    public string? Project { get; set; }

    [Description("ReportPortal API key used for publishing. Defaults to the global API key.")]
    [DefaultValue("Global API key in QaaS.Configuration")]
    public string? ApiKey { get; set; }

    [Description("Optional launch name override.")]
    [DefaultValue("Generated according MetaData.Team, MetaData.System and the session occured")]
    public string? LaunchName { get; set; }

    [Description("Optional launch description override.")]
    [DefaultValue("Default QaaS description")]
    public string? Description { get; set; }

    [Description("Whether to create the launch in debug mode.")]
    [DefaultValue(false)]
    public bool? DebugMode { get; set; } = false;

    [Description("Static launch attributes to add to every launch in addition to the default QaaS team/system/session/source attributes.")]
    [DefaultValue(null)]
    public Dictionary<string, string>? Attributes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    private static IReportPortalConfigurationDefaultsProvider? _defaultsProvider;

    /// <summary>
    /// Provides fixed ReportPortal configuration defaults from a supplied defaults object.
    /// </summary>
    /// <param name="defaults">The defaults to return whenever requested.</param>
    private sealed class StaticReportPortalDefaultsProvider(ReportPortalConfigurationDefaults defaults)
        : IReportPortalConfigurationDefaultsProvider
    {
        /// <summary>
        /// Gets the configured static ReportPortal defaults.
        /// </summary>
        /// <returns>The ReportPortal defaults supplied to this provider.</returns>
        public ReportPortalConfigurationDefaults GetDefaults() => defaults;
    }

    /// <summary>
    /// Registers the provider used to supply global ReportPortal configuration defaults.
    /// </summary>
    /// <param name="defaultsProvider">The provider that supplies default ReportPortal settings.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="defaultsProvider"/> is <see langword="null"/>.
    /// </exception>
    public static void RegisterDefaultsProvider(IReportPortalConfigurationDefaultsProvider defaultsProvider)
    {
        ArgumentNullException.ThrowIfNull(defaultsProvider);
        _defaultsProvider = defaultsProvider;
    }

    /// <summary>
    /// Registers static global ReportPortal configuration defaults.
    /// </summary>
    /// <param name="enabled">Default value indicating whether ReportPortal reporting is enabled.</param>
    /// <param name="reportPortalUri">Default ReportPortal endpoint URI.</param>
    /// <param name="reportPortalApiKey">Default ReportPortal API key.</param>
    public static void RegisterDefaults(
        bool? enabled,
        string? reportPortalUri = null,
        string? reportPortalApiKey = null
    ) =>
        RegisterDefaultsProvider(
            new StaticReportPortalDefaultsProvider(
                new ReportPortalConfigurationDefaults
                {
                    Enabled = enabled,
                    ReportPortalUri = reportPortalUri,
                    ReportPortalApiKey = reportPortalApiKey,
                }
            )
        );

    /// <summary>
    /// Gets the registered ReportPortal defaults provider.
    /// </summary>
    /// <returns>
    /// The registered defaults provider, or <see langword="null"/> when no provider was registered.
    /// </returns>
    public static IReportPortalConfigurationDefaultsProvider? GetDefaultsProvider() => _defaultsProvider;

    /// <summary>
    /// Resolves unset ReportPortal values against the currently registered defaults provider.
    /// </summary>
    /// <returns>A copy of the current configuration with provider-backed defaults applied to unset fields.</returns>
    internal ReportPortalConfig ResolveDefaults()
    {
        var defaults = _defaultsProvider?.GetDefaults();
        return new ReportPortalConfig
        {
            Enabled = Enabled ?? defaults?.Enabled,
            Endpoint = Endpoint ?? defaults?.ReportPortalUri,
            Project = Project,
            ApiKey = ApiKey ?? defaults?.ReportPortalApiKey,
            LaunchName = LaunchName,
            Description = Description,
            DebugMode = DebugMode,
            Attributes = Attributes is null
                ? null
                : new Dictionary<string, string>(Attributes, StringComparer.OrdinalIgnoreCase),
        };
    }
}

/// <summary>
/// Represents global default values used by ReportPortal configuration.
/// </summary>
public sealed record ReportPortalConfigurationDefaults
{
    /// <summary>
    /// Gets the default value indicating whether ReportPortal reporting is enabled.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Gets the default ReportPortal endpoint URI.
    /// </summary>
    public string? ReportPortalUri { get; init; }

    /// <summary>
    /// Gets the default ReportPortal API key.
    /// </summary>
    public string? ReportPortalApiKey { get; init; }
}

/// <summary>
/// Provides default ReportPortal configuration values.
/// </summary>
public interface IReportPortalConfigurationDefaultsProvider
{
    /// <summary>
    /// Gets the default ReportPortal configuration values.
    /// </summary>
    /// <returns>The default ReportPortal configuration values.</returns>
    ReportPortalConfigurationDefaults GetDefaults();
}
