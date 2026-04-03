using System.ComponentModel;
using System.Runtime.CompilerServices;
using QaaS.Framework.Configurations;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Assertions.LinkBuilders;

[assembly: InternalsVisibleTo("QaaS.Runner")]

namespace QaaS.Runner.Assertions.ConfigurationObjects;

/// <summary>
/// Fluent builder for assertion link configuration and runtime link creation.
/// </summary>
public class LinkBuilder
{
    [Description("The display name of the link in the test results, if none is given uses the `Type` as the name")]
    public string? Name { get; internal set; }

    [Description("Links the kibana's discovery filtered for the test's session times to each test result.")]
    public KibanaLinkConfig? Kibana { get; internal set; }
    [Description("Links the prometheus' graph filtered for the test's session times to each test result.")]
    public PrometheusLinkConfig? Prometheus { get; internal set; }
    [Description("Links the grafana dashboard filtered for the test's session times to each test result.")]
    public GrafanaLinkConfig? Grafana { get; internal set; }
    public ILinkConfig? Configuration
    {
        get => GetConfiguration();
        internal set
        {
            if (value == null)
            {
                Reset();
                return;
            }

            Configure(value);
        }
    }
    /// <summary>
    /// Sets the name used for the current Runner link builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner link builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Links" />
    public LinkBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner link builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner link builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Links" />
    /// <summary>
     /// Updates the configuration currently stored on the Runner link builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner link builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Links" />
    public LinkBuilder UpdateConfiguration(Func<ILinkConfig, ILinkConfig> update)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Link configuration is not set");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner link builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner link builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Links" />
    public LinkBuilder UpdateConfiguration(ILinkConfig config)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Link configuration is not set");
        return Configure(currentConfig.UpdateConfiguration(config));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner link builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner link builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Links" />
    public LinkBuilder UpdateConfiguration(object configuration)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Link configuration is not set");
        return Configure(currentConfig.UpdateConfiguration(configuration));
    }

    private LinkBuilder Reset()
    {
        Kibana = null;
        Prometheus = null;
        Grafana = null;
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner link builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner link builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Links" />
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
    /// Returns the configuration currently stored on the Runner link builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner link builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Links" />
    private ILinkConfig? GetConfiguration()
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
