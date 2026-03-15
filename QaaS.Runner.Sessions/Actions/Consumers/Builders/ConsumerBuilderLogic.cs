using QaaS.Framework.Configurations.ConfigurationBindingUtils;
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
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.Testing;
using InvalidOperationException = System.InvalidOperationException;

namespace QaaS.Runner.Sessions.Actions.Consumers.Builders;

public partial class ConsumerBuilder
{
    public ConsumerBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    public ConsumerBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    public ConsumerBuilder WithTimeout(int timeoutMs)
    {
        TimeoutMs = timeoutMs;
        return this;
    }

    public ConsumerBuilder FilterData(DataFilter dataFilter)
    {
        DataFilter = dataFilter;
        return this;
    }

    public ConsumerBuilder WithDeserializer(DeserializeConfig deserializeConfig)
    {
        Deserialize = deserializeConfig;
        return this;
    }

    public ConsumerBuilder AddPolicy(PolicyBuilder policy)
    {
        var policiesList = Policies.ToList();
        policiesList.Add(policy);
        Policies = policiesList.ToArray();
        return this;
    }

    public ConsumerBuilder CreatePolicy(PolicyBuilder policy)
    {
        return AddPolicy(policy);
    }

    public IReadOnlyList<PolicyBuilder> ReadPolicies()
    {
        return Policies;
    }

    public ConsumerBuilder UpdatePolicyAt(int index, PolicyBuilder policy)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies[index] = policy;
        return this;
    }

    public ConsumerBuilder DeletePolicyAt(int index)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies = Policies.Where((_, i) => i != index).ToArray();
        return this;
    }

    public ConsumerBuilder CreateConfiguration(IReaderConfig config)
    {
        return Configure(config);
    }

    public IReaderConfig? ReadConfiguration()
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

    /// <summary>
    /// Applies a partial update to the current consumer configuration while preserving omitted fields.
    /// </summary>
    public ConsumerBuilder UpdateConfiguration(Func<IReaderConfig, IReaderConfig> update)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Consumer configuration is not set");
        return Configure(currentConfig.MergeConfiguration(update(currentConfig))!);
    }

    /// <summary>
    /// Upserts the consumer configuration, merging same-type configs and replacing different config types.
    /// </summary>
    public ConsumerBuilder UpsertConfiguration(IReaderConfig config)
    {
        return Configure(ReadConfiguration().MergeConfiguration(config)!);
    }

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
        IReaderConfig? type = null;
        try
        {
            var policies = PolicyBuilder.BuildPolicies(Policies);
            var serializationType = Deserialize?.Deserializer;
            var deserializerSpecificType = Deserialize?.SpecificType?.GetConfiguredType();
            var timeout = TimeSpan.FromMilliseconds(TimeoutMs!.Value);
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

            var factoryRequest = new ConsumerFactoryRequest(Name!, type, context.Logger, DataFilter);
            var (reader, chunkReader) = context.GetSessionActionFactoryOverrides()?.ConsumerFactory?.Invoke(factoryRequest)
                                        ?? ReaderFactory.CreateReader(type, context.Logger, DataFilter);
            var consumerTypeName = reader?.GetType().Name ?? chunkReader?.GetType().Name ?? "Unknown";
            
            context.Logger.LogDebugWithMetaData("Started building Consumer of type {type}",
                context.GetMetaDataOrDefault(), new object?[] { consumerTypeName });

            return reader != null
                ? new Consumer(Name!, reader, timeout, Stage, policies, DataFilter, serializationType,
                    deserializerSpecificType, context.Logger)
                : chunkReader != null
                    ? new ChunkConsumer(Name!, chunkReader, timeout, Stage, policies, DataFilter,
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
}
