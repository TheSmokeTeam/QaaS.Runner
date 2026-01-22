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
    internal int? TimeoutMs { get; set; }

    [Description("How to filter the properties of each returned consumed data")]
    internal DataFilter DataFilter { get; set; } = new();

    [Description("The stage in which the Consumer runs at")]
    [DefaultValue((int)OrderedActions.Consumers)]
    internal int Stage { get; set; } = (int)OrderedActions.Consumers;

    [Description("List of policies to use when communicating with this action's protocol")]
    public PolicyBuilder[] Policies { get; set; } = [];

    [Description("Consumes messages from a rabbitmq")]
    internal RabbitMqReaderConfig? RabbitMq { get; set; }

    [Description("Consumes messages from a kafka topic")]
    internal KafkaTopicReaderConfig? KafkaTopic { get; set; }

    [Description("Consume messages from an mssql database table")]
    internal MsSqlReaderConfig? MsSqlTable { get; set; }

    [Description("Consume messages from an oracle sql database table")]
    internal OracleReaderConfig? OracleSqlTable { get; set; }

    [Description("Consume messages from a trino sql database table")]
    internal TrinoReaderConfig? TrinoSqlTable { get; set; }

    [Description("Consume messages from an postgresql database table")]
    internal PostgreSqlReaderConfig? PostgreSqlTable { get; set; }

    [Description("Consumes messages from an s3 bucket")]
    internal S3BucketReaderConfig? S3Bucket { get; set; }

    [Description("Consumes documents from elasticsearch indices by an index pattern")]
    internal ElasticReaderConfig? ElasticIndices { get; set; }

    [Description("Consumes messages from socket communications in various protocols")]
    internal SocketReaderConfig? Socket { get; set; }

    [Description("Consumes messages from IbmMq queue")]
    internal IbmMqReaderConfig? IbmMqQueue { get; set; }

    [DefaultValue(null)]
    [Description("Serializer to use to deserialize the consumed data")]
    internal DeserializeConfig? Deserialize { get; set; }
}