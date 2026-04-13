using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Policies;
using QaaS.Framework.Policies.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Http;
using QaaS.Framework.Protocols.ConfigurationObjects.Prometheus;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Socket;
using QaaS.Framework.Protocols.ConfigurationObjects.Sql;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.RuntimeOverrides;
using QaaS.Runner.Sessions.Session.Builders;
using QaaS.Runner.Sessions.Tests.Actions.Utils;
using ConsumerBuilder = QaaS.Runner.Sessions.Actions.Consumers.Builders.ConsumerBuilder;
using PublisherBuilder = QaaS.Runner.Sessions.Actions.Publishers.Builders.PublisherBuilder;
using StageConfig = QaaS.Runner.Sessions.Session.Builders.StageConfig;

namespace QaaS.Runner.Sessions.Tests.Session.Builders;

[TestFixture]
public class SessionBuilderTests
{
    private InternalContext _context = null!;
    private Mock<IProbe> _mockProbe = null!;
    private Mock<ConsumerBuilder> _mockConsumerBuilder = null!;
    private Mock<PublisherBuilder> _mockPublisherBuilder = null!;
    private Mock<TransactionBuilder> _mockTransactionBuilder = null!;
    private Mock<ProbeBuilder> _mockProbeBuilder = null!;
    private Mock<CollectorBuilder> _mockCollectorBuilder = null!;
    private Mock<MockerCommandBuilder> _mockMockerCommandBuilder = null!;
    private const string SessionName = "test session";

    [SetUp]
    public void SetUp()
    {
        _context = CreationalFunctions.CreateContext(SessionName, []);
        _mockProbe = new Mock<IProbe>();
        _mockConsumerBuilder = new Mock<ConsumerBuilder>();
        _mockPublisherBuilder = new Mock<PublisherBuilder>();
        _mockTransactionBuilder = new Mock<TransactionBuilder>();
        _mockProbeBuilder = new Mock<ProbeBuilder>();
        _mockCollectorBuilder = new Mock<CollectorBuilder>();
        _mockMockerCommandBuilder = new Mock<MockerCommandBuilder>();
    }

    [Test]
    public void Named_Should_Set_Name()
    {
        var builder = new SessionBuilder();
        builder.Named("TestSession");

        Assert.That(builder.Name, Is.EqualTo("TestSession"));
    }

    [Test]
    public void WithTimeoutBefore_Should_Set_TimeoutBeforeSessionMs()
    {
        var builder = new SessionBuilder();
        builder.WithTimeoutBefore(5000);

        Assert.That(builder.TimeoutBeforeSessionMs, Is.EqualTo(5000));
    }

    [Test]
    public void WithTimeoutAfter_Should_Set_TimeoutAfterSessionMs()
    {
        var builder = new SessionBuilder();
        builder.WithTimeoutAfter(3000);

        Assert.That(builder.TimeoutAfterSessionMs, Is.EqualTo(3000));
    }

    [Test]
    public void AtStage_Should_Set_Stage_And_RunUntilStage()
    {
        var builder = new SessionBuilder();
        builder.AtStage(5);

        Assert.That(builder.Stage, Is.EqualTo(5));
        Assert.That(builder.RunUntilStage, Is.EqualTo(6));
    }

    [Test]
    public void RunSessionUntilStage_Should_Set_RunUntilStage()
    {
        var builder = new SessionBuilder();
        builder.RunSessionUntilStage(10);

        Assert.That(builder.RunUntilStage, Is.EqualTo(10));
    }

    [Test]
    public void DiscardData_Should_Set_SaveData()
    {
        var builder = new SessionBuilder();
        builder.DiscardData();

        Assert.That(builder.SaveData, Is.False);
    }

    [Test]
    public void WithinCategory_Should_Set_Category()
    {
        var builder = new SessionBuilder();
        builder.WithinCategory("TestCategory");

        Assert.That(builder.Category, Is.EqualTo("TestCategory"));
    }

    [Test]
    public void WithTimeZone_Should_Set_TimeZoneId()
    {
        var builder = new SessionBuilder();
        builder.WithTimeZone("Europe/London");

        Assert.That(builder.TimeZoneId, Is.EqualTo("Europe/London"));
    }

    [Test]
    public void AddConsumer_Should_Add_ConsumerBuilder()
    {
        var builder = new SessionBuilder();
        builder.AddConsumer(_mockConsumerBuilder.Object);

        Assert.That(builder.Consumers, Contains.Item(_mockConsumerBuilder.Object));
    }

    [Test]
    public void AddPublisher_Should_Add_PublisherBuilder()
    {
        var builder = new SessionBuilder();
        builder.AddPublisher(_mockPublisherBuilder.Object);

        Assert.That(builder.Publishers, Contains.Item(_mockPublisherBuilder.Object));
    }

    [Test]
    public void AddTransaction_Should_Add_TransactionBuilder()
    {
        var builder = new SessionBuilder();
        builder.AddTransaction(_mockTransactionBuilder.Object);

        Assert.That(builder.Transactions, Contains.Item(_mockTransactionBuilder.Object));
    }

    [Test]
    public void AddProbe_Should_Add_ProbeBuilder()
    {
        var builder = new SessionBuilder();
        builder.AddProbe(_mockProbeBuilder.Object);

        Assert.That(builder.Probes, Contains.Item(_mockProbeBuilder.Object));
    }

    [Test]
    public void AddCollector_Should_Add_CollectorBuilder()
    {
        var builder = new SessionBuilder();
        builder.AddCollector(_mockCollectorBuilder.Object);

        Assert.That(builder.Collectors, Contains.Item(_mockCollectorBuilder.Object));
    }

    [Test]
    public void AddMockerCommand_Should_Add_MockerCommandBuilder()
    {
        var builder = new SessionBuilder();
        builder.AddMockerCommand(_mockMockerCommandBuilder.Object);

        Assert.That(builder.MockerCommands, Contains.Item(_mockMockerCommandBuilder.Object));
    }

    [Test]
    public void AddStage_Should_Add_StageConfig()
    {
        var builder = new SessionBuilder();
        var stageConfig = new StageConfig(1, 0, 0);
        builder.AddStage(stageConfig);

        Assert.That(builder.Stages, Contains.Item(stageConfig));
    }

    [Test]
    public void Build_Should_Create_Session_With_All_Builders()
    {
        // Arrange
        var builder = new SessionBuilder()
            .Named("TestSession")
            .AtStage(1)
            .WithTimeoutBefore(1000)
            .WithTimeoutAfter(2000)
            .DiscardData()
            .WithinCategory("TestCategory");

        builder.AddConsumer(
            new ConsumerBuilder()
                .Named("TestConsumer")
                .AddPolicy(new PolicyBuilder().Configure(new CountPolicyConfig()))
                .Configure(new SocketReaderConfig
                {
                    Host = "https:test",
                    Port = 8080,
                    ProtocolType = ProtocolType.IP
                }));
        
        builder.AddPublisher(
            new PublisherBuilder()
                .Named("TestPublisher")
                .AddPolicy(new PolicyBuilder().Configure(new CountPolicyConfig()))
                .Configure(new RabbitMqSenderConfig { Host = "https://test.com" }));
        
        builder.AddTransaction(new TransactionBuilder()
            .Named("TestTransaction")
            .Configure(new HttpTransactorConfig
            {
                Method = HttpMethods.Delete,
                BaseAddress = "https://test.com"
            }));
        
        var probes = new List<KeyValuePair<string, IProbe>>
        {
            new ("TestProbe", _mockProbe.Object)
        };
        
        builder.AddProbe(new ProbeBuilder()
            .Named("TestProbe")
            .HookNamed("TestHook"));
        
        builder.AddCollector(new CollectorBuilder()
            .Named("TestCollector")
            .Configure(new PrometheusFetcherConfig { Url = "https://promql:8080", Expression = "sum ()" }));
        
        builder.AddMockerCommand(new MockerCommandBuilder()
            .Named("TestMockerCommand")
            .Configure(new MockerCommandConfig()));

        // Act
        var session = builder.Build(_context, probes);

        // Assert
        Assert.That(session, Is.Not.Null);
        Assert.That(session.Name, Is.EqualTo("TestSession"));
        Assert.That(session.SessionStage, Is.EqualTo(1));
    }

    [Test]
    public void Build_Should_Handle_Null_Collections()
    {
        // Arrange
        var builder = new SessionBuilder()
            .Named("TestSession")
            .AtStage(1);

        var probeHooks = new List<KeyValuePair<string, IProbe>>();

        // Act
        var session = builder.Build(_context, probeHooks);

        // Assert
        Assert.That(session, Is.Not.Null);
        Assert.That(session.Name, Is.EqualTo("TestSession"));
    }

    [Test]
    public void Build_Should_Handle_Empty_Collections()
    {
        // Arrange
        var builder = new SessionBuilder()
            .Named("TestSession")
            .AtStage(1);

        var probeHooks = new List<KeyValuePair<string, IProbe>>();

        // Act
        var session = builder.Build(_context, probeHooks);

        // Assert
        Assert.That(session, Is.Not.Null);
    }

    [Test]
    public void Build_When_Action_Collections_Are_Null_Treats_Them_As_Empty_Collections()
    {
        var builder = new SessionBuilder()
            .Named("TestSession")
            .AtStage(1);

        foreach (var propertyName in new[] { "Consumers", "Publishers", "Transactions", "Probes", "Collectors", "MockerCommands" })
        {
            typeof(SessionBuilder).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(builder, null);
        }

        var session = builder.Build(_context, []);

        Assert.That(session, Is.Not.Null);
        foreach (var propertyName in new[] { "Consumers", "Publishers", "Transactions", "Probes", "Collectors", "MockerCommands" })
        {
            var propertyValue = typeof(SessionBuilder).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(builder) as Array;
            Assert.That(propertyValue, Is.Not.Null, propertyName);
            Assert.That(propertyValue, Is.Empty, propertyName);
        }
    }

    [Test]
    public void Build_WithMatchingStageConfig_AppliesStageTimeoutsToBuiltStage()
    {
        var builder = new SessionBuilder()
            .Named("TestSession")
            .AtStage(1)
            .AddStage(new StageConfig(stageNumber: 2, timeoutBefore: 123, timeoutAfter: 456))
            .AddPublisher(new PublisherBuilder()
                .Named("publisher-stage-2")
                .AtStage(2)
                .Configure(new RabbitMqSenderConfig { Host = "https://test.com" }));

        var session = builder.Build(_context, []);
        var stagesField = typeof(global::QaaS.Runner.Sessions.Session.Session)
            .GetField("_stages", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var stages = (Dictionary<int, global::QaaS.Runner.Sessions.Session.Stage>)stagesField.GetValue(session)!;
        var stageTwo = stages[2];

        var sleepBeforeField = typeof(global::QaaS.Runner.Sessions.Session.Stage).GetProperty("SleepBeforeMilliseconds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var sleepAfterField = typeof(global::QaaS.Runner.Sessions.Session.Stage).GetProperty("SleepAfterMilliseconds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        Assert.That(sleepBeforeField.GetValue(stageTwo), Is.EqualTo(123));
        Assert.That(sleepAfterField.GetValue(stageTwo), Is.EqualTo(456));
    }

    [Test]
    public void Build_WhenSessionTimeZoneIsConfigured_ForwardsItToProtocolOverrides()
    {
        const string timeZoneId = "Europe/London";
        string? capturedConsumerTimeZone = null;
        string? capturedPublisherTimeZone = null;
        var chunkReader = new Mock<IChunkReader>();
        chunkReader.Setup(reader => reader.ReadChunk(It.IsAny<TimeSpan>()))
            .Returns(Array.Empty<DetailedData<object>>());
        var sender = new Mock<ISender>();
        sender.Setup(mock => mock.Send(It.IsAny<Data<object>>()))
            .Returns(new DetailedData<object>());

        _context.SetSessionActionOverrides(new SessionActionOverrides
        {
            Consumer = request =>
            {
                capturedConsumerTimeZone = request.TimeZoneId;
                return (null, chunkReader.Object);
            },
            Publisher = request =>
            {
                capturedPublisherTimeZone = request.TimeZoneId;
                return (sender.Object, null);
            }
        });

        var builder = new SessionBuilder()
            .Named("TestSession")
            .AtStage(1)
            .WithTimeZone(timeZoneId)
            .AddConsumer(new ConsumerBuilder()
                .Named("sql-consumer")
                .WithTimeout(100)
                .Configure(new MsSqlReaderConfig
                {
                    ConnectionString = "Server=localhost",
                    TableName = "events",
                    InsertionTimeField = "created_at"
                }))
            .AddPublisher(new PublisherBuilder()
                .Named("sql-publisher")
                .Configure(new RabbitMqSenderConfig
                {
                    Host = "https://test.com"
                }));

        _ = builder.Build(_context, []);

        Assert.Multiple(() =>
        {
            Assert.That(capturedConsumerTimeZone, Is.EqualTo(timeZoneId));
            Assert.That(capturedPublisherTimeZone, Is.EqualTo(timeZoneId));
        });
    }
}

