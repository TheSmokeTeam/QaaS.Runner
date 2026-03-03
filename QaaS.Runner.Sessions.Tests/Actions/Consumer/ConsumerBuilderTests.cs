using System.Collections.Generic;
using System.Net.Sockets;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Elastic;
using QaaS.Framework.Protocols.ConfigurationObjects.IbmMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.S3;
using QaaS.Framework.Protocols.ConfigurationObjects.Socket;
using QaaS.Framework.Protocols.ConfigurationObjects.Sql;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;

namespace QaaS.Runner.Sessions.Tests.Actions.Consumer;

[TestFixture]
public class ConsumerBuilderTests
{
    private IList<ActionFailure> _actionFailures = null!;
    private string _sessionName = null!;

    [SetUp]
    public void SetUp()
    {
        _actionFailures = new List<ActionFailure>();
        _sessionName = "TestSession";
    }

    [Test]
    public void Named_Should_Set_Name()
    {
        // Arrange
        var builder = new ConsumerBuilder();

        // Act
        builder.Named("TestConsumer");

        // Assert
        Assert.That(builder.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void AtStage_Should_Set_Stage()
    {
        // Arrange
        var builder = new ConsumerBuilder();

        // Act
        builder.AtStage(5);

        // Assert
        Assert.That(builder.Stage, Is.EqualTo(5));
    }

    [Test]
    public void WithTimeout_Should_Set_TimeoutMs()
    {
        // Arrange
        var builder = new ConsumerBuilder();

        // Act
        builder.WithTimeout(5000);

        // Assert
        Assert.That(builder.TimeoutMs, Is.EqualTo(5000));
    }

    [Test]
    public void FilterData_Should_Set_DataFilter()
    {
        // Arrange
        var filter = new DataFilter();
        var builder = new ConsumerBuilder();

        // Act
        builder.FilterData(filter);

        // Assert
        Assert.That(builder.DataFilter, Is.SameAs(filter));
    }

    [Test]
    public void WithDeserializer_Should_Set_Deserialize()
    {
        // Arrange
        var config = new DeserializeConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.WithDeserializer(config);

        // Assert
        Assert.That(builder.Deserialize, Is.SameAs(config));
    }

    [Test]
    public void AddPolicy_Should_Add_Policy()
    {
        // Arrange
        var policy = new PolicyBuilder();
        var builder = new ConsumerBuilder();

        // Act
        builder.AddPolicy(policy);

        // Assert
        Assert.That(builder.Policies, Has.Length.EqualTo(1));
        Assert.That(builder.Policies[0], Is.SameAs(policy));
    }

    [Test]
    public void AddPolicy_Should_Add_Multiple_Policies()
    {
        // Arrange
        var policy1 = new PolicyBuilder();
        var policy2 = new PolicyBuilder();
        var builder = new ConsumerBuilder();

        // Act
        builder.AddPolicy(policy1);
        builder.AddPolicy(policy2);

        // Assert
        Assert.That(builder.Policies, Has.Length.EqualTo(2));
        Assert.That(builder.Policies[0], Is.SameAs(policy1));
        Assert.That(builder.Policies[1], Is.SameAs(policy2));
    }

    [Test]
    public void Configure_With_RabbitMqReaderConfig_Should_Set_RabbitMq()
    {
        // Arrange
        var config = new RabbitMqReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.RabbitMq, Is.SameAs(config));
        Assert.That(builder.KafkaTopic, Is.Null);
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.IbmMqQueue, Is.Null);
        Assert.That(builder.PostgreSqlTable, Is.Null);
        Assert.That(builder.OracleSqlTable, Is.Null);
        Assert.That(builder.MsSqlTable, Is.Null);
        Assert.That(builder.TrinoSqlTable, Is.Null);
        Assert.That(builder.ElasticIndices, Is.Null);
        Assert.That(builder.S3Bucket, Is.Null);
    }

    [Test]
    public void Configure_With_KafkaTopicReaderConfig_Should_Set_KafkaTopic()
    {
        // Arrange
        var config = new KafkaTopicReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.KafkaTopic, Is.SameAs(config));
        Assert.That(builder.RabbitMq, Is.Null);
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.IbmMqQueue, Is.Null);
        Assert.That(builder.PostgreSqlTable, Is.Null);
        Assert.That(builder.OracleSqlTable, Is.Null);
        Assert.That(builder.MsSqlTable, Is.Null);
        Assert.That(builder.TrinoSqlTable, Is.Null);
        Assert.That(builder.ElasticIndices, Is.Null);
        Assert.That(builder.S3Bucket, Is.Null);
    }

    [Test]
    public void Configure_With_SocketReaderConfig_Should_Set_Socket()
    {
        // Arrange
        var config = new SocketReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.Socket, Is.SameAs(config));
        Assert.That(builder.RabbitMq, Is.Null);
        Assert.That(builder.KafkaTopic, Is.Null);
        Assert.That(builder.IbmMqQueue, Is.Null);
        Assert.That(builder.PostgreSqlTable, Is.Null);
        Assert.That(builder.OracleSqlTable, Is.Null);
        Assert.That(builder.MsSqlTable, Is.Null);
        Assert.That(builder.TrinoSqlTable, Is.Null);
        Assert.That(builder.ElasticIndices, Is.Null);
        Assert.That(builder.S3Bucket, Is.Null);
    }

    [Test]
    public void Configure_With_IbmMqReaderConfig_Should_Set_IbmMqQueue()
    {
        // Arrange
        var config = new IbmMqReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.IbmMqQueue, Is.SameAs(config));
        Assert.That(builder.RabbitMq, Is.Null);
        Assert.That(builder.KafkaTopic, Is.Null);
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.PostgreSqlTable, Is.Null);
        Assert.That(builder.OracleSqlTable, Is.Null);
        Assert.That(builder.MsSqlTable, Is.Null);
        Assert.That(builder.TrinoSqlTable, Is.Null);
        Assert.That(builder.ElasticIndices, Is.Null);
        Assert.That(builder.S3Bucket, Is.Null);
    }

    [Test]
    public void Configure_With_PostgreSqlReaderConfig_Should_Set_PostgreSqlTable()
    {
        // Arrange
        var config = new PostgreSqlReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.PostgreSqlTable, Is.SameAs(config));
        Assert.That(builder.RabbitMq, Is.Null);
        Assert.That(builder.KafkaTopic, Is.Null);
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.IbmMqQueue, Is.Null);
        Assert.That(builder.OracleSqlTable, Is.Null);
        Assert.That(builder.MsSqlTable, Is.Null);
        Assert.That(builder.TrinoSqlTable, Is.Null);
        Assert.That(builder.ElasticIndices, Is.Null);
        Assert.That(builder.S3Bucket, Is.Null);
    }

    [Test]
    public void Configure_With_OracleReaderConfig_Should_Set_OracleSqlTable()
    {
        // Arrange
        var config = new OracleReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.OracleSqlTable, Is.SameAs(config));
        Assert.That(builder.RabbitMq, Is.Null);
        Assert.That(builder.KafkaTopic, Is.Null);
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.IbmMqQueue, Is.Null);
        Assert.That(builder.PostgreSqlTable, Is.Null);
        Assert.That(builder.MsSqlTable, Is.Null);
        Assert.That(builder.TrinoSqlTable, Is.Null);
        Assert.That(builder.ElasticIndices, Is.Null);
        Assert.That(builder.S3Bucket, Is.Null);
    }

    [Test]
    public void Configure_With_MsSqlReaderConfig_Should_Set_MsSqlTable()
    {
        // Arrange
        var config = new MsSqlReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.MsSqlTable, Is.SameAs(config));
        Assert.That(builder.RabbitMq, Is.Null);
        Assert.That(builder.KafkaTopic, Is.Null);
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.IbmMqQueue, Is.Null);
        Assert.That(builder.PostgreSqlTable, Is.Null);
        Assert.That(builder.OracleSqlTable, Is.Null);
        Assert.That(builder.TrinoSqlTable, Is.Null);
        Assert.That(builder.ElasticIndices, Is.Null);
        Assert.That(builder.S3Bucket, Is.Null);
    }

    [Test]
    public void Configure_With_TrinoReaderConfig_Should_Set_TrinoSqlTable()
    {
        // Arrange
        var config = new TrinoReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.TrinoSqlTable, Is.SameAs(config));
        Assert.That(builder.RabbitMq, Is.Null);
        Assert.That(builder.KafkaTopic, Is.Null);
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.IbmMqQueue, Is.Null);
        Assert.That(builder.PostgreSqlTable, Is.Null);
        Assert.That(builder.OracleSqlTable, Is.Null);
        Assert.That(builder.MsSqlTable, Is.Null);
        Assert.That(builder.ElasticIndices, Is.Null);
        Assert.That(builder.S3Bucket, Is.Null);
    }

    [Test]
    public void Configure_With_ElasticReaderConfig_Should_Set_ElasticIndices()
    {
        // Arrange
        var config = new ElasticReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.ElasticIndices, Is.SameAs(config));
        Assert.That(builder.RabbitMq, Is.Null);
        Assert.That(builder.KafkaTopic, Is.Null);
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.IbmMqQueue, Is.Null);
        Assert.That(builder.PostgreSqlTable, Is.Null);
        Assert.That(builder.OracleSqlTable, Is.Null);
        Assert.That(builder.MsSqlTable, Is.Null);
        Assert.That(builder.TrinoSqlTable, Is.Null);
        Assert.That(builder.S3Bucket, Is.Null);
    }

    [Test]
    public void Configure_With_S3BucketReaderConfig_Should_Set_S3Bucket()
    {
        // Arrange
        var config = new S3BucketReaderConfig();
        var builder = new ConsumerBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.S3Bucket, Is.SameAs(config));
        Assert.That(builder.RabbitMq, Is.Null);
        Assert.That(builder.KafkaTopic, Is.Null);
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.IbmMqQueue, Is.Null);
        Assert.That(builder.PostgreSqlTable, Is.Null);
        Assert.That(builder.OracleSqlTable, Is.Null);
        Assert.That(builder.MsSqlTable, Is.Null);
        Assert.That(builder.TrinoSqlTable, Is.Null);
        Assert.That(builder.ElasticIndices, Is.Null);
    }

    [Test]
    public void Build_With_Valid_RabbitMq_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new RabbitMqReaderConfig
        {
            Host = "https://test"
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_With_Valid_KafkaTopic_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new KafkaTopicReaderConfig
        {
            TopicName = "test",
            GroupId = "test",
            HostNames = ["host1:8080", "host2:8081"],
            Username = "test",
            Password = "test"
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures!, _sessionName!);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_With_Valid_Socket_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new SocketReaderConfig
        {
            Host = "https:test",
            Port = 8080,
            ProtocolType = ProtocolType.IP
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_With_Valid_IbmMq_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new IbmMqReaderConfig
        {
            HostName = "https:tests",
            Port = 8080,
            Channel = "test",
            Manager = "test",
            QueueName = "test"
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_With_Valid_PostgreSql_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new PostgreSqlReaderConfig
        {
            ConnectionString = "Host=trino.test.com;Port=8443;",
            TableName = "test"
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_With_Valid_OracleSql_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new OracleReaderConfig
        {
            ConnectionString = "Data Source=OracleSql.test.com;User Id=test;Password=test",
            TableName = "test"
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_With_Valid_MsSql_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new MsSqlReaderConfig
        {
            ConnectionString = "Server=testServer;Database=testDataBase;User Id=test;Password=test;",
            TableName = "test"
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_With_Valid_TrinoSql_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new TrinoReaderConfig
        {
            ConnectionString = "Host=trino.test.com;Port=8443;",
            TableName = "test",
            Username = "test",
            Password = "test",
            ClientTag = "test",
            Schema = "default",
            Catalog = "hive",
            Hostname = "https://trino.test.com"
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_With_Valid_Elastic_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new ElasticReaderConfig
        {
            TimestampField = "log_timestamp",
            ReadBatchSize = 500,
            ScrollContextExpirationMs = 30000,
            ReadFromRunStartTime = true,
            FilterSecondsBeforeRunStartTime = 300,
            IndexPattern = "*-test",
            Url = "http://test",
            Username = "test",
            Password = "123456"
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_With_Valid_S3Bucket_Config_Should_Create_Consumer()
    {
        // Arrange
        var config = new S3BucketReaderConfig
        {
            StorageBucket = "test",
            ServiceURL = "url",
            AccessKey = "test",
            SecretKey = "test"
        };
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    public void Build_Without_Configuration_Should_Throw_Exception()
    {
        // Arrange
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter());

        // Act & Assert
        Assert.That(builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName), Is.Null);
    }

    [Test]
    public void Build_With_Multiple_Configs_Should_Throw_Exception()
    {
        // Arrange
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(new RabbitMqReaderConfig())
            .Configure(new KafkaTopicReaderConfig());

        // Act & Assert
        Assert.That(builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName), Is.Null);
    }

    [Test]
    public void Build_With_Unsupported_Config_Type_Should_Throw_Exception()
    {
        // Arrange
        var mockConfig = new Mock<IReaderConfig>();
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(mockConfig.Object);

        // Act & Assert
        Assert.That(builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName), Is.Null);
    }

    [Test]
    public void Build_When_Exception_Is_Thrown_Should_Log_Failure()
    {
        // Arrange
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter());

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Null);
        Assert.That(_actionFailures.Count, Is.GreaterThan(0));
    }
}
