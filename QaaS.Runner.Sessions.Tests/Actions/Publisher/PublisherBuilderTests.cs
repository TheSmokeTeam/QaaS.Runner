using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
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
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.ConfigurationObjects;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace QaaS.Runner.Sessions.Tests.Actions.Publisher;

[TestFixture]
public class PublisherBuilderTests
{
    private IList<ActionFailure> _actionFailures = null!;
    private string _sessionName = null!;

    private static IEnumerable<TestCaseData> SupportedSenderConfigurationsForRead()
    {
        yield return new TestCaseData(new RabbitMqSenderConfig()).SetName("ReadConfiguration_WithRabbitMq_ReturnsRabbitMq");
        yield return new TestCaseData(new KafkaTopicSenderConfig()).SetName("ReadConfiguration_WithKafkaTopic_ReturnsKafkaTopic");
        yield return new TestCaseData(new SocketSenderConfig()).SetName("ReadConfiguration_WithSocket_ReturnsSocket");
        yield return new TestCaseData(new SftpSenderConfig()).SetName("ReadConfiguration_WithSftp_ReturnsSftp");
        yield return new TestCaseData(new PostgreSqlSenderConfig()).SetName("ReadConfiguration_WithPostgreSql_ReturnsPostgreSql");
        yield return new TestCaseData(new OracleSenderConfig()).SetName("ReadConfiguration_WithOracle_ReturnsOracle");
        yield return new TestCaseData(new MsSqlSenderConfig()).SetName("ReadConfiguration_WithMsSql_ReturnsMsSql");
        yield return new TestCaseData(new ElasticSenderConfig()).SetName("ReadConfiguration_WithElastic_ReturnsElastic");
        yield return new TestCaseData(new RedisSenderConfig()).SetName("ReadConfiguration_WithRedis_ReturnsRedis");
        yield return new TestCaseData(new S3BucketSenderConfig()).SetName("ReadConfiguration_WithS3_ReturnsS3");
        yield return new TestCaseData(new MongoDbCollectionSenderConfig()).SetName("ReadConfiguration_WithMongo_ReturnsMongo");
    }

    [SetUp]
    public void SetUp()
    {
        _actionFailures = [];
        _sessionName = "TestSession";
    }

    [Test]
    public void Named_Should_Set_Name_Property()
    {
        var builder = new PublisherBuilder();
        builder.Named("TestPublisher");

        Assert.That(builder.Name, Is.EqualTo("TestPublisher"));
    }

    [Test]
    public void AtStage_Should_Set_Stage_Property()
    {
        var builder = new PublisherBuilder();
        builder.AtStage(5);

        Assert.That(builder.Stage, Is.EqualTo(5));
    }

    [Test]
    public void FilterData_Should_Set_DataFilter_Property()
    {
        var filter = new DataFilter();
        var builder = new PublisherBuilder();
        builder.FilterData(filter);

        Assert.That(builder.DataFilter, Is.SameAs(filter));
    }

    [Test]
    public void WithSerializer_Should_Set_Serialize_Property()
    {
        var config = new SerializeConfig();
        var builder = new PublisherBuilder();
        builder.WithSerializer(config);

        Assert.That(builder.Serialize, Is.SameAs(config));
    }

    [Test]
    public void WithIterations_Should_Set_Iterations_Property()
    {
        var builder = new PublisherBuilder();
        builder.WithIterations(10);

        Assert.That(builder.Iterations, Is.EqualTo(10));
    }

    [Test]
    public void AddDataSource_Should_Add_To_DataSourceNames()
    {
        var builder = new PublisherBuilder();
        builder.AddDataSource("source1");
        builder.AddDataSource("source2");

        Assert.That(builder.DataSourceNames, Is.EquivalentTo(new[] { "source1", "source2" }));
    }

    [Test]
    public void AddDataSourcePattern_Should_Add_To_DataSourcePatterns()
    {
        var builder = new PublisherBuilder();
        builder.AddDataSourcePattern("pattern1");
        builder.AddDataSourcePattern("pattern2");

        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(new[] { "pattern1", "pattern2" }));
    }

    [Test]
    public void UpdateAndDeleteDataSourcePattern_ShouldApplyExpectedMutations()
    {
        var builder = new PublisherBuilder()
            .AddDataSourcePattern("pattern1")
            .AddDataSourcePattern("pattern2");

        builder.UpdateDataSourcePattern("pattern1", "pattern1-updated");
        builder.DeleteDataSourcePattern("pattern2");

        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(new[] { "pattern1-updated" }));
    }

    [Test]
    public void UpdateDataSource_WhenCollectionIsNull_DoesNothing()
    {
        var builder = new PublisherBuilder();

        Assert.DoesNotThrow(() => builder.UpdateDataSource("missing", "new-value"));
        Assert.That(builder.ReadDataSources(), Is.Empty);
    }

    [Test]
    public void UpdateDataSourcePattern_WhenCollectionIsNull_DoesNothing()
    {
        var builder = new PublisherBuilder();

        Assert.DoesNotThrow(() => builder.UpdateDataSourcePattern("missing", "new-value"));
        Assert.That(builder.ReadDataSourcePatterns(), Is.Empty);
    }

    [Test]
    public void DeleteDataSource_WhenCollectionIsNull_DoesNothing()
    {
        var builder = new PublisherBuilder();

        Assert.DoesNotThrow(() => builder.DeleteDataSource("missing"));
        Assert.That(builder.ReadDataSources(), Is.Empty);
    }

    [Test]
    public void DeleteDataSourcePattern_WhenCollectionIsNull_DoesNothing()
    {
        var builder = new PublisherBuilder();

        Assert.DoesNotThrow(() => builder.DeleteDataSourcePattern("missing"));
        Assert.That(builder.ReadDataSourcePatterns(), Is.Empty);
    }

    [Test]
    public void InLoops_Should_Set_Loop_Property()
    {
        var builder = new PublisherBuilder();
        builder.InLoops();

        Assert.That(builder.Loop, Is.True);
    }

    [Test]
    public void WithSleep_Should_Set_SleepTimeMs_Property()
    {
        var builder = new PublisherBuilder();
        builder.WithSleep(1000UL);

        Assert.That(builder.SleepTimeMs, Is.EqualTo(1000UL));
    }

    [Test]
    public void WithChunks_Should_Set_Chunk_Property()
    {
        var chunks = new Chunks { ChunkSize = 1024 };
        var builder = new PublisherBuilder();
        builder.WithChunks(chunks);

        Assert.That(builder.Chunk, Is.SameAs(chunks));
    }

    [Test]
    public void AddPolicy_Should_Add_To_Policies()
    {
        var policy = new PolicyBuilder();
        var builder = new PublisherBuilder();
        builder.AddPolicy(policy);

        Assert.That(builder.Policies, Contains.Item(policy));
    }

    [Test]
    public void DeletePolicyAt_WithValidIndex_RemovesPolicy()
    {
        var builder = new PublisherBuilder()
            .AddPolicy(new PolicyBuilder())
            .AddPolicy(new PolicyBuilder());

        builder.DeletePolicyAt(0);

        Assert.That(builder.Policies.Length, Is.EqualTo(1));
    }

    [Test]
    public void UpdatePolicyAt_WithInvalidIndex_ThrowsArgumentOutOfRangeException()
    {
        var builder = new PublisherBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UpdatePolicyAt(0, new PolicyBuilder()));
    }

    [Test]
    public void DeletePolicyAt_WithInvalidIndex_ThrowsArgumentOutOfRangeException()
    {
        var builder = new PublisherBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.DeletePolicyAt(0));
    }

    [Test]
    public void IsParallel_Should_Set_Async_Property()
    {
        var builder = new PublisherBuilder();
        builder.WithParallelism(3);

        Assert.That(builder.Parallel, Is.Not.Null);
    }

    [Test]
    public void Configure_RabbitMqSenderConfig_Should_Set_RabbitMq()
    {
        var config = new RabbitMqSenderConfig();
        var builder = new PublisherBuilder();
        builder.Configure(config);

        Assert.That(builder.RabbitMq, Is.SameAs(config));
        Assert.That(builder.Socket, Is.Null);
        Assert.That(builder.Sftp, Is.Null);
    }

    [Test]
    [TestCaseSource(nameof(SupportedSenderConfigurationsForRead))]
    public void ReadConfiguration_WithConfiguredType_ReturnsConfiguredInstance(ISenderConfig config)
    {
        var builder = new PublisherBuilder().Configure(config);

        Assert.That(builder.ReadConfiguration(), Is.SameAs(config));
    }

    [Test]
    public void ReadConfiguration_WithoutConfiguredType_ReturnsNull()
    {
        var builder = new PublisherBuilder();

        Assert.That(builder.ReadConfiguration(), Is.Null);
    }

    [Test]
    public void Configure_MultipleSenderConfigs_ThrowsInvalidOperationException()
    {
        var rabbitMq = new RabbitMqSenderConfig();
        var kafka = new KafkaTopicSenderConfig();

        var builder = new PublisherBuilder();
        builder.Configure(rabbitMq);
        builder.Configure(kafka);

        Assert.That(builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName), Is.Null);
        Assert.That(_actionFailures.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Build_WithMultipleSenderConfigsAndValidRequiredFields_ReturnsNullAndActionFailure()
    {
        var builder = new PublisherBuilder()
            .Named("publisher-conflict");

        typeof(PublisherBuilder).GetProperty("RabbitMq", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(builder, new RabbitMqSenderConfig());
        typeof(PublisherBuilder).GetProperty("KafkaTopic", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(builder, new KafkaTopicSenderConfig
            {
                TopicName = "topic",
                HostNames = ["host:9092"],
                Username = "user",
                Password = "pass"
            });

        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures, Is.Not.Empty);
    }

    private class FakeSenderConfig : ISenderConfig;

    [Test]
    public void Configure_NoSupportedType_ThrowsInvalidOperationException()
    {
        var builder = new PublisherBuilder();
        var fakeConfig = new FakeSenderConfig(); // assume this implements ISenderConfig but isn't handled

        builder.Configure(fakeConfig);

        Assert.That(builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName), Is.Null);
        Assert.That(_actionFailures.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Build_With_RabbitMqSender_Should_Create_Publisher()
    {
        var builder = new PublisherBuilder()
            .Named("TestPublisher")
            .AtStage(1)
            .Configure(new RabbitMqSenderConfig { Host = "https://test.com" });

        var publisher = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, "SessionName");

        Assert.That(publisher, Is.Not.Null);
        Assert.That(publisher!.Name, Is.EqualTo("TestPublisher"));
    }

    [Test]
    public void Build_WithoutSender_ThrowsInvalidOperationException()
    {
        var builder = new PublisherBuilder()
            .Named("TestPublisher");

        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, "SessionName");

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures.Count, Is.GreaterThan(0));
    }

    [Test]
    public void UpdateConfiguration_WithoutExistingConfiguration_ThrowsInvalidOperationException()
    {
        var builder = new PublisherBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.UpdateConfiguration(config => config));
    }

    [Test]
    public void UpdatePolicyAt_WithValidIndex_ReplacesPolicy()
    {
        var replacementPolicy = new PolicyBuilder();
        var builder = new PublisherBuilder()
            .AddPolicy(new PolicyBuilder())
            .AddPolicy(new PolicyBuilder());

        builder.UpdatePolicyAt(0, replacementPolicy);

        Assert.That(builder.Policies[0], Is.SameAs(replacementPolicy));
        Assert.That(builder.Policies[1], Is.Not.SameAs(replacementPolicy));
    }

    private static IEnumerable<TestCaseData> TestSendersWhichSupportSingleSending()
    {
        yield return new TestCaseData(new RabbitMqSenderConfig()).SetName("RabbitMqSender");

        yield return new TestCaseData(new KafkaTopicSenderConfig
                { TopicName = "Test", HostNames = ["Test"], Username = "Test", Password = "Test" })
            .SetName("KafkaTopicSender");

        yield return new TestCaseData(new SftpSenderConfig
                { Hostname = "Test", Port = 123, Username = "Test", Password = "Test", Path = "Test" })
            .SetName("SftpSender");

        yield return new TestCaseData(new SocketSenderConfig
        {
            Port = 100, Host = "Test", SocketType = SocketType.Stream, BufferSize = 10, ProtocolType = ProtocolType.IP
        }).SetName("SocketSender");

        yield return new TestCaseData(new S3BucketSenderConfig()).SetName("S3BucketSender");
    }

    private static IEnumerable<TestCaseData> TestSendersWhichSupportChunkSending()
    {
        yield return new TestCaseData(new ElasticSenderConfig
                { Password = "Test", Username = "Test", Url = "http://localhost:8080", IndexName = "Test" })
            .SetName("ElasticSender");

        yield return new TestCaseData(new MongoDbCollectionSenderConfig()).SetName(
            "MongoDbCollectionSender");

        yield return new TestCaseData(new OracleSenderConfig()).SetName("OracleSender");

        yield return new TestCaseData(new MsSqlSenderConfig()).SetName("MsSqlSender");

        yield return new TestCaseData(new RedisSenderConfig()).SetName("RedisSender");
    }

    private static IEnumerable<TestCaseData> TestSendersWhichSupportAllSending()
    {
        yield return new TestCaseData(new PostgreSqlSenderConfig
                { TableName = "Test", ConnectionString = "Host=localhost;Port=100;Username=Test;Password=Test" })
            .SetName("PostgreSqlSender");
    }

    [Test]
    [TestCaseSource(nameof(TestSendersWhichSupportSingleSending))]
    [TestCaseSource(nameof(TestSendersWhichSupportAllSending))]
    public void
        TestCreationOfSendersInPublisherBuilder_CreateSendersWhichSupportSingleSending_ShouldBuildWithSenderSuccessfully
        (
            ISenderConfig senderConfiguration)
    {
        // Arrange
        var publisherBuilder = new PublisherBuilder();
        publisherBuilder.Configure(senderConfiguration);

        var buildMethod = publisherBuilder.GetType()
            .GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        var actionFailures = new List<ActionFailure>();
        var parameters = new object[] { Globals.GetContextWithMetadata(), actionFailures, "Test" };

        // Act
        var publisher = buildMethod!.Invoke(publisherBuilder, parameters);

        // Extract sender and chunkSender fields safely
        var sender = GetFieldValue(publisher, "_sender");
        var chunkSender = GetFieldValue(publisher, "_chunkSender");

        // Assert
        Assert.That(sender, Is.Not.Null);
        Assert.That(chunkSender, Is.Null);
    }

    [Test]
    [TestCaseSource(nameof(TestSendersWhichSupportChunkSending))]
    [TestCaseSource(nameof(TestSendersWhichSupportAllSending))]
    public void
        TestCreationOfSendersInPublisherBuilder_CreateSendersWhichSupportChunkSending_ShouldBuildWithChunkSenderSuccessfully
        (
            ISenderConfig senderConfiguration)
    {
        // Arrange
        var publisherBuilder = new PublisherBuilder().WithChunks(new Chunks { ChunkSize = 1 })
            .Configure(senderConfiguration).Named("Test");

        var buildMethod = publisherBuilder.GetType()
            .GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        var actionFailures = new List<ActionFailure>();

        // Act
        var publisher = publisherBuilder.Build(Globals.GetContextWithMetadata(), actionFailures, "Test");

        // Extract sender and chunkSender fields safely
        var sender = GetFieldValue(publisher, "_sender");
        var chunkSender = GetFieldValue(publisher, "_chunkSender");

        // Assert
        Assert.That(chunkSender, Is.Not.Null);
        Assert.That(sender, Is.Null);
    }

    [Test]
    [TestCaseSource(nameof(TestSendersWhichSupportChunkSending))]
    public void TestValidationOfChunks_ConfigureChunkableProtocolWithoutChunksField_ShouldNotBeValid(
        ISenderConfig senderConfiguration)
    {
        // Arrange
        var publisherBuilder = new PublisherBuilder();
        publisherBuilder.Configure(senderConfiguration);

        // Act 
        var validationResults = new List<ValidationResult>();
        RunnerValidationUtils.TryValidateProperties(publisherBuilder, validationResults, "Chunk");

        // Assert
        Assert.That(validationResults.Count(r => r.ErrorMessage!.Contains("Chunk")), Is.EqualTo(1));
    }

    [Test]
    [TestCaseSource(nameof(TestSendersWhichSupportSingleSending))]
    public void TestValidationOfChunks_ConfigureSingleProtocolWithChunksField_ShouldNotBeValid(
        ISenderConfig senderConfiguration)
    {
        // Arrange
        var publisherBuilder = new PublisherBuilder();
        publisherBuilder.WithChunks(new Chunks());
        publisherBuilder.Configure(senderConfiguration);

        // Act 
        var validationResults = new List<ValidationResult>();
        RunnerValidationUtils.TryValidateProperties(publisherBuilder, validationResults, "Chunk");

        // Assert
        Assert.That(validationResults.Count(r => r.ErrorMessage!.Contains("Chunk")), Is.EqualTo(1));
    }

    [Test]
    [TestCaseSource(nameof(TestSendersWhichSupportSingleSending))]
    public void
        TestValidationOfChunks_ConfigureValidConfigurationAccordingToIfTheProtocolNeedsChunksOrNot_ShouldBeValidOnSingleProtocols(
            ISenderConfig senderConfiguration)
    {
        // Arrange
        var publisherBuilder = new PublisherBuilder();
        publisherBuilder.Configure(senderConfiguration);

        // Act 
        var validationResults = new List<ValidationResult>();
        RunnerValidationUtils.TryValidateProperties(publisherBuilder, validationResults, "Chunk");

        // Assert
        Assert.That(validationResults.Count(r => r.ErrorMessage!.Contains("Chunk")), Is.EqualTo(0));
    }


    [Test]
    [TestCaseSource(nameof(TestSendersWhichSupportChunkSending))]
    public void
        TestValidationOfChunks_ConfigureValidConfigurationAccordingToIfTheProtocolNeedsChunksOrNot_ShouldBeValidOnChunkProtocols(
            ISenderConfig senderConfiguration)
    {
        // Arrange
        var publisherBuilder = new PublisherBuilder();
        publisherBuilder.WithChunks(new Chunks());
        publisherBuilder.Configure(senderConfiguration);

        // Act 
        var validationResults = new List<ValidationResult>();
        RunnerValidationUtils.TryValidateProperties(publisherBuilder, validationResults, "Chunk");

        // Assert
        Assert.That(validationResults.Count(r => r.ErrorMessage!.Contains("Chunk")), Is.EqualTo(0));
    }

    private static object? GetFieldValue(object? obj, string fieldName)
    {
        var field = obj?.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        return field?.GetValue(obj);
    }
}
