using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.ConfigurationObjects.Elastic;
using QaaS.Framework.Protocols.ConfigurationObjects.IbmMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.S3;
using QaaS.Framework.Protocols.ConfigurationObjects.Socket;
using QaaS.Framework.Protocols.ConfigurationObjects.Sql;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.ConfigurationObjects;

namespace QaaS.Runner.Sessions.Actions.Consumers.Builders;

public partial class ConsumerBuilder
{
    [Required]
    [Description("The name of the consumer")]
    public string? Name { get; internal set; }

    [Required]
    [Description("The consumption timeout in milliseconds" +
                 " (timeout is the time since last message was read by the consumer)")]
    public int? TimeoutMs { get; internal set; }

    [Required]
    [Description("The initial consumption timeout in milliseconds" +
                 " (timeout is the time before any message has been read by the consumer)")]
    public int? InitialTimeoutMs { get; internal set; } = null;

    [Description("How to filter the properties of each returned consumed data")]
    public DataFilter DataFilter { get; internal set; } = new();

    [Description("The stage in which the Consumer runs at")]
    [DefaultValue((int)OrderedActions.Consumers)]
    public int Stage { get; internal set; } = (int)OrderedActions.Consumers;

    [Description("List of policies to use when communicating with this action's protocol")]
    public PolicyBuilder[] Policies { get; internal set; } = [];

    [Description("Consumes messages from a rabbitmq")]
    public RabbitMqReaderConfig? RabbitMq { get; internal set; }

    [Description("Consumes messages from a kafka topic")]
    public KafkaTopicReaderConfig? KafkaTopic { get; internal set; }

    [Description("Consume messages from an mssql database table")]
    public MsSqlReaderConfig? MsSqlTable { get; internal set; }

    [Description("Consume messages from an oracle sql database table")]
    public OracleReaderConfig? OracleSqlTable { get; internal set; }

    [Description("Consume messages from a trino sql database table")]
    public TrinoReaderConfig? TrinoSqlTable { get; internal set; }

    [Description("Consume messages from an postgresql database table")]
    public PostgreSqlReaderConfig? PostgreSqlTable { get; internal set; }

    [Description("Consumes messages from an s3 bucket")]
    public S3BucketReaderConfig? S3Bucket { get; internal set; }

    [Description("Consumes documents from elasticsearch indices by an index pattern")]
    public ElasticReaderConfig? ElasticIndices { get; internal set; }

    [Description("Consumes messages from socket communications in various protocols")]
    public SocketReaderConfig? Socket { get; internal set; }

    [Description("Consumes messages from IbmMq queue")]
    public IbmMqReaderConfig? IbmMqQueue { get; internal set; }

    [DefaultValue(null)]
    [Description("Serializer to use to deserialize the consumed data")]
    public DeserializeConfig? Deserialize { get; internal set; }
}