using System.ComponentModel;

namespace QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;

/// <summary>
/// Defines the passive ReportPortal configuration used by QaaS to publish assertion results without provisioning or
/// mutating ReportPortal resources.
/// </summary>
public class ReportPortalConfig : IReporterConfig
{
    private sealed class StaticReportPortalDefaultsProvider(ReportPortalConfigurationDefaults defaults)
        : IReportPortalConfigurationDefaultsProvider
    {
        public ReportPortalConfigurationDefaults GetDefaults() => defaults;
    }

    private bool _enabledConfigured;
    private static IReportPortalConfigurationDefaultsProvider? _defaultsProvider;

    internal bool EnabledConfigured => _enabledConfigured;

    [Description(
        "Whether to publish runner assertion results to ReportPortal in addition to Allure. Defaults to the registered QaaS.Configuration value.")]
    [DefaultValue(true)]
    public bool Enabled
    {
        get;
        set
        {
            field = value;
            _enabledConfigured = true;
        }
    }

    [Description("ReportPortal endpoint URL. Defaults to the registered global ReportPortal URL. Accepts either the gateway URL or the API URL and normalizes it to /api/.")]
    [DefaultValue(null)]
    public string? Endpoint { get; set; }

    [Description("ReportPortal project where the launch will be published. Defaults to MetaData.Team; QaaS routes launches by MetaData.Team at runtime.")]
    [DefaultValue(null)]
    public string? Project { get; set; }

    [Description("ReportPortal API key used for best-effort publishing when reporting is enabled. Defaults to the registered global API key.")]
    [DefaultValue(null)]
    public string? ApiKey { get; set; }

    [Description("Optional launch name override. When omitted the runner derives the launch name from the grouped run descriptor.")]
    [DefaultValue(null)]
    public string? LaunchName { get; set; }

    [Description("Optional launch description override. When omitted the runner derives the launch description from the grouped run descriptor.")]
    [DefaultValue(null)]
    public string? Description { get; set; }

    [Description("Whether to create the launch in debug mode.")]
    [DefaultValue(false)]
    public bool DebugMode { get; set; }

    [Description("Static launch attributes to add to every launch in addition to the default QaaS team/system/session/source attributes.")]
    [DefaultValue(null)]
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
