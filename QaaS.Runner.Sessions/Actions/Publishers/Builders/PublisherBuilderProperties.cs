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
    internal string[]? DataSourceNames { get; set; }

    [RequiredIfAny(nameof(DataSourceNames), [null])]
    [Description("Patterns of the names of data sources to publish the data of off")]
    internal string[]? DataSourcePatterns { get; set; }

    [Description("How to filter the properties of each returned published data")]
    internal DataFilter DataFilter { get; set; } = new();

    [Description("How much iterations of the publishing action to execute")]
    [DefaultValue(1)]
    [Range(1, int.MaxValue)]
    internal int Iterations { get; set; } = 1;

    [Description("Whether to publish in loop")]
    [DefaultValue(false)]
    internal bool Loop { get; set; } = false;

    [Range(ulong.MinValue, ulong.MaxValue),
     Description("The time to sleep in milliseconds in between iterations"), DefaultValue(0)]
    internal ulong SleepTimeMs { get; set; } = 0;

    [Description("Whether to publish in a specified parallelism")]
    internal Parallel? Parallel { get; set; }

    [Description("Determines whether the publisher acts as a chunk publisher")]
    [RequiredOrNullBasedOnOtherFieldsConfiguration(
        [
            nameof(ElasticIndex), nameof(MongoDbCollection), nameof(OracleSqlTable), nameof(MsSqlTable), nameof(Redis),
            nameof(S3Bucket), nameof(Socket), nameof(Sftp), nameof(KafkaTopic), nameof(RabbitMq)
        ],
        true, true, true, true, true, false, false, false, false, false)]
    internal Chunks? Chunk { get; set; }

    [Description("The stage in which the Publisher runs at")]
    [DefaultValue((int)OrderedActions.Publishers)]
    internal int Stage { get; set; } = (int)OrderedActions.Publishers;

    [Description("List of policies to use when communicating with this action's protocol")]
    internal PolicyBuilder[] Policies { get; set; } = [];

    [Description("Publishes messages to a rabbitmq")]
    internal RabbitMqSenderConfig? RabbitMq { get; set; }

    [Description("Publishes messages to a kafka topic")]
    internal KafkaTopicSenderConfig? KafkaTopic { get; set; }

    [Description("Publishes messages to a redis cache")]
    internal RedisSenderConfig? Redis { get; set; }

    [Description("Publishes messages to a postgresql database table")]
    internal PostgreSqlSenderConfig? PostgreSqlTable { get; set; }

    [Description("Publishes messages to an oracle sql database table")]
    internal OracleSenderConfig? OracleSqlTable { get; set; }

    [Description("Publishes messages to an S3 bucket")]
    internal S3BucketSenderConfig? S3Bucket { get; set; }

    [Description("Publishes messages from a socket to an endpoint")]
    internal SocketSenderConfig? Socket { get; set; }

    [Description("Publishes documents to an elastic index")]
    internal ElasticSenderConfig? ElasticIndex { get; set; }

    [Description("Publishes messages to an mssql database table")]
    internal MsSqlSenderConfig? MsSqlTable { get; set; }

    [Description("Publishes messages to an MongoDb collection")]
    internal MongoDbCollectionSenderConfig? MongoDbCollection { get; set; }

    [Description("Publishes files to a remote machine using SFTP")]
    internal SftpSenderConfig? Sftp { get; set; }

    [Description("The serializer to use to serialize the data to publish")]
    [DefaultValue(null)]
    internal SerializeConfig? Serialize { get; set; }
}
