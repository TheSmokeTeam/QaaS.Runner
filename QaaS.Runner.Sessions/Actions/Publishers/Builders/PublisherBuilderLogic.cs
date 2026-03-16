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
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.RuntimeOverrides;
using Parallel = QaaS.Runner.Sessions.ConfigurationObjects.Parallel;

namespace QaaS.Runner.Sessions.Actions.Publishers.Builders;

public partial class PublisherBuilder
{
    public PublisherBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    public PublisherBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    public PublisherBuilder FilterData(DataFilter dataFilter)
    {
        DataFilter = dataFilter;
        return this;
    }

    public PublisherBuilder WithSerializer(SerializeConfig serializeConfig)
    {
        Serialize = serializeConfig;
        return this;
    }

    public PublisherBuilder WithIterations(int iterations)
    {
        Iterations = iterations;
        return this;
    }

    public PublisherBuilder AddDataSource(string dataSourceName)
    {
        var dataSourceNamesList = DataSourceNames?.ToList() ?? [];
        dataSourceNamesList.Add(dataSourceName);
        DataSourceNames = dataSourceNamesList.ToArray();
        return this;
    }

    public PublisherBuilder CreateDataSource(string dataSourceName)
    {
        return AddDataSource(dataSourceName);
    }

    public IReadOnlyList<string> ReadDataSources()
    {
        return DataSourceNames ?? [];
    }

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

    public PublisherBuilder DeleteDataSource(string dataSourceName)
    {
        DataSourceNames = DataSourceNames?.Where(value => value != dataSourceName).ToArray();
        return this;
    }

    public PublisherBuilder AddDataSourcePattern(string dataSourcePattern)
    {
        var dataSourcePatternsList = DataSourcePatterns?.ToList() ?? [];
        dataSourcePatternsList.Add(dataSourcePattern);
        DataSourcePatterns = dataSourcePatternsList.ToArray();
        return this;
    }

    public PublisherBuilder CreateDataSourcePattern(string dataSourcePattern)
    {
        return AddDataSourcePattern(dataSourcePattern);
    }

    public IReadOnlyList<string> ReadDataSourcePatterns()
    {
        return DataSourcePatterns ?? [];
    }

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

    public PublisherBuilder DeleteDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = DataSourcePatterns?.Where(value => value != dataSourcePattern).ToArray();
        return this;
    }

    public PublisherBuilder InLoops()
    {
        Loop = true;
        return this;
    }

    public PublisherBuilder WithSleep(ulong sleepTimeMs)
    {
        SleepTimeMs = sleepTimeMs;
        return this;
    }

    public PublisherBuilder WithChunks(Chunks chunks)
    {
        Chunk = chunks;
        return this;
    }

    public PublisherBuilder AddPolicy(PolicyBuilder policy)
    {
        var policiesList = Policies.ToList();
        policiesList.Add(policy);
        Policies = policiesList.ToArray();
        return this;
    }

    public PublisherBuilder CreatePolicy(PolicyBuilder policy)
    {
        return AddPolicy(policy);
    }

    public IReadOnlyList<PolicyBuilder> ReadPolicies()
    {
        return Policies;
    }

    public PublisherBuilder UpdatePolicyAt(int index, PolicyBuilder policy)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies[index] = policy;
        return this;
    }

    public PublisherBuilder DeletePolicyAt(int index)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies = Policies.Where((_, i) => i != index).ToArray();
        return this;
    }

    public PublisherBuilder WithParallelism(int parallelism)
    {
        Parallel = new Parallel { Parallelism = parallelism };
        return this;
    }

    public PublisherBuilder CreateConfiguration(ISenderConfig config)
    {
        return Configure(config);
    }

    public ISenderConfig? ReadConfiguration()
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

    /// <summary>
    /// Applies a computed partial update to the current publisher configuration while preserving omitted fields.
    /// </summary>
    public PublisherBuilder UpdateConfiguration(Func<ISenderConfig, ISenderConfig> update)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Publisher configuration is not set");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the publisher configuration by merging same-type values and replacing the current type when needed.
    /// </summary>
    public PublisherBuilder UpdateConfiguration(ISenderConfig config)
    {
        return Configure(ReadConfiguration().UpdateConfiguration(config));
    }

    public PublisherBuilder DeleteConfiguration()
    {
        return Reset();
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
            
            var factoryRequest = new PublisherFactoryRequest(Name!, type, Chunk != null, context.Logger, DataFilter);
            var (sender, chunkSender) = context.GetSessionActionFactoryOverrides()?.PublisherFactory
                                           ?.Invoke(factoryRequest)
                                       ?? SenderFactory.CreateSender(Chunk != null, type, context.Logger, DataFilter);
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
}
