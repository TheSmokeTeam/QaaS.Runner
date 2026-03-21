using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Elastic;
using QaaS.Framework.Protocols.ConfigurationObjects.IbmMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.Protocols.ConfigurationObjects.MongoDb;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Redis;
using QaaS.Framework.Protocols.ConfigurationObjects.S3;
using QaaS.Framework.Protocols.ConfigurationObjects.Sftp;
using QaaS.Framework.Protocols.ConfigurationObjects.Socket;
using QaaS.Framework.Protocols.ConfigurationObjects.Sql;

namespace QaaS.Runner.Sessions.Actions;

/// <summary>
/// Describes how a protocol can exchange data with runner actions.
/// </summary>
internal enum ProtocolChunkMode
{
    /// <summary>
    /// The protocol supports only single-message communication.
    /// </summary>
    SingleOnly,

    /// <summary>
    /// The protocol supports only chunk-based communication.
    /// </summary>
    ChunkOnly,

    /// <summary>
    /// The protocol supports both single-message and chunk-based communication.
    /// </summary>
    SingleOrChunk
}

/// <summary>
/// Centralizes protocol chunk capabilities so validation and runtime action selection stay aligned.
/// </summary>
internal static class ProtocolChunkSupport
{
    private static readonly IReadOnlyDictionary<Type, (ProtocolChunkMode Mode, string PropertyName)> SenderModes =
        new Dictionary<Type, (ProtocolChunkMode Mode, string PropertyName)>
        {
            [typeof(RabbitMqSenderConfig)] = (ProtocolChunkMode.SingleOnly, "RabbitMq"),
            [typeof(KafkaTopicSenderConfig)] = (ProtocolChunkMode.SingleOnly, "KafkaTopic"),
            [typeof(SftpSenderConfig)] = (ProtocolChunkMode.SingleOnly, "Sftp"),
            [typeof(SocketSenderConfig)] = (ProtocolChunkMode.SingleOnly, "Socket"),
            [typeof(S3BucketSenderConfig)] = (ProtocolChunkMode.SingleOnly, "S3Bucket"),
            [typeof(ElasticSenderConfig)] = (ProtocolChunkMode.ChunkOnly, "ElasticIndex"),
            [typeof(MongoDbCollectionSenderConfig)] = (ProtocolChunkMode.ChunkOnly, "MongoDbCollection"),
            [typeof(OracleSenderConfig)] = (ProtocolChunkMode.ChunkOnly, "OracleSqlTable"),
            [typeof(MsSqlSenderConfig)] = (ProtocolChunkMode.ChunkOnly, "MsSqlTable"),
            [typeof(RedisSenderConfig)] = (ProtocolChunkMode.ChunkOnly, "Redis"),
            [typeof(PostgreSqlSenderConfig)] = (ProtocolChunkMode.SingleOrChunk, "PostgreSqlTable")
        };

    private static readonly IReadOnlyDictionary<Type, (ProtocolChunkMode Mode, string PropertyName)> ReaderModes =
        new Dictionary<Type, (ProtocolChunkMode Mode, string PropertyName)>
        {
            [typeof(RabbitMqReaderConfig)] = (ProtocolChunkMode.SingleOnly, "RabbitMq"),
            [typeof(KafkaTopicReaderConfig)] = (ProtocolChunkMode.SingleOnly, "KafkaTopic"),
            [typeof(SocketReaderConfig)] = (ProtocolChunkMode.SingleOnly, "Socket"),
            [typeof(IbmMqReaderConfig)] = (ProtocolChunkMode.SingleOnly, "IbmMqQueue"),
            [typeof(PostgreSqlReaderConfig)] = (ProtocolChunkMode.ChunkOnly, "PostgreSqlTable"),
            [typeof(OracleReaderConfig)] = (ProtocolChunkMode.ChunkOnly, "OracleSqlTable"),
            [typeof(MsSqlReaderConfig)] = (ProtocolChunkMode.ChunkOnly, "MsSqlTable"),
            [typeof(TrinoReaderConfig)] = (ProtocolChunkMode.ChunkOnly, "TrinoSqlTable"),
            [typeof(ElasticReaderConfig)] = (ProtocolChunkMode.ChunkOnly, "ElasticIndices"),
            [typeof(S3BucketReaderConfig)] = (ProtocolChunkMode.ChunkOnly, "S3Bucket")
        };

    /// <summary>
    /// Resolves the sender mode for the configured publisher protocol.
    /// </summary>
    public static ProtocolChunkMode ResolveSenderMode(ISenderConfig configuration)
    {
        return ResolveSupport(configuration, SenderModes, "sender").Mode;
    }

    /// <summary>
    /// Returns the publisher builder property name that owns the configured sender protocol.
    /// </summary>
    public static string GetSenderConfigurationPropertyName(ISenderConfig configuration)
    {
        return ResolveSupport(configuration, SenderModes, "sender").PropertyName;
    }

    /// <summary>
    /// Resolves the reader mode for the configured consumer protocol.
    /// </summary>
    public static ProtocolChunkMode ResolveReaderMode(IReaderConfig configuration)
    {
        return ResolveSupport(configuration, ReaderModes, "reader").Mode;
    }

    /// <summary>
    /// Returns the consumer builder property name that owns the configured reader protocol.
    /// </summary>
    public static string GetReaderConfigurationPropertyName(IReaderConfig configuration)
    {
        return ResolveSupport(configuration, ReaderModes, "reader").PropertyName;
    }

    /// <summary>
    /// Resolves chunk support metadata for a configured protocol object.
    /// </summary>
    private static (ProtocolChunkMode Mode, string PropertyName) ResolveSupport<TConfiguration>(
        TConfiguration configuration,
        IReadOnlyDictionary<Type, (ProtocolChunkMode Mode, string PropertyName)> supportedModes,
        string protocolRole)
        where TConfiguration : class
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configurationType = configuration.GetType();
        foreach (var supportedMode in supportedModes)
        {
            if (supportedMode.Key.IsAssignableFrom(configurationType))
            {
                return supportedMode.Value;
            }
        }

        throw new InvalidOperationException(
            $"Protocol type {configurationType.Name} is not supported for {protocolRole} chunk validation.");
    }
}
