using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using QaaS.Framework.Configurations.CustomValidationAttributes;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.ConfigurationObjects.Elastic;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.Protocols.ConfigurationObjects.MongoDb;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Redis;
using QaaS.Framework.Protocols.ConfigurationObjects.S3;
using QaaS.Framework.Protocols.ConfigurationObjects.Sftp;
using QaaS.Framework.Protocols.ConfigurationObjects.Socket;
using QaaS.Framework.Protocols.ConfigurationObjects.Sql;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.ConfigurationObjects;
using Parallel = QaaS.Runner.Sessions.ConfigurationObjects.Parallel;

[assembly: InternalsVisibleTo("QaaS.Runner")]

namespace QaaS.Runner.Sessions.Actions.Publishers.Builders;

public partial class PublisherBuilder
{
    [Required]
    [Description("The name of the publisher")]
    public string? Name { get; internal set; }

    [RequiredIfAny(nameof(DataSourcePatterns), [null])]
    [Description(
        "The name of the data sources to publish the data of" +
        " in the order their data will be published")]
    public string[]? DataSourceNames { get; internal set; }
    [RequiredIfAny(nameof(DataSourceNames), [null])]
    [Description("Patterns of the names of data sources to publish the data of off")]
    public string[]? DataSourcePatterns { get; internal set; }
    [Description("How to filter the properties of each returned published data")]
    public DataFilter DataFilter { get; internal set; } = new();
    [Description("How much iterations of the publishing action to execute")]
    [DefaultValue(1)]
    [Range(1, int.MaxValue)]
    public int Iterations { get; internal set; } = 1;
    [Description("Whether to publish in loop")]
    [DefaultValue(false)]
    public bool Loop { get; internal set; } = false;
    [Range(ulong.MinValue, ulong.MaxValue),
     Description("The time to sleep in milliseconds in between iterations"), DefaultValue(0)]
    public ulong SleepTimeMs { get; internal set; } = 0;
    [Description("Whether to publish in a specified parallelism")]
    public Parallel? Parallel { get; internal set; }
    [Description("Determines whether the publisher acts as a chunk publisher")]
    public Chunks? Chunk { get; internal set; }
    [Description("The stage in which the Publisher runs at")]
    [DefaultValue((int)OrderedActions.Publishers)]
    public int Stage { get; internal set; } = (int)OrderedActions.Publishers;
    [Description("List of policies to use when communicating with this action's protocol")]
    public PolicyBuilder[] Policies { get; internal set; } = [];
    [Description("Publishes messages to a rabbitmq")]
    public RabbitMqSenderConfig? RabbitMq { get; internal set; }
    [Description("Publishes messages to a kafka topic")]
    public KafkaTopicSenderConfig? KafkaTopic { get; internal set; }
    [Description("Publishes messages to a redis cache")]
    public RedisSenderConfig? Redis { get; internal set; }
    [Description("Publishes messages to a postgresql database table")]
    public PostgreSqlSenderConfig? PostgreSqlTable { get; internal set; }
    [Description("Publishes messages to an oracle sql database table")]
    public OracleSenderConfig? OracleSqlTable { get; internal set; }
    [Description("Publishes messages to an S3 bucket")]
    public S3BucketSenderConfig? S3Bucket { get; internal set; }
    [Description("Publishes messages from a socket to an endpoint")]
    public SocketSenderConfig? Socket { get; internal set; }
    [Description("Publishes documents to an elastic index")]
    public ElasticSenderConfig? ElasticIndex { get; internal set; }
    [Description("Publishes messages to an mssql database table")]
    public MsSqlSenderConfig? MsSqlTable { get; internal set; }
    [Description("Publishes messages to an MongoDb collection")]
    public MongoDbCollectionSenderConfig? MongoDbCollection { get; internal set; }
    [Description("Publishes files to a remote machine using SFTP")]
    public SftpSenderConfig? Sftp { get; internal set; }
    [Description("The serializer to use to serialize the data to publish")]
    [DefaultValue(null)]
    public SerializeConfig? Serialize { get; internal set; }
}
