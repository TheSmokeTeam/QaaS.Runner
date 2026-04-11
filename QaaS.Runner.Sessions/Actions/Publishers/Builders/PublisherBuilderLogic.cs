using QaaS.Framework.Configurations;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Elastic;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.Protocols.ConfigurationObjects.MongoDb;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Redis;
using QaaS.Framework.Protocols.ConfigurationObjects.S3;
using QaaS.Framework.Protocols.ConfigurationObjects.Sftp;
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
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.RuntimeOverrides;
using Parallel = QaaS.Runner.Sessions.ConfigurationObjects.Parallel;

namespace QaaS.Runner.Sessions.Actions.Publishers.Builders;

public partial class PublisherBuilder
{
    public ISenderConfig? Configuration
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
    /// Sets the name used for the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the stage used by the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    /// <summary>
    /// Sets the data filter used by the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder FilterData(DataFilter dataFilter)
    {
        DataFilter = dataFilter;
        return this;
    }

    /// <summary>
    /// Sets the serializer configuration used by the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder WithSerializer(SerializeConfig serializeConfig)
    {
        Serialize = serializeConfig;
        return this;
    }

    /// <summary>
    /// Sets how many iterations the transaction should execute.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder WithIterations(int iterations)
    {
        Iterations = iterations;
        return this;
    }

    /// <summary>
    /// Adds the supplied data source to the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder AddDataSource(string dataSourceName)
    {
        var dataSourceNamesList = DataSourceNames?.ToList() ?? [];
        dataSourceNamesList.Add(dataSourceName);
        DataSourceNames = dataSourceNamesList.ToArray();
        return this;
    }

    /// <summary>
    /// Updates the configured data source stored on the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder UpdateDataSource(string existingValue, string newValue)
    {
        if (DataSourceNames == null)
        {
            return this;
        }

        var index = Array.IndexOf(DataSourceNames, existingValue);
        if (index >= 0)
        {
            DataSourceNames[index] = newValue;
        }

        return this;
    }

    /// <summary>
    /// Removes the configured data source from the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder RemoveDataSource(string dataSourceName)
    {
        DataSourceNames = DataSourceNames?.Where(value => value != dataSourceName).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source at the specified index from the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder RemoveDataSourceAt(int index)
    {
        if (DataSourceNames == null)
        {
            return this;
        }

        if (index < 0 || index >= DataSourceNames.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        DataSourceNames = DataSourceNames.Where((_, i) => i != index).ToArray();
        return this;
    }

    /// <summary>
    /// Adds the supplied data source pattern to the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder AddDataSourcePattern(string dataSourcePattern)
    {
        var dataSourcePatternsList = DataSourcePatterns?.ToList() ?? [];
        dataSourcePatternsList.Add(dataSourcePattern);
        DataSourcePatterns = dataSourcePatternsList.ToArray();
        return this;
    }

    /// <summary>
    /// Updates the configured data source pattern stored on the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder UpdateDataSourcePattern(string existingValue, string newValue)
    {
        if (DataSourcePatterns == null)
        {
            return this;
        }

        var index = Array.IndexOf(DataSourcePatterns, existingValue);
        if (index >= 0)
        {
            DataSourcePatterns[index] = newValue;
        }

        return this;
    }

    /// <summary>
    /// Removes the configured data source pattern from the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder RemoveDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = DataSourcePatterns?.Where(value => value != dataSourcePattern).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source pattern at the specified index from the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder RemoveDataSourcePatternAt(int index)
    {
        if (DataSourcePatterns == null)
        {
            return this;
        }

        if (index < 0 || index >= DataSourcePatterns.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        DataSourcePatterns = DataSourcePatterns.Where((_, i) => i != index).ToArray();
        return this;
    }

    /// <summary>
    /// Marks the transaction to execute continuously in loop mode.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder InLoops()
    {
        Loop = true;
        return this;
    }

    /// <summary>
    /// Sets the delay applied between transaction iterations.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder WithSleep(ulong sleepTimeMs)
    {
        SleepTimeMs = sleepTimeMs;
        return this;
    }

    /// <summary>
    /// Configures chunks on the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder WithChunks(Chunks chunks)
    {
        Chunk = chunks;
        return this;
    }

    /// <summary>
    /// Adds the supplied policy to the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder AddPolicy(PolicyBuilder policy)
    {
        var policiesList = Policies.ToList();
        policiesList.Add(policy);
        Policies = policiesList.ToArray();
        return this;
    }

    /// <summary>
    /// Updates the configured policy at the specified index on the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder UpdatePolicyAt(int index, PolicyBuilder policy)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies[index] = policy;
        return this;
    }

    /// <summary>
    /// Removes the configured policy at the specified index from the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder RemovePolicyAt(int index)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies = Policies.Where((_, i) => i != index).ToArray();
        return this;
    }

    /// <summary>
    /// Configures parallelism on the current Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder WithParallelism(int parallelism)
    {
        Parallel = new Parallel { Parallelism = parallelism };
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    internal PublisherBuilder AddConfiguration(ISenderConfig config)
    {
        return Configure(config);
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    internal PublisherBuilder Create(ISenderConfig config)
    {
        return AddConfiguration(config);
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder UpdateConfiguration(Func<ISenderConfig, ISenderConfig> update)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException(
                                "Publisher configuration is not set and cannot be inferred from an update function.");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder UpdateConfiguration(ISenderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var currentConfig = Configuration;
        return Configure(currentConfig == null
            ? config
            : ConfigurationUpdateExtensions.UpdateConfiguration(currentConfig, config));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder UpdateConfiguration(object configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration is ISenderConfig typedConfiguration)
        {
            return UpdateConfiguration(typedConfiguration);
        }

        var currentConfig = Configuration ??
                            throw new InvalidOperationException(
                                "Publisher configuration is not set and cannot be inferred from an object patch. Configure a concrete publisher configuration first.");
        return Configure(ConfigurationUpdateExtensions.UpdateConfiguration(currentConfig, configuration));
    }

    private PublisherBuilder Reset()
    {
        RabbitMq = null;
        Socket = null;
        Sftp = null;
        S3Bucket = null;
        KafkaTopic = null;
        OracleSqlTable = null;
        PostgreSqlTable = null;
        MsSqlTable = null;
        ElasticIndex = null;
        MongoDbCollection = null;
        Redis = null;
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner publisher builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner publisher builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Publishers" />
    public PublisherBuilder Configure(ISenderConfig config)
    {
        Reset();
        switch (config)
        {
            case RabbitMqSenderConfig rabbitMqSenderConfig:
                RabbitMq = rabbitMqSenderConfig;
                break;
            case SocketSenderConfig socketSenderConfig:
                Socket = socketSenderConfig;
                break;
            case SftpSenderConfig sftpSenderConfig:
                Sftp = sftpSenderConfig;
                break;
            case S3BucketSenderConfig s3SenderConfig:
                S3Bucket = s3SenderConfig;
                break;
            case KafkaTopicSenderConfig kafkaSenderConfig:
                KafkaTopic = kafkaSenderConfig;
                break;
            case OracleSenderConfig oracleSenderConfig:
                OracleSqlTable = oracleSenderConfig;
                break;
            case PostgreSqlSenderConfig postgreSqlSenderConfig:
                PostgreSqlTable = postgreSqlSenderConfig;
                break;
            case MsSqlSenderConfig mssqlSenderConfig:
                MsSqlTable = mssqlSenderConfig;
                break;
            case ElasticSenderConfig elasticSenderConfig:
                ElasticIndex = elasticSenderConfig;
                break;
            case MongoDbCollectionSenderConfig mongoDbCollectionSenderConfig:
                MongoDbCollection = mongoDbCollectionSenderConfig;
                break;
            case RedisSenderConfig redisSenderConfig:
                Redis = redisSenderConfig;
                break;
        }

        return this;
    }

    /// <summary>
    /// Builds a concrete publisher action from the configured sender/chunk sender pair.
    /// Any construction failure is written to <paramref name="actionFailures"/> and returns null.
    /// </summary>
    internal BasePublisher? Build(InternalContext context, IList<ActionFailure> actionFailures, string sessionName)
    {
        return BuildWithTimeZone(context, actionFailures, sessionName, TimeZoneInfoResolver.DefaultTimeZoneId);
    }

    /// <summary>
    /// Builds a concrete publisher action from the configured sender/chunk sender pair.
    /// Any construction failure is written to <paramref name="actionFailures"/> and returns null.
    /// </summary>
    internal BasePublisher? BuildWithTimeZone(
        InternalContext context,
        IList<ActionFailure> actionFailures,
        string sessionName,
        string timeZoneId)
    {
        ISenderConfig? type = null;
        try
        {
            var allTypes = new List<ISenderConfig?>
            {
                RabbitMq, KafkaTopic, Socket, Sftp, PostgreSqlTable, OracleSqlTable, MsSqlTable, ElasticIndex, Redis,
                S3Bucket, MongoDbCollection
            };
            if (allTypes.Count(config => config != null) > 1)
            {
                var conflictingConfigs = allTypes
                    .Where(config => config != null)
                    .Select(config => config!.GetType().Name)
                    .ToArray();
                throw new InvalidOperationException(
                    $"Multiple configurations provided for Publisher '{Name}': {string.Join(", ", conflictingConfigs)}. " +
                    "Only one type is allowed at a time.");
            }

            type = allTypes.FirstOrDefault(configuredType => configuredType != null) ??
                   throw new InvalidOperationException($"Missing supported type in publisher {Name}");
            var senderChunkMode = ProtocolChunkSupport.ResolveSenderMode(type);
            var propertyName = ProtocolChunkSupport.GetSenderConfigurationPropertyName(type);
            if (Chunk == null && senderChunkMode == ProtocolChunkMode.ChunkOnly)
            {
                throw new InvalidOperationException(
                    $"The {nameof(Chunk)} field is required when {propertyName} is configured.");
            }

            if (Chunk != null && senderChunkMode == ProtocolChunkMode.SingleOnly)
            {
                throw new InvalidOperationException(
                    $"The {nameof(Chunk)} field must be empty when {propertyName} is configured.");
            }
            
            var overrideRequest = new PublisherOverrideRequest(
                Name!,
                type,
                Chunk != null,
                context.Logger,
                DataFilter,
                timeZoneId);
            var (sender, chunkSender) = context.GetSessionActionOverrides()?.Publisher?.Invoke(overrideRequest)
                                       ?? ProtocolFactoryCompatibility.CreateSender(
                                           Chunk != null,
                                           type,
                                           context.Logger,
                                           DataFilter,
                                           timeZoneId);
            var publisherTypeName = sender?.GetType().Name ?? chunkSender?.GetType().Name ?? "Unknown";
            
            context.Logger.LogDebugWithMetaData("Started building Publisher of type {type}",
                context.GetMetaDataOrDefault(), new object?[] { publisherTypeName });

            return sender != null
                ? new Publisher(Name!, sender, Stage, DataFilter, PolicyBuilder.BuildPolicies(Policies), Loop,
                    Parallel?.Parallelism, Iterations, SleepTimeMs, Serialize?.Serializer, DataSourcePatterns,
                    DataSourceNames, context.Logger)
                : chunkSender != null
                    ? new ChunkPublisher(Name!, chunkSender, Stage, DataFilter, PolicyBuilder.BuildPolicies(Policies),
                        Parallel?.Parallelism, Chunk!.ChunkSize!.Value, Loop, Iterations, SleepTimeMs,
                        Serialize?.Serializer, DataSourcePatterns, DataSourceNames, context.Logger)
                    : null;
        }
        catch (Exception e)
        {
            actionFailures.AppendActionFailure(e, sessionName, context.Logger, nameof(Publisher), Name!,
                type?.GetType().Name);
        }

        return null;
    }

    private ISenderConfig? GetConfiguration()
    {
        if (RabbitMq != null) return RabbitMq;
        if (KafkaTopic != null) return KafkaTopic;
        if (Socket != null) return Socket;
        if (Sftp != null) return Sftp;
        if (PostgreSqlTable != null) return PostgreSqlTable;
        if (OracleSqlTable != null) return OracleSqlTable;
        if (MsSqlTable != null) return MsSqlTable;
        if (ElasticIndex != null) return ElasticIndex;
        if (Redis != null) return Redis;
        if (S3Bucket != null) return S3Bucket;
        return MongoDbCollection;
    }
}
