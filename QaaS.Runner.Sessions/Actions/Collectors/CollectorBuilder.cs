using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

public class CollectorBuilder
{
    [Required]
    [Description("The name of the collector")]
    public string? Name { get; internal set; }

    [Description("How to filter the properties of each returned collected data")]
    internal DataFilter DataFilter { get; set; } = new();

    [Description("The collection range of the collector's action contains parameters for the start and end times " +
                 "of the collection range in relation to the start and end time of the collector's session.")]
    internal CollectionRange CollectionRange { get; set; } = new();

    [Range(uint.MinValue, uint.MaxValue)]
    [Description(
        "The check interval in milliseconds of the check that the current UTC time is past" +
        " the collection end time, so the collection action can happen.")]
    [DefaultValue(1000)]
    internal uint EndTimeReachedCheckIntervalMs { get; set; } = 1000;

    [Description(
        "Collects messages from the prometheus `query_range` API and saves each of them as an item of a vector result's array." +
        " vector is a result type in prometheus that represents a set of time series data, every item of" +
        " its result array represents a single value at a certain time.")]
    internal PrometheusFetcherConfig? Prometheus { get; set; }

    public CollectorBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    public CollectorBuilder FilterData(DataFilter dataFilter)
    {
        DataFilter = dataFilter;
        return this;
    }

    public CollectorBuilder CollectInRange(CollectionRange collectionRange)
    {
        CollectionRange = collectionRange;
        return this;
    }

    public CollectorBuilder Create(IFetcherConfig config)
    {
        return Configure(config);
    }

    public IFetcherConfig? ReadConfiguration()
    {
        return Prometheus;
    }

    /// <summary>
    /// Applies a computed partial update to the current collector configuration while preserving omitted fields.
    /// </summary>
    public CollectorBuilder UpdateConfiguration(Func<IFetcherConfig, IFetcherConfig> update)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Collector configuration is not set");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the collector configuration by merging same-type values and replacing the current type when needed.
    /// </summary>
    public CollectorBuilder UpdateConfiguration(IFetcherConfig config)
    {
        return Configure(ReadConfiguration().UpdateConfiguration(config));
    }

    public CollectorBuilder DeleteConfiguration()
    {
        Reset();
        return this;
    }

    private void Reset()
    {
        Prometheus = null;
    }

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
    /// Creates a runtime collector action and captures failures into <paramref name="actionFailures"/> instead of throwing.
    /// </summary>
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

            var factoryRequest = new CollectorFactoryRequest(Name!, type, context.Logger);
            var fetcher = context.GetSessionActionFactoryOverrides()?.CollectorFactory?.Invoke(factoryRequest)
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
