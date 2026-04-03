using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using QaaS.Framework.Configurations;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Prometheus;
using QaaS.Framework.Protocols.Protocols.Factories;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.RuntimeOverrides;

namespace QaaS.Runner.Sessions.Actions.Collectors;

/// <summary>
/// Fluent builder for collector actions and their fetcher configuration.
/// </summary>
public class CollectorBuilder
{
    [Required]
    [Description("The name of the collector")]
    public string? Name { get; internal set; }

    [Description("How to filter the properties of each returned collected data")]
    public DataFilter DataFilter { get; internal set; } = new();
    [Description("The collection range of the collector's action contains parameters for the start and end times " +
                 "of the collection range in relation to the start and end time of the collector's session.")]
    public CollectionRange CollectionRange { get; internal set; } = new();
    [Range(uint.MinValue, uint.MaxValue)]
    [Description(
        "The check interval in milliseconds of the check that the current UTC time is past" +
        " the collection end time, so the collection action can happen.")]
    [DefaultValue(1000)]
    public uint EndTimeReachedCheckIntervalMs { get; internal set; } = 1000;
    [Description(
        "Collects messages from the prometheus `query_range` API and saves each of them as an item of a vector result's array." +
        " vector is a result type in prometheus that represents a set of time series data, every item of" +
        " its result array represents a single value at a certain time.")]
    public PrometheusFetcherConfig? Prometheus { get; internal set; }
    public IFetcherConfig? Configuration
    {
        get => Prometheus;
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
    /// Sets the name used for the current Runner collector builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner collector builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Collectors" />
    public CollectorBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the data filter used by the current Runner collector builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner collector builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Collectors" />
    public CollectorBuilder FilterData(DataFilter dataFilter)
    {
        DataFilter = dataFilter;
        return this;
    }

    /// <summary>
    /// Configures collect in range on the current Runner collector builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner collector builder API surface in code. The behavior exposed here is part of the public surface that the generated function documentation groups under 'Configuration as Code / Collectors'.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Collectors" />
    public CollectorBuilder CollectInRange(CollectionRange collectionRange)
    {
        CollectionRange = collectionRange;
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner collector builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner collector builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Collectors" />
    /// <summary>
     /// Updates the configuration currently stored on the Runner collector builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner collector builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Collectors" />
    public CollectorBuilder UpdateConfiguration(Func<IFetcherConfig, IFetcherConfig> update)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Collector configuration is not set");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner collector builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner collector builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Collectors" />
    public CollectorBuilder UpdateConfiguration(IFetcherConfig config)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Collector configuration is not set");
        return Configure(ConfigurationUpdateExtensions.UpdateConfiguration(currentConfig, config));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner collector builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner collector builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Collectors" />
    public CollectorBuilder UpdateConfiguration(object configuration)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Collector configuration is not set");
        return Configure(ConfigurationUpdateExtensions.UpdateConfiguration(currentConfig, configuration));
    }

    private CollectorBuilder Reset()
    {
        Prometheus = null;
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner collector builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner collector builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Collectors" />
    public CollectorBuilder Configure(IFetcherConfig config)
    {
        Reset();
        switch (config)
        {
            case PrometheusFetcherConfig prometheusFetcherConfig:
                Prometheus = prometheusFetcherConfig;
                break;
            default:
                throw new ArgumentException("Exception: unsupported config type in collector");
        }

        return this;
    }

    /// <summary>
    /// Returns the configuration currently stored on the Runner collector builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner collector builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Collectors" />
    internal Collector? Build(InternalContext context, IList<ActionFailure> actionFailures, string sessionName)
    {
        IFetcherConfig? type = null;
        try
        {
            var allTypes = new List<IFetcherConfig?>
            {
                Prometheus
            };
            if (allTypes.Count(config => config != null) > 1)
            {
                var conflictingConfigs = allTypes
                    .Where(config => config != null)
                    .Select(config => config!.GetType().Name)
                    .ToArray();
                throw new InvalidOperationException(
                    $"Multiple configurations provided for Collector '{Name}': {string.Join(", ", conflictingConfigs)}. " +
                    "Only one type is allowed at a time.");
            }

            type = allTypes.FirstOrDefault(configuredType => configuredType != null) ??
                   throw new InvalidOperationException($"Missing supported type in collector {Name}");
            var collectorTypeName = type.GetType().Name;
            context.Logger.LogDebugWithMetaData("Started building Collector of type {type}",
                context.GetMetaDataOrDefault(), new object?[] { collectorTypeName });

            var overrideRequest = new CollectorOverrideRequest(Name!, type, context.Logger);
            var fetcher = context.GetSessionActionOverrides()?.Collector?.Invoke(overrideRequest)
                          ?? FetcherFactory.CreateFetcher(type, context.Logger);
            
            return new Collector(Name!, fetcher, DataFilter, CollectionRange.StartTimeMs, CollectionRange.EndTimeMs,
                EndTimeReachedCheckIntervalMs, context.Logger);
        }
        catch (Exception e)
        {
            actionFailures.AppendActionFailure(e, sessionName, context.Logger, nameof(Collector), Name!,
                type?.GetType().Name);
        }

        return null;
    }
}
