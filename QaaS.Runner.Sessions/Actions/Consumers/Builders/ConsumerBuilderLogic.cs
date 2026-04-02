using QaaS.Framework.Configurations;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Elastic;
using QaaS.Framework.Protocols.ConfigurationObjects.IbmMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.S3;
using QaaS.Framework.Protocols.ConfigurationObjects.Socket;
using QaaS.Framework.Protocols.ConfigurationObjects.Sql;
using QaaS.Framework.Protocols.Protocols.Factories;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.RuntimeOverrides;
using InvalidOperationException = System.InvalidOperationException;

namespace QaaS.Runner.Sessions.Actions.Consumers.Builders;

public partial class ConsumerBuilder
{
    public IReaderConfig? Configuration
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
    /// Sets the name used for the current Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the stage used by the current Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    /// <summary>
    /// Configures timeout on the current Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder WithTimeout(int timeoutMs)
    {
        TimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// Sets the data filter used by the current Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder FilterData(DataFilter dataFilter)
    {
        DataFilter = dataFilter;
        return this;
    }

    /// <summary>
    /// Sets the deserializer configuration used by the current Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder WithDeserializer(DeserializeConfig deserializeConfig)
    {
        Deserialize = deserializeConfig;
        return this;
    }

    /// <summary>
    /// Adds the supplied policy to the current Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    internal ConsumerBuilder AddPolicy(PolicyBuilder policy)
    {
        var policiesList = Policies.ToList();
        policiesList.Add(policy);
        Policies = policiesList.ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured policy entry on the current Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder CreatePolicy(PolicyBuilder policy)
    {
        return AddPolicy(policy);
    }

    /// <summary>
    /// Returns the configured policies currently stored on the Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public IReadOnlyList<PolicyBuilder> ReadPolicies()
    {
        return Policies;
    }

    /// <summary>
    /// Updates the configured policy at the specified index on the current Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder UpdatePolicyAt(int index, PolicyBuilder policy)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies[index] = policy;
        return this;
    }

    /// <summary>
    /// Removes the configured policy at the specified index from the current Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder DeletePolicyAt(int index)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies = Policies.Where((_, i) => i != index).ToArray();
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    internal ConsumerBuilder AddConfiguration(IReaderConfig config)
    {
        return Configure(config);
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    internal ConsumerBuilder Create(IReaderConfig config)
    {
        return AddConfiguration(config);
    }

    /// <summary>
    /// Returns the configuration currently stored on the Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder UpdateConfiguration(Func<IReaderConfig, IReaderConfig> update)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Consumer configuration is not set");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder UpdateConfiguration(IReaderConfig config)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Consumer configuration is not set");
        return Configure(ConfigurationUpdateExtensions.UpdateConfiguration(currentConfig, config));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder UpdateConfiguration(object configuration)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Consumer configuration is not set");
        return Configure(ConfigurationUpdateExtensions.UpdateConfiguration(currentConfig, configuration));
    }

    /// <summary>
    /// Clears the configuration currently stored on the Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder DeleteConfiguration()
    {
        return Reset();
    }

    private ConsumerBuilder Reset()
    {
        RabbitMq = null;
        KafkaTopic = null;
        Socket = null;
        IbmMqQueue = null;
        PostgreSqlTable = null;
        OracleSqlTable = null;
        MsSqlTable = null;
        TrinoSqlTable = null;
        ElasticIndices = null;
        S3Bucket = null;
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner consumer builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner consumer builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Consumers" />
    public ConsumerBuilder Configure(IReaderConfig config)
    {
        Reset();
        switch (config)
        {
            case RabbitMqReaderConfig rabbitMqReaderConfig:
                RabbitMq = rabbitMqReaderConfig;
                break;
            case SocketReaderConfig socketReaderConfig:
                Socket = socketReaderConfig;
                break;
            case S3BucketReaderConfig s3ReaderConfig:
                S3Bucket = s3ReaderConfig;
                break;
            case KafkaTopicReaderConfig kafkaReaderConfig:
                KafkaTopic = kafkaReaderConfig;
                break;
            case IbmMqReaderConfig ibmMqReaderConfig:
                IbmMqQueue = ibmMqReaderConfig;
                break;
            case TrinoReaderConfig trinoReaderConfig:
                TrinoSqlTable = trinoReaderConfig;
                break;
            case OracleReaderConfig oracleReaderConfig:
                OracleSqlTable = oracleReaderConfig;
                break;
            case PostgreSqlReaderConfig postgreSqlReaderConfig:
                PostgreSqlTable = postgreSqlReaderConfig;
                break;
            case MsSqlReaderConfig mssqlReaderConfig:
                MsSqlTable = mssqlReaderConfig;
                break;
            case ElasticReaderConfig elasticReaderConfig:
                ElasticIndices = elasticReaderConfig;
                break;
        }

        return this;
    }

    /// <summary>
    /// Builds a concrete consumer action and degrades to an action failure instead of throwing on invalid setup.
    /// </summary>
    internal BaseConsumer? Build(InternalContext context, IList<ActionFailure> actionFailures, string sessionName)
    {
        return BuildWithTimeZone(context, actionFailures, sessionName, TimeZoneInfoResolver.DefaultTimeZoneId);
    }

    /// <summary>
    /// Builds a concrete consumer action and degrades to an action failure instead of throwing on invalid setup.
    /// </summary>
    internal BaseConsumer? BuildWithTimeZone(
        InternalContext context,
        IList<ActionFailure> actionFailures,
        string sessionName,
        string timeZoneId)
    {
        IReaderConfig? type = null;
        try
        {
            var policies = PolicyBuilder.BuildPolicies(Policies);
            var serializationType = Deserialize?.Deserializer;
            var deserializerSpecificType = Deserialize?.SpecificType?.GetConfiguredType();
            var timeout = TimeSpan.FromMilliseconds(TimeoutMs!.Value);
            var initialTimeout = TimeSpan.FromMilliseconds(InitialTimeoutMs!.Value);
            var allTypes = new List<IReaderConfig?>
            {
                RabbitMq, KafkaTopic, Socket, IbmMqQueue, PostgreSqlTable, OracleSqlTable, MsSqlTable, TrinoSqlTable,
                ElasticIndices, S3Bucket
            };
            if (allTypes.Count(config => config != null) > 1)
            {
                var conflictingConfigs = allTypes
                    .Where(config => config != null)
                    .Select(config => config!.GetType().Name)
                    .ToArray();
                throw new InvalidOperationException(
                    $"Multiple configurations provided for Consumer '{Name}': {string.Join(", ", conflictingConfigs)}. " +
                    "Only one type is allowed at a time.");
            }

            type = allTypes.FirstOrDefault(configuredType => configuredType != null) ??
                   throw new InvalidOperationException($"Missing supported type in consumer {Name}");
            var readerChunkMode = ProtocolChunkSupport.ResolveReaderMode(type);
            if (readerChunkMode == ProtocolChunkMode.SingleOrChunk)
            {
                var propertyName = ProtocolChunkSupport.GetReaderConfigurationPropertyName(type);
                throw new InvalidOperationException(
                    $"The {propertyName} field is ambiguous because the configured protocol supports both single and chunk reading, but consumer configuration does not expose a chunk selection option.");
            }

            var overrideRequest = new ConsumerOverrideRequest(Name!, type, context.Logger, DataFilter, timeZoneId);
            var (reader, chunkReader) = context.GetSessionActionOverrides()?.Consumer?.Invoke(overrideRequest)
                                        ?? ProtocolFactoryCompatibility.CreateReader(
                                            type,
                                            context.Logger,
                                            DataFilter,
                                            timeZoneId);
            var consumerTypeName = reader?.GetType().Name ?? chunkReader?.GetType().Name ?? "Unknown";
            
            context.Logger.LogDebugWithMetaData("Started building Consumer of type {type}",
                context.GetMetaDataOrDefault(), new object?[] { consumerTypeName });

            return reader != null
                ? new Consumer(Name!, reader, timeout, initialTimeout, Stage, policies, DataFilter, serializationType,
                    deserializerSpecificType, context.Logger)
                : chunkReader != null
                    ? new ChunkConsumer(Name!, chunkReader, timeout, initialTimeout, Stage, policies, DataFilter,
                        serializationType,
                        deserializerSpecificType, context.Logger)
                    : null;
        }
        catch (Exception e)
        {
            actionFailures.AppendActionFailure(e, sessionName, context.Logger, nameof(Consumer), Name!,
                type?.GetType().Name);
        }

        return null;
    }

    private IReaderConfig? GetConfiguration()
    {
        if (RabbitMq != null) return RabbitMq;
        if (KafkaTopic != null) return KafkaTopic;
        if (Socket != null) return Socket;
        if (IbmMqQueue != null) return IbmMqQueue;
        if (PostgreSqlTable != null) return PostgreSqlTable;
        if (OracleSqlTable != null) return OracleSqlTable;
        if (MsSqlTable != null) return MsSqlTable;
        if (TrinoSqlTable != null) return TrinoSqlTable;
        if (ElasticIndices != null) return ElasticIndices;
        return S3Bucket;
    }
}
