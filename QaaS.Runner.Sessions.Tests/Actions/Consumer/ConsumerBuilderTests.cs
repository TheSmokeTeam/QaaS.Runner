using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using System.Reflection;
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
using ConsumerAction = QaaS.Runner.Sessions.Actions.Consumers.Consumer;
using ChunkConsumerAction = QaaS.Runner.Sessions.Actions.Consumers.ChunkConsumer;

namespace QaaS.Runner.Sessions.Tests.Actions.Consumer;

[TestFixture]
public class ConsumerBuilderTests
{
    private IList<ActionFailure> _actionFailures = null!;
    private string _sessionName = null!;

    private static IEnumerable<TestCaseData> SupportedReaderConfigurations()
    {
        yield return new TestCaseData(new RabbitMqReaderConfig()).SetName("ReadConfiguration_WithRabbitMq_ReturnsRabbitMq");
        yield return new TestCaseData(new KafkaTopicReaderConfig()).SetName("ReadConfiguration_WithKafkaTopic_ReturnsKafkaTopic");
        yield return new TestCaseData(new SocketReaderConfig()).SetName("ReadConfiguration_WithSocket_ReturnsSocket");
        yield return new TestCaseData(new IbmMqReaderConfig()).SetName("ReadConfiguration_WithIbmMq_ReturnsIbmMq");
        yield return new TestCaseData(new PostgreSqlReaderConfig()).SetName("ReadConfiguration_WithPostgreSql_ReturnsPostgreSql");
        yield return new TestCaseData(new OracleReaderConfig()).SetName("ReadConfiguration_WithOracle_ReturnsOracle");
        yield return new TestCaseData(new MsSqlReaderConfig()).SetName("ReadConfiguration_WithMsSql_ReturnsMsSql");
        yield return new TestCaseData(new TrinoReaderConfig()).SetName("ReadConfiguration_WithTrino_ReturnsTrino");
        yield return new TestCaseData(new ElasticReaderConfig()).SetName("ReadConfiguration_WithElastic_ReturnsElastic");
        yield return new TestCaseData(new S3BucketReaderConfig()).SetName("ReadConfiguration_WithS3_ReturnsS3");
    }

    private static IEnumerable<TestCaseData> ReaderConfigurationsWhichCreateSingleConsumers()
    {
        yield return new TestCaseData(
            new RabbitMqReaderConfig { Host = "https://test" },
            typeof(ConsumerAction)).SetName("RabbitMqReader_CreatesSingleConsumer");
        yield return new TestCaseData(
            new KafkaTopicReaderConfig
            {
                TopicName = "test",
                GroupId = "test",
                HostNames = ["host1:8080"],
                Username = "test",
                Password = "test"
            },
            typeof(ConsumerAction)).SetName("KafkaReader_CreatesSingleConsumer");
        yield return new TestCaseData(
            new SocketReaderConfig
            {
                Host = "https:test",
                Port = 8080,
                ProtocolType = ProtocolType.IP
            },
            typeof(ConsumerAction)).SetName("SocketReader_CreatesSingleConsumer");
        yield return new TestCaseData(
            new IbmMqReaderConfig
            {
                HostName = "https:tests",
                Port = 8080,
                Channel = "test",
                Manager = "test",
                QueueName = "test"
            },
            typeof(ConsumerAction)).SetName("IbmMqReader_CreatesSingleConsumer");
    }

    private static IEnumerable<TestCaseData> ReaderConfigurationsWhichCreateChunkConsumers()
    {
        yield return new TestCaseData(
            new PostgreSqlReaderConfig
            {
                ConnectionString = "Host=trino.test.com;Port=8443;",
                TableName = "test"
            },
            typeof(ChunkConsumerAction)).SetName("PostgreSqlReader_CreatesChunkConsumer");
        yield return new TestCaseData(
            new OracleReaderConfig
            {
                ConnectionString = "Data Source=OracleSql.test.com;User Id=test;Password=test",
                TableName = "test"
            },
            typeof(ChunkConsumerAction)).SetName("OracleReader_CreatesChunkConsumer");
        yield return new TestCaseData(
            new MsSqlReaderConfig
            {
                ConnectionString = "Server=testServer;Database=testDataBase;User Id=test;Password=test;",
                TableName = "test"
            },
            typeof(ChunkConsumerAction)).SetName("MsSqlReader_CreatesChunkConsumer");
        yield return new TestCaseData(
            new TrinoReaderConfig
            {
                ConnectionString = "Host=trino.test.com;Port=8443;",
                TableName = "test",
                Username = "test",
                Password = "test",
                ClientTag = "test",
                Schema = "default",
                Catalog = "hive",
                Hostname = "https://trino.test.com"
            },
            typeof(ChunkConsumerAction)).SetName("TrinoReader_CreatesChunkConsumer");
        yield return new TestCaseData(
            new ElasticReaderConfig
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
            },
            typeof(ChunkConsumerAction)).SetName("ElasticReader_CreatesChunkConsumer");
        yield return new TestCaseData(
            new S3BucketReaderConfig
            {
                StorageBucket = "test",
                ServiceURL = "url",
                AccessKey = "test",
                SecretKey = "test"
            },
            typeof(ChunkConsumerAction)).SetName("S3Reader_CreatesChunkConsumer");
    }

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
        builder.CreatePolicy(policy);

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
        builder.CreatePolicy(policy1);
        builder.CreatePolicy(policy2);

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
    [TestCaseSource(nameof(SupportedReaderConfigurations))]
    public void ReadConfiguration_WithConfiguredType_ReturnsConfiguredInstance(IReaderConfig config)
    {
        var builder = new ConsumerBuilder().Configure(config);

        Assert.That(builder.Configuration, Is.SameAs(config));
    }

    [Test]
    public void ReadConfiguration_WithoutConfiguredType_ReturnsNull()
    {
        var builder = new ConsumerBuilder();

        Assert.That(builder.Configuration, Is.Null);
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
    public void Build_With_Valid_KafkaTopic_Config_And_InitialTimeout_Should_Create_Consumer()
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
            .WithInitialTimeout(1000)
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
    public void Build_With_Valid_S3Bucket_Config_And_InitialTimeout_Should_Create_Consumer()
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
            .WithInitialTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestConsumer"));
    }

    [Test]
    [TestCaseSource(nameof(ReaderConfigurationsWhichCreateSingleConsumers))]
    [TestCaseSource(nameof(ReaderConfigurationsWhichCreateChunkConsumers))]
    public void Build_WithSupportedReaderConfig_CreatesExpectedConsumerMode(IReaderConfig config, Type expectedType)
    {
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        Assert.That(result, Is.InstanceOf(expectedType));
    }

    [Test]
    [TestCaseSource(nameof(ReaderConfigurationsWhichCreateSingleConsumers))]
    [TestCaseSource(nameof(ReaderConfigurationsWhichCreateChunkConsumers))]
    public void Validate_WithSupportedReaderConfig_HasNoChunkModeErrors(IReaderConfig config, Type expectedType)
    {
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .AtStage(1)
            .WithTimeout(1000)
            .FilterData(new DataFilter())
            .Configure(config);

        _ = expectedType;
        var validationResults = ((IValidatableObject)builder)
            .Validate(new ValidationContext(builder))
            .ToList();

        Assert.That(
            validationResults.Count(result => result.ErrorMessage!.Contains("chunk", StringComparison.OrdinalIgnoreCase)),
            Is.EqualTo(0));
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

    [Test]
    public void UpdateConfiguration_WithoutExistingConfiguration_ThrowsInvalidOperationException()
    {
        var builder = new ConsumerBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.UpdateConfiguration(config => config));
    }

    [Test]
    public void UpdateConfiguration_WithConfigurationWithoutExistingConfiguration_ThrowsInvalidOperationException()
    {
        var builder = new ConsumerBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.UpdateConfiguration(new RabbitMqReaderConfig()));
    }

    [Test]
    public void UpdatePolicyAt_WithValidIndex_ReplacesPolicy()
    {
        var replacementPolicy = new PolicyBuilder();
        var builder = new ConsumerBuilder()
            .CreatePolicy(new PolicyBuilder())
            .CreatePolicy(new PolicyBuilder());

        builder.UpdatePolicyAt(0, replacementPolicy);

        Assert.That(builder.Policies[0], Is.SameAs(replacementPolicy));
        Assert.That(builder.Policies[1], Is.Not.SameAs(replacementPolicy));
    }

    [Test]
    public void UpdatePolicyAt_WithInvalidIndex_ThrowsArgumentOutOfRangeException()
    {
        var builder = new ConsumerBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UpdatePolicyAt(0, new PolicyBuilder()));
    }

    [Test]
    public void RemovePolicyAt_WithInvalidIndex_ThrowsArgumentOutOfRangeException()
    {
        var builder = new ConsumerBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.RemovePolicyAt(0));
    }

    [Test]
    public void Build_WithMultipleReaderConfigsAndValidRequiredFields_ReturnsNullAndAddsActionFailure()
    {
        var builder = new ConsumerBuilder()
            .Named("TestConsumer")
            .WithTimeout(1000)
            .FilterData(new DataFilter());

        typeof(ConsumerBuilder).GetProperty("RabbitMq", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(builder, new RabbitMqReaderConfig());
        typeof(ConsumerBuilder).GetProperty("KafkaTopic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(builder, new KafkaTopicReaderConfig
            {
                TopicName = "topic",
                GroupId = "group",
                HostNames = ["host1:9092"],
                Username = "user",
                Password = "pass"
            });

        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures, Is.Not.Empty);
    }
}

