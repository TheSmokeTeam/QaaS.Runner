using System.ComponentModel;
using System.Runtime.CompilerServices;
using QaaS.Framework.Configurations.ConfigurationBindingUtils;
using QaaS.Framework.SDK.Extensions;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Assertions.LinkBuilders;

[assembly: InternalsVisibleTo("QaaS.Runner")]

namespace QaaS.Runner.Assertions.ConfigurationObjects;

public class LinkBuilder
{
    [Description("The display name of the link in the test results, if none is given uses the `Type` as the name")]
    public string? Name { get; internal set; }

    [Description("Links the kibana's discovery filtered for the test's session times to each test result.")]
    internal KibanaLinkConfig? Kibana { get; set; }

    [Description("Links the prometheus' graph filtered for the test's session times to each test result.")]
    internal PrometheusLinkConfig? Prometheus { get; set; }

    [Description("Links the grafana dashboard filtered for the test's session times to each test result.")]
    internal GrafanaLinkConfig? Grafana { get; set; }

    public LinkBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    public LinkBuilder Create(ILinkConfig config)
    {
        return Configure(config);
    }

    public ILinkConfig? ReadConfiguration()
    {
        if (Kibana != null)
        {
            return Kibana;
        }

        if (Prometheus != null)
        {
            return Prometheus;
        }

        return Grafana;
    }

    /// <summary>
    /// Applies a partial update to the currently configured link config while preserving omitted fields.
    /// </summary>
    public LinkBuilder UpdateConfiguration(Func<ILinkConfig, ILinkConfig> update)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Link configuration is not set");
        return Configure(currentConfig.MergeConfiguration(update(currentConfig))!);
    }

    /// <summary>
    /// Upserts the configured link config, merging same-type configs and replacing different config types.
    /// </summary>
    public LinkBuilder UpsertConfiguration(ILinkConfig config)
    {
        return Configure(ReadConfiguration().MergeConfiguration(config)!);
    }

    public LinkBuilder DeleteConfiguration()
    {
        Reset();
        return this;
    }
    
    private LinkBuilder Reset()
    {
        Kibana = null;
        Prometheus = null;
        Grafana = null;
        return this;
    }

    public LinkBuilder Configure(ILinkConfig config)
    {
        Reset();
        switch (config)
        {
            case KibanaLinkConfig kibanaLinkConfig:
                Kibana = kibanaLinkConfig;
                break;
            case PrometheusLinkConfig prometheusLinkConfig:
                Prometheus = prometheusLinkConfig;
                break;
            case GrafanaLinkConfig grafanaLinkConfig:
                Grafana = grafanaLinkConfig;
                break;
        }

        return this;
    }

    /// <summary>
    /// Builds the concrete link implementation and validates only one link source is configured.
    /// </summary>
    internal BaseLink Build()
    {
        var allTypes = new List<ILinkConfig?>
        {
            Kibana, Prometheus, Grafana
        };
        var type = allTypes.FirstOrDefault(configuredType => configuredType != null) ??
                   throw new InvalidOperationException("Missing supported type for policy");
        if (allTypes.Count(config => config != null) > 1)
        {
            var conflictingConfigs = allTypes
                .Where(config => config != null)
                .Select(config => config!.GetType().Name)
                .ToArray();
            throw new InvalidOperationException(
                $"Multiple configurations provided for Link: {string.Join(", ", conflictingConfigs)}. " +
                "Only one type is allowed at a time.");
        }

        var linkName = Name ?? type.ToString()!;
        return type switch
        {
            KibanaLinkConfig => new KibanaLink(linkName, Kibana!),
            PrometheusLinkConfig => new PrometheusLink(linkName, Prometheus!),
            GrafanaLinkConfig => new GrafanaLink(linkName, Grafana!),
            _ => throw new ArgumentException("Exception: Link must have a type.")
        };
    }
}
