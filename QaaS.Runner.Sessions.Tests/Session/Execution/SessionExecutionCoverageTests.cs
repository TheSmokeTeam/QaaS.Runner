using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Elastic;
using QaaS.Framework.Protocols.ConfigurationObjects.Grpc;
using QaaS.Framework.Protocols.ConfigurationObjects.Http;
using QaaS.Framework.Protocols.ConfigurationObjects.IbmMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.Protocols.ConfigurationObjects.MongoDb;
using QaaS.Framework.Protocols.ConfigurationObjects.Prometheus;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Redis;
using QaaS.Framework.Protocols.ConfigurationObjects.S3;
using QaaS.Framework.Protocols.ConfigurationObjects.Sftp;
using QaaS.Framework.Protocols.ConfigurationObjects.Socket;
using QaaS.Framework.Protocols.ConfigurationObjects.Sql;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Loaders;
using QaaS.Runner.Options;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.Session.Builders;
using QaaS.Runner.Sessions.RuntimeOverrides;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;

namespace QaaS.Runner.Sessions.Tests.Session.Execution;

[TestFixture]
[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class SessionExecutionCoverageTests
{
    private const int MinimalActionTimeoutMs = 1;
    private const int MinimalMockerRequestDurationMs = 1;
    private static readonly MethodInfo BuildContextMethodInfo = typeof(RunLoader<Runner, RunOptions>).GetMethod(
        "BuildContext", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly ScenarioDataSource[] ScenarioDataSources =
    [
        new("source-alpha", ["alpha-0", "alpha-1"]),
        new("source-beta", ["beta-100"]),
        new("skip-gamma", ["gamma-200"])
    ];

    private static readonly string[] IncludedDataSourceNames = ["source-alpha", "source-beta"];
    private static readonly string[] IncludedDataSourcePatterns = ["^source-"];
    private static readonly string[] ConsumerOutputs = ["consumer-0", "consumer-1", "consumer-2"];
    private static readonly string[] CollectorOutputs = ["collector-0", "collector-1"];
    private static readonly string[] MockerInputs = ["mocker-input-0"];
    private static readonly string[] MockerOutputs = ["mocker-output-0"];

    [TestCaseSource(nameof(GetProtocolScenarioCases))]
    public void Session_WithEachProtocol_CanRunFromCodeAndYaml(object scenarioObject, object sourceObject)
    {
        var scenario = (SessionScenarioDefinition)scenarioObject;
        var source = (ScenarioSource)sourceObject;
        var registry = new MockRuntimeRegistry();
        var context = source == ScenarioSource.Code
            ? CreateSessionContext()
            : CreateContextFromYaml(BuildYamlConfiguration([scenario]));

        context.ExecutionData.DataSources = CreateDataSources();
        context.ExecutionData.SessionDatas.Clear();
        context.SetSessionActionOverrides(CreateActionOverrides(registry, scenario));

        var sessionBuilder = source == ScenarioSource.Code
            ? CreateCodeSessionBuilder(scenario)
            : CreateYamlSessionBuilder(context, scenario);

        var session = sessionBuilder.Build(context, []);
        var sessionData = session.Run(context.ExecutionData);

        Assert.That(sessionData, Is.Not.Null);
        Assert.That(sessionData!.Name, Is.EqualTo(scenario.Name));
        AssertScenario(sessionData, registry, scenario);
    }

    [TestCaseSource(nameof(GetCombinationScenarioCases))]
    public void Session_WithActionCombinations_CanRunFromCodeAndYaml(object scenarioObject, object sourceObject)
    {
        var scenario = (SessionScenarioDefinition)scenarioObject;
        var source = (ScenarioSource)sourceObject;
        var registry = new MockRuntimeRegistry();
        var context = source == ScenarioSource.Code
            ? CreateSessionContext()
            : CreateContextFromYaml(BuildYamlConfiguration([scenario]));

        context.ExecutionData.DataSources = CreateDataSources();
        context.ExecutionData.SessionDatas.Clear();
        context.SetSessionActionOverrides(CreateActionOverrides(registry, scenario));

        var sessionBuilder = source == ScenarioSource.Code
            ? CreateCodeSessionBuilder(scenario)
            : CreateYamlSessionBuilder(context, scenario);

        var session = sessionBuilder.Build(context, []);
        var sessionData = session.Run(context.ExecutionData);

        Assert.That(sessionData, Is.Not.Null);
        Assert.That(sessionData!.Name, Is.EqualTo(scenario.Name));
        AssertScenario(sessionData, registry, scenario);
    }

    [TestCaseSource(nameof(GetStageLayoutScenarioCases))]
    public void Session_WithActionStageLayouts_CanRunFromCodeAndYaml(object layoutObject, object sourceObject)
    {
        var layout = (ActionStageLayout)layoutObject;
        var source = (ScenarioSource)sourceObject;
        var scenario = CreateActionStageScenario(layout.Name);
        var registry = new MockRuntimeRegistry();
        var context = source == ScenarioSource.Code
            ? CreateSessionContext()
            : CreateContextFromYaml(BuildYamlConfiguration([scenario]));

        context.ExecutionData.DataSources = CreateDataSources();
        context.ExecutionData.SessionDatas.Clear();
        context.SetSessionActionOverrides(CreateActionOverrides(registry, scenario));

        var sessionBuilder = source == ScenarioSource.Code
            ? CreateCodeSessionBuilder(scenario)
            : CreateYamlSessionBuilder(context, scenario);
        ApplyActionStageLayout(sessionBuilder, scenario, layout);

        var session = sessionBuilder.Build(context, []);
        var sessionData = session.Run(context.ExecutionData);
        var sessionLog = context.GetSessionLog(scenario.Name);

        Assert.That(sessionData, Is.Not.Null);
        Assert.That(sessionLog, Is.Not.Null);
        Assert.That(sessionData!.Name, Is.EqualTo(scenario.Name));
        AssertScenario(sessionData, registry, scenario);
        AssertStageLayoutLogged(sessionLog!, scenario.Name, layout);
    }

    [TestCase(ScenarioSource.Code)]
    [TestCase(ScenarioSource.Yaml)]
    public void Session_WhenSaveDataIsFalse_StillExecutesActions(object sourceObject)
    {
        var source = (ScenarioSource)sourceObject;
        var scenario = new SessionScenarioDefinition(
            Name: $"save-data-disabled-{source.ToString().ToLowerInvariant()}",
            SaveData: false,
            Publisher: new PublisherDefinition(
                ActionName: "publisher-save-data-disabled",
                ConfigKey: "RabbitMq",
                Configuration: CreateValidRabbitMqSenderConfig("https://publisher.test"),
                UseChunkSending: false,
                UseDataSourcePatterns: false,
                UseBinarySerialization: true,
                Iterations: 1,
                Parallelism: null));

        var registry = new MockRuntimeRegistry();
        var context = source == ScenarioSource.Code
            ? CreateSessionContext()
            : CreateContextFromYaml(BuildYamlConfiguration([scenario]));

        context.ExecutionData.DataSources = CreateDataSources();
        context.ExecutionData.SessionDatas.Clear();
        context.SetSessionActionOverrides(CreateActionOverrides(registry, scenario));

        var sessionBuilder = source == ScenarioSource.Code
            ? CreateCodeSessionBuilder(scenario)
            : CreateYamlSessionBuilder(context, scenario);

        var session = sessionBuilder.Build(context, []);
        var sessionData = session.Run(context.ExecutionData);

        Assert.That(sessionData, Is.Null);
        var publishedBodies = registry.Values(scenario.Publisher!.ActionName, Channels.PublisherItem);
        Assert.That(publishedBodies, Has.Count.EqualTo(GetSelectedPayloads(usePatterns: false).Length));
        Assert.That(publishedBodies, Has.All.InstanceOf<byte[]>());
    }

    [Test]
    public void ActExecution_FromYaml_WithSessionNameFilter_RunsOnlyRequestedSession()
    {
        const string targetSessionName = "filtered-target-session";
        var target = new SessionScenarioDefinition(
            Name: targetSessionName,
            Category: "target",
            Consumer: CreateSingleReaderConsumer("consumer-filter-target", "RabbitMq",
                CreateValidRabbitMqReaderConfig("https://consumer.test"), useBinarySerialization: true));
        var ignored = new SessionScenarioDefinition(
            Name: "ignored-session",
            Category: "ignored",
            Consumer: CreateSingleReaderConsumer("consumer-filter-ignored", "RabbitMq",
                CreateValidRabbitMqReaderConfig("https://consumer.test"), useBinarySerialization: true));

        var registry = new MockRuntimeRegistry();
        var context = CreateContextFromYaml(BuildYamlConfiguration([target, ignored]));
        context.SetSessionActionOverrides(CreateActionOverrides(registry, target, ignored));

        using var execution = new ExecutionBuilder(context, ExecutionType.Act, [targetSessionName], null, null, null)
            .Build();

        var exitCode = execution.Start();

        Assert.That(exitCode, Is.Zero);
        Assert.That(registry.Count(target.Consumer!.ActionName, Channels.ConsumerItem), Is.EqualTo(ConsumerOutputs.Length));
        Assert.That(registry.Count(ignored.Consumer!.ActionName, Channels.ConsumerItem), Is.Zero);
    }

    [Test]
    public void ActExecution_FromYaml_WithSessionCategoryFilter_RunsOnlyRequestedCategory()
    {
        var target = new SessionScenarioDefinition(
            Name: "category-target-session",
            Category: "critical",
            Consumer: CreateSingleReaderConsumer("consumer-category-target", "RabbitMq",
                CreateValidRabbitMqReaderConfig("https://consumer.test"), useBinarySerialization: false));
        var ignored = new SessionScenarioDefinition(
            Name: "category-ignored-session",
            Category: "background",
            Consumer: CreateSingleReaderConsumer("consumer-category-ignored", "RabbitMq",
                CreateValidRabbitMqReaderConfig("https://consumer.test"), useBinarySerialization: false));

        var registry = new MockRuntimeRegistry();
        var context = CreateContextFromYaml(BuildYamlConfiguration([target, ignored]));
        context.SetSessionActionOverrides(CreateActionOverrides(registry, target, ignored));

        using var execution = new ExecutionBuilder(context, ExecutionType.Act, null, ["critical"], null, null)
            .Build();

        var exitCode = execution.Start();

        Assert.That(exitCode, Is.Zero);
        Assert.That(registry.Count(target.Consumer!.ActionName, Channels.ConsumerItem), Is.EqualTo(ConsumerOutputs.Length));
        Assert.That(registry.Count(ignored.Consumer!.ActionName, Channels.ConsumerItem), Is.Zero);
    }

    private static IEnumerable<TestCaseData> GetProtocolScenarioCases()
    {
        foreach (var scenario in CreateProtocolScenarios())
        {
            foreach (var source in Enum.GetValues<ScenarioSource>())
            {
                yield return new TestCaseData(scenario, source)
                    .SetName($"{scenario.Name}_{source}");
            }
        }
    }

    private static IEnumerable<TestCaseData> GetCombinationScenarioCases()
    {
        foreach (var scenario in CreateCombinationScenarios())
        {
            foreach (var source in Enum.GetValues<ScenarioSource>())
            {
                yield return new TestCaseData(scenario, source)
                    .SetName($"{scenario.Name}_{source}");
            }
        }
    }

    private static IEnumerable<TestCaseData> GetStageLayoutScenarioCases()
    {
        foreach (var layout in CreateActionStageLayouts())
        {
            foreach (var source in Enum.GetValues<ScenarioSource>())
            {
                yield return new TestCaseData(layout, source)
                    .SetName($"{layout.Name}_{source}");
            }
        }
    }

    private static IEnumerable<SessionScenarioDefinition> CreateProtocolScenarios()
    {
        foreach (var definition in CreateConsumerProtocolScenarios())
        {
            yield return definition;
        }

        foreach (var definition in CreatePublisherProtocolScenarios())
        {
            yield return definition;
        }

        yield return new SessionScenarioDefinition(
            Name: "protocol-transaction-http",
            Transaction: new TransactionDefinition(
                ActionName: "transaction-http",
                ConfigKey: "Http",
                Configuration: new HttpTransactorConfig { Method = HttpMethods.Post, BaseAddress = "https://tx.test" },
                UseDataSourcePatterns: false,
                UseBinarySerialization: true,
                Iterations: 2));

        yield return new SessionScenarioDefinition(
            Name: "protocol-transaction-grpc",
            Transaction: new TransactionDefinition(
                ActionName: "transaction-grpc",
                ConfigKey: "Grpc",
                Configuration: new GrpcTransactorConfig(),
                UseDataSourcePatterns: true,
                UseBinarySerialization: true,
                Iterations: 1));

        yield return new SessionScenarioDefinition(
            Name: "protocol-collector-prometheus",
            Collector: new CollectorDefinition(
                ActionName: "collector-prometheus",
                ConfigKey: "Prometheus",
                Configuration: new PrometheusFetcherConfig
                {
                    Url = "https://prometheus.test",
                    Expression = "up"
                },
                UseBinarySerialization: false));

        yield return new SessionScenarioDefinition(
            Name: "protocol-mocker-change-action-stub",
            Mocker: new MockerDefinition(
                ActionName: "mocker-change-action-stub",
                Command: new MockerCommandConfig { ChangeActionStub = new ChangeActionStub() },
                Flavor: CommandFlavor.ChangeActionStub));

        yield return new SessionScenarioDefinition(
            Name: "protocol-mocker-trigger-action",
            Mocker: new MockerDefinition(
                ActionName: "mocker-trigger-action",
                Command: new MockerCommandConfig { TriggerAction = new TriggerAction() },
                Flavor: CommandFlavor.TriggerAction));

        yield return new SessionScenarioDefinition(
            Name: "protocol-mocker-consume",
            Mocker: new MockerDefinition(
                ActionName: "mocker-consume",
                Command: new MockerCommandConfig { Consume = new ConsumeCommandConfig() },
                Flavor: CommandFlavor.Consume));
    }

    private static IEnumerable<SessionScenarioDefinition> CreateConsumerProtocolScenarios()
    {
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-rabbitmq",
            Consumer: CreateSingleReaderConsumer("consumer-rabbitmq", "RabbitMq",
                CreateValidRabbitMqReaderConfig("https://rabbitmq.test"), useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-kafka",
            Consumer: CreateSingleReaderConsumer("consumer-kafka", "KafkaTopic",
                new KafkaTopicReaderConfig
                {
                    TopicName = "topic",
                    GroupId = "group",
                    HostNames = ["kafka:9092"],
                    Username = "user",
                    Password = "pass"
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-socket",
            Consumer: CreateSingleReaderConsumer("consumer-socket", "Socket",
                new SocketReaderConfig
                {
                    Host = "socket.test",
                    Port = 8080,
                    ProtocolType = ProtocolType.IP
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-ibmmq",
            Consumer: CreateSingleReaderConsumer("consumer-ibmmq", "IbmMqQueue",
                new IbmMqReaderConfig
                {
                    HostName = "ibmmq.test",
                    Port = 1414,
                    Channel = "DEV.APP.SVRCONN",
                    Manager = "QM1",
                    QueueName = "QUEUE1"
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-postgresql",
            Consumer: CreateSingleReaderConsumer("consumer-postgresql", "PostgreSqlTable",
                new PostgreSqlReaderConfig
                {
                    ConnectionString = "Host=postgres.test;Port=5432;",
                    TableName = "messages"
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-oracle",
            Consumer: CreateSingleReaderConsumer("consumer-oracle", "OracleSqlTable",
                new OracleReaderConfig
                {
                    ConnectionString = "Data Source=oracle.test;User Id=user;Password=pass",
                    TableName = "messages"
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-mssql",
            Consumer: CreateSingleReaderConsumer("consumer-mssql", "MsSqlTable",
                new MsSqlReaderConfig
                {
                    ConnectionString = "Server=mssql.test;Database=test;",
                    TableName = "messages"
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-trino",
            Consumer: CreateSingleReaderConsumer("consumer-trino", "TrinoSqlTable",
                new TrinoReaderConfig
                {
                    ConnectionString = "Host=trino.test;Port=8443;",
                    TableName = "messages",
                    Username = "user",
                    Password = "pass",
                    ClientTag = "tests",
                    Schema = "default",
                    Catalog = "hive",
                    Hostname = "trino.test"
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-elastic",
            Consumer: new ConsumerDefinition(
                ActionName: "consumer-elastic",
                ConfigKey: "ElasticIndices",
                Configuration: new ElasticReaderConfig
                {
                    TimestampField = "timestamp",
                    ReadBatchSize = 100,
                    ScrollContextExpirationMs = 1000,
                    ReadFromRunStartTime = true,
                    FilterSecondsBeforeRunStartTime = 10,
                    IndexPattern = "logs-*",
                    Url = "https://elastic.test",
                    Username = "user",
                    Password = "pass"
                },
                UseChunkReading: true,
                UseBinarySerialization: true,
                MessageCount: ConsumerOutputs.Length));
        yield return new SessionScenarioDefinition(
            Name: "protocol-consumer-s3",
            Consumer: CreateSingleReaderConsumer("consumer-s3", "S3Bucket",
                new S3BucketReaderConfig
                {
                    StorageBucket = "bucket",
                    ServiceURL = "https://s3.test",
                    AccessKey = "access",
                    SecretKey = "secret"
                }, useBinarySerialization: true));
    }

    private static IEnumerable<SessionScenarioDefinition> CreatePublisherProtocolScenarios()
    {
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-rabbitmq",
            Publisher: CreateSinglePublisher("publisher-rabbitmq", "RabbitMq",
                CreateValidRabbitMqSenderConfig("https://rabbitmq.test"), useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-kafka",
            Publisher: CreateSinglePublisher("publisher-kafka", "KafkaTopic",
                new KafkaTopicSenderConfig
                {
                    TopicName = "topic",
                    HostNames = ["kafka:9092"],
                    Username = "user",
                    Password = "pass"
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-sftp",
            Publisher: CreateSinglePublisher("publisher-sftp", "Sftp",
                new SftpSenderConfig
                {
                    Hostname = "sftp.test",
                    Port = 22,
                    Username = "user",
                    Password = "pass",
                    Path = "/tmp/test.txt"
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-socket",
            Publisher: CreateSinglePublisher("publisher-socket", "Socket",
                new SocketSenderConfig
                {
                    Host = "socket.test",
                    Port = 8080,
                    SocketType = SocketType.Stream,
                    BufferSize = 128,
                    ProtocolType = ProtocolType.IP
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-s3",
            Publisher: CreateSinglePublisher("publisher-s3", "S3Bucket",
                new S3BucketSenderConfig(), useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-elastic",
            Publisher: CreateChunkPublisher("publisher-elastic", "ElasticIndex",
                new ElasticSenderConfig
                {
                    Url = "https://elastic.test",
                    Username = "user",
                    Password = "pass",
                    IndexName = "logs"
                }, useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-mongodb",
            Publisher: CreateChunkPublisher("publisher-mongodb", "MongoDbCollection",
                new MongoDbCollectionSenderConfig(), useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-oracle",
            Publisher: CreateChunkPublisher("publisher-oracle", "OracleSqlTable",
                new OracleSenderConfig(), useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-mssql",
            Publisher: CreateChunkPublisher("publisher-mssql", "MsSqlTable",
                new MsSqlSenderConfig(), useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-redis",
            Publisher: CreateChunkPublisher("publisher-redis", "Redis",
                new RedisSenderConfig(), useBinarySerialization: true));
        yield return new SessionScenarioDefinition(
            Name: "protocol-publisher-postgresql",
            Publisher: CreateSinglePublisher("publisher-postgresql", "PostgreSqlTable",
                new PostgreSqlSenderConfig
                {
                    TableName = "messages",
                    ConnectionString = "Host=postgres.test;Port=5432;"
                }, useBinarySerialization: true));
    }

    private static IEnumerable<SessionScenarioDefinition> CreateCombinationScenarios()
    {
        foreach (var mask in Enumerable.Range(1, 31))
        {
            var usePatterns = mask % 2 == 0;
            var useBinarySerialization = mask % 3 != 0;
            var useChunkPublisher = (mask & (int)SessionActionFlags.Publisher) != 0 && mask % 4 == 0;
            var useChunkConsumer = (mask & (int)SessionActionFlags.Consumer) != 0 && mask % 5 == 0;
            var useGrpc = (mask & (int)SessionActionFlags.Transaction) != 0 && mask % 2 == 1;
            var useConsumeCommand = (mask & (int)SessionActionFlags.Mocker) != 0 && mask % 3 == 0;

            yield return new SessionScenarioDefinition(
                Name: $"combination-{mask:D2}",
                Publisher: (mask & (int)SessionActionFlags.Publisher) == 0
                    ? null
                    : useChunkPublisher
                        ? CreateChunkPublisher("publisher-combo", "ElasticIndex",
                            new ElasticSenderConfig
                            {
                                Url = "https://elastic.test",
                                Username = "user",
                                Password = "pass",
                                IndexName = "logs"
                            },
                            useBinarySerialization,
                            usePatterns,
                            iterations: 2,
                            parallelism: 2)
                        : CreateSinglePublisher("publisher-combo", "RabbitMq",
                            CreateValidRabbitMqSenderConfig("https://publisher.test"),
                            useBinarySerialization,
                            usePatterns,
                            iterations: 2,
                            parallelism: 2),
                Consumer: (mask & (int)SessionActionFlags.Consumer) == 0
                    ? null
                    : useChunkConsumer
                        ? new ConsumerDefinition(
                            ActionName: "consumer-combo",
                            ConfigKey: "ElasticIndices",
                            Configuration: new ElasticReaderConfig
                            {
                                TimestampField = "timestamp",
                                ReadBatchSize = 100,
                                ScrollContextExpirationMs = 1000,
                                ReadFromRunStartTime = true,
                                FilterSecondsBeforeRunStartTime = 10,
                                IndexPattern = "logs-*",
                                Url = "https://elastic.test",
                                Username = "user",
                                Password = "pass"
                            },
                            UseChunkReading: true,
                            UseBinarySerialization: useBinarySerialization,
                            MessageCount: ConsumerOutputs.Length)
                        : CreateSingleReaderConsumer("consumer-combo", "RabbitMq",
                            CreateValidRabbitMqReaderConfig("https://consumer.test"), useBinarySerialization),
                Transaction: (mask & (int)SessionActionFlags.Transaction) == 0
                    ? null
                    : new TransactionDefinition(
                        ActionName: "transaction-combo",
                        ConfigKey: useGrpc ? "Grpc" : "Http",
                        Configuration: useGrpc
                            ? new GrpcTransactorConfig()
                            : new HttpTransactorConfig
                            {
                                Method = HttpMethods.Post,
                                BaseAddress = "https://tx.test"
                            },
                        UseDataSourcePatterns: usePatterns,
                        UseBinarySerialization: useBinarySerialization,
                        Iterations: 2),
                Collector: (mask & (int)SessionActionFlags.Collector) == 0
                    ? null
                    : new CollectorDefinition(
                        ActionName: "collector-combo",
                        ConfigKey: "Prometheus",
                        Configuration: new PrometheusFetcherConfig
                        {
                            Url = "https://prometheus.test",
                            Expression = "up"
                        },
                        UseBinarySerialization: false),
                Mocker: (mask & (int)SessionActionFlags.Mocker) == 0
                    ? null
                    : new MockerDefinition(
                        ActionName: "mocker-combo",
                        Command: useConsumeCommand
                            ? new MockerCommandConfig { Consume = new ConsumeCommandConfig() }
                            : new MockerCommandConfig { TriggerAction = new TriggerAction() },
                        Flavor: useConsumeCommand ? CommandFlavor.Consume : CommandFlavor.TriggerAction));
        }
    }

    private static IEnumerable<ActionStageLayout> CreateActionStageLayouts()
    {
        yield return new ActionStageLayout("stage-layout-all-same", 0, 0, 0, 0);
        yield return new ActionStageLayout("stage-layout-split", 0, 1, 1, 2);
        yield return new ActionStageLayout("stage-layout-staggered", 0, 1, 2, 4);
    }

    private static SessionScenarioDefinition CreateActionStageScenario(string layoutName)
    {
        return new SessionScenarioDefinition(
            Name: layoutName,
            Publisher: CreateSinglePublisher("publisher-stage-layout", "RabbitMq",
                CreateValidRabbitMqSenderConfig("https://publisher.test"), useBinarySerialization: false),
            Consumer: CreateSingleReaderConsumer("consumer-stage-layout", "RabbitMq",
                CreateValidRabbitMqReaderConfig("https://consumer.test"), useBinarySerialization: false),
            Transaction: new TransactionDefinition(
                ActionName: "transaction-stage-layout",
                ConfigKey: "Http",
                Configuration: new HttpTransactorConfig
                {
                    Method = HttpMethods.Post,
                    BaseAddress = "https://transaction.test"
                },
                UseDataSourcePatterns: false,
                UseBinarySerialization: false,
                Iterations: 1),
            Mocker: new MockerDefinition(
                ActionName: "mocker-stage-layout",
                Command: new MockerCommandConfig { Consume = new ConsumeCommandConfig() },
                Flavor: CommandFlavor.Consume));
    }

    private static void ApplyActionStageLayout(SessionBuilder builder, SessionScenarioDefinition scenario,
        ActionStageLayout layout)
    {
        if (scenario.Consumer != null)
        {
            builder.UpdateConsumer(scenario.Consumer.ActionName, consumer => consumer.AtStage(layout.ConsumerStage));
            EnsureZeroTimeoutStageConfiguration(builder, layout.ConsumerStage);
        }

        if (scenario.Publisher != null)
        {
            builder.UpdatePublisher(scenario.Publisher.ActionName,
                publisher => publisher.AtStage(layout.PublisherStage));
            EnsureZeroTimeoutStageConfiguration(builder, layout.PublisherStage);
        }

        if (scenario.Transaction != null)
        {
            builder.UpdateTransaction(scenario.Transaction.ActionName,
                transaction => transaction.AtStage(layout.TransactionStage));
            EnsureZeroTimeoutStageConfiguration(builder, layout.TransactionStage);
        }

        if (scenario.Mocker != null)
        {
            builder.UpdateMockerCommand(scenario.Mocker.ActionName, command => command.AtStage(layout.MockerStage));
            EnsureZeroTimeoutStageConfiguration(builder, layout.MockerStage);
        }
    }

    private static void EnsureZeroTimeoutStageConfiguration(SessionBuilder builder, int stageNumber)
    {
        if (builder.ReadStage(stageNumber) == null)
        {
            builder.CreateStage(new StageConfig(stageNumber, timeoutBefore: 0, timeoutAfter: 0));
            return;
        }

        builder.UpdateStage(stageNumber, new StageConfig(stageNumber, timeoutBefore: 0, timeoutAfter: 0));
    }

    private static ConsumerDefinition CreateSingleReaderConsumer(string actionName, string configKey,
        IReaderConfig configuration, bool useBinarySerialization)
    {
        return new ConsumerDefinition(
            ActionName: actionName,
            ConfigKey: configKey,
            Configuration: configuration,
            UseChunkReading: false,
            UseBinarySerialization: useBinarySerialization,
            MessageCount: ConsumerOutputs.Length);
    }

    private static RabbitMqReaderConfig CreateValidRabbitMqReaderConfig(string host)
    {
        return new RabbitMqReaderConfig
        {
            Host = host,
            QueueName = "queue"
        };
    }

    private static PublisherDefinition CreateSinglePublisher(string actionName, string configKey,
        ISenderConfig configuration, bool useBinarySerialization, bool usePatterns = false, int iterations = 1,
        int? parallelism = null)
    {
        return new PublisherDefinition(
            ActionName: actionName,
            ConfigKey: configKey,
            Configuration: configuration,
            UseChunkSending: false,
            UseDataSourcePatterns: usePatterns,
            UseBinarySerialization: useBinarySerialization,
            Iterations: iterations,
            Parallelism: parallelism);
    }

    private static RabbitMqSenderConfig CreateValidRabbitMqSenderConfig(string host)
    {
        return new RabbitMqSenderConfig
        {
            Host = host,
            ExchangeName = "exchange",
            RoutingKey = "routing-key"
        };
    }

    private static PublisherDefinition CreateChunkPublisher(string actionName, string configKey,
        ISenderConfig configuration, bool useBinarySerialization, bool usePatterns = false, int iterations = 1,
        int? parallelism = null)
    {
        return new PublisherDefinition(
            ActionName: actionName,
            ConfigKey: configKey,
            Configuration: configuration,
            UseChunkSending: true,
            UseDataSourcePatterns: usePatterns,
            UseBinarySerialization: useBinarySerialization,
            Iterations: iterations,
            Parallelism: parallelism);
    }

    private static InternalContext CreateSessionContext()
    {
        var context = new InternalContext
        {
            Logger = NullLogger.Instance,
            RootConfiguration = new ConfigurationBuilder().Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>()),
            InternalGlobalDict = new Dictionary<string, object?>(),
            ExecutionData = new ExecutionData
            {
                DataSources = [],
                SessionDatas = []
            }
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig
        {
            Team = "Smoke",
            System = "QaaS"
        });
        return context;
    }

    private static InternalContext CreateContextFromYaml(string yamlContent)
    {
        var relativePath = Path.Combine("TestData", "Generated", $"session-{Guid.NewGuid():N}.yaml");
        var absolutePath = Path.Combine(Environment.CurrentDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, yamlContent);

        try
        {
            var loader = new RunLoader<Runner, RunOptions>(new RunOptions
            {
                ConfigurationFile = relativePath,
                SendLogs = false,
                NoProcessExit = true
            });

            var loadedContext = (InternalContext)BuildContextMethodInfo.Invoke(loader,
                [Guid.NewGuid().ToString("N"), null, null])!;
            return new InternalContext
            {
                Logger = NullLogger.Instance,
                RootConfiguration = loadedContext.RootConfiguration,
                ExecutionId = loadedContext.ExecutionId,
                CaseName = loadedContext.CaseName,
                InternalRunningSessions = loadedContext.InternalRunningSessions,
                InternalGlobalDict = loadedContext.InternalGlobalDict ?? new Dictionary<string, object?>(),
                ExecutionData = new ExecutionData
                {
                    DataSources = [],
                    SessionDatas = []
                }
            };
        }
        finally
        {
            File.Delete(absolutePath);
        }
    }

    private static List<DataSource> CreateDataSources()
    {
        return ScenarioDataSources.Select(source => new DataSource
        {
            Name = source.Name,
            Lazy = true,
            Generator = new StaticGenerator(source.Payloads)
        }).ToList();
    }

    private static SessionBuilder CreateCodeSessionBuilder(SessionScenarioDefinition scenario)
    {
        var builder = new SessionBuilder
        {
            Name = scenario.Name,
            Stage = 0,
            SaveData = scenario.SaveData,
            Category = scenario.Category,
            Probes = []
        };

        if (scenario.Publisher != null)
        {
            var publisherBuilder = new PublisherBuilder()
                .Named(scenario.Publisher.ActionName)
                .WithIterations(scenario.Publisher.Iterations)
                .Configure(scenario.Publisher.Configuration);

            if (scenario.Publisher.UseDataSourcePatterns)
            {
                foreach (var pattern in IncludedDataSourcePatterns)
                {
                    publisherBuilder.AddDataSourcePattern(pattern);
                }
            }
            else
            {
                foreach (var name in IncludedDataSourceNames)
                {
                    publisherBuilder.AddDataSource(name);
                }
            }

            if (scenario.Publisher.UseChunkSending)
            {
                publisherBuilder.WithChunks(new Chunks { ChunkSize = 2 });
            }

            if (scenario.Publisher.UseBinarySerialization)
            {
                publisherBuilder.WithSerializer(new SerializeConfig { Serializer = SerializationType.Binary });
            }

            if (scenario.Publisher.Parallelism.HasValue)
            {
                publisherBuilder.WithParallelism(scenario.Publisher.Parallelism.Value);
            }

            builder.CreatePublisher(publisherBuilder);
        }

        if (scenario.Consumer != null)
        {
            var consumerBuilder = new ConsumerBuilder()
                .Named(scenario.Consumer.ActionName)
                .WithTimeout(MinimalActionTimeoutMs)
                .Configure(scenario.Consumer.Configuration);

            if (scenario.Consumer.UseBinarySerialization)
            {
                consumerBuilder.WithDeserializer(new DeserializeConfig { Deserializer = SerializationType.Binary });
            }

            builder.CreateConsumer(consumerBuilder);
        }

        if (scenario.Transaction != null)
        {
            var transactionBuilder = new TransactionBuilder()
                .Named(scenario.Transaction.ActionName)
                .WithTimeout(MinimalActionTimeoutMs)
                .WithIterations(scenario.Transaction.Iterations)
                .Configure(scenario.Transaction.Configuration);

            if (scenario.Transaction.UseDataSourcePatterns)
            {
                foreach (var pattern in IncludedDataSourcePatterns)
                {
                    transactionBuilder.AddDataSourcePattern(pattern);
                }
            }
            else
            {
                foreach (var name in IncludedDataSourceNames)
                {
                    transactionBuilder.AddDataSource(name);
                }
            }

            if (scenario.Transaction.UseBinarySerialization)
            {
                transactionBuilder
                    .WithSerializer(new SerializeConfig { Serializer = SerializationType.Binary })
                    .WithDeserializer(new DeserializeConfig { Deserializer = SerializationType.Binary });
            }

            builder.CreateTransaction(transactionBuilder);
        }

        if (scenario.Collector != null)
        {
            builder.CreateCollector(new QaaS.Runner.Sessions.Actions.Collectors.CollectorBuilder()
                .Named(scenario.Collector.ActionName)
                .Configure(scenario.Collector.Configuration));
        }

        if (scenario.Mocker != null)
        {
            builder.CreateMockerCommand(new MockerCommandBuilder()
                .Named(scenario.Mocker.ActionName)
                .WithServerName("test-server")
                .WithRedis(new QaaS.Runner.Sessions.ConfigurationObjects.RedisConfig { Host = "localhost:6379" })
                .WithRequestDurationMs(MinimalMockerRequestDurationMs)
                .WithRequestRetries(1)
                .Configure(scenario.Mocker.Command));
        }

        ApplyZeroTimeoutStageConfiguration(builder, scenario);
        return builder;
    }

    private static void ApplyZeroTimeoutStageConfiguration(SessionBuilder builder, SessionScenarioDefinition scenario)
    {
        foreach (var stageNumber in GetUsedActionStages(scenario))
        {
            builder.CreateStage(new StageConfig(stageNumber, timeoutBefore: 0, timeoutAfter: 0));
        }
    }

    private static IEnumerable<int> GetUsedActionStages(SessionScenarioDefinition scenario)
    {
        if (scenario.Consumer != null)
        {
            yield return 0;
        }

        if (scenario.Publisher != null)
        {
            yield return 1;
        }

        if (scenario.Transaction != null)
        {
            yield return 2;
        }

        if (scenario.Mocker != null)
        {
            yield return 4;
        }
    }

    private static SessionBuilder CreateYamlSessionBuilder(InternalContext context, SessionScenarioDefinition scenario)
    {
        var executionBuilder = new ExecutionBuilder(context, ExecutionType.Act, null, null, null, null);
        var builder = (executionBuilder.Sessions ?? []).Single(session => session.Name == scenario.Name);
        ApplyZeroTimeoutStageConfiguration(builder, scenario);
        return builder;
    }

    private static SessionActionOverrides CreateActionOverrides(MockRuntimeRegistry registry,
        params SessionScenarioDefinition[] scenarios)
    {
        return new SessionActionOverrides
        {
            Consumer = request =>
            {
                var scenario = scenarios.Single(session =>
                    session.Consumer?.ActionName == request.ActionName);
                var consumer = scenario.Consumer!;
                return consumer.UseChunkReading
                    ? (null, CreateChunkReaderMock(request.ActionName, registry, consumer.UseBinarySerialization).Object)
                    : (CreateReaderMock(request.ActionName, registry, consumer.UseBinarySerialization).Object, null);
            },
            Publisher = request =>
            {
                var scenario = scenarios.Single(session =>
                    session.Publisher?.ActionName == request.ActionName);
                var publisher = scenario.Publisher!;
                return publisher.UseChunkSending
                    ? (null,
                        CreateChunkSenderMock(request.ActionName, registry, publisher.UseBinarySerialization).Object)
                    : (CreateSenderMock(request.ActionName, registry, publisher.UseBinarySerialization).Object, null);
            },
            Transaction = request =>
            {
                var scenario = scenarios.Single(session =>
                    session.Transaction?.ActionName == request.ActionName);
                return CreateTransactorMock(request.ActionName, registry, scenario.Transaction!.UseBinarySerialization)
                    .Object;
            },
            Collector = request =>
            {
                var scenario = scenarios.Single(session =>
                    session.Collector?.ActionName == request.ActionName);
                return CreateFetcherMock(request.ActionName, registry, scenario.Collector!.UseBinarySerialization)
                    .Object;
            },
            MockerCommand = request =>
            {
                var scenario = scenarios.Single(session =>
                    session.Mocker?.ActionName == request.ActionName);
                return new FakeMockerAction(request.ActionName, request.Stage, registry, scenario.Mocker!.Flavor,
                    scenario.Mocker.UseBinarySerialization);
            }
        };
    }

    private static Mock<IReader> CreateReaderMock(string actionName, MockRuntimeRegistry registry,
        bool useBinarySerialization)
    {
        var mock = new Mock<IReader>(MockBehavior.Strict);
        var payloads = ConsumerOutputs
            .Select(value => new DetailedData<object> { Body = MaybeSerialize(value, useBinarySerialization) })
            .Cast<DetailedData<object>?>()
            .Concat([null])
            .ToArray();
        var sequence = mock.SetupSequence(reader => reader.Read(It.IsAny<TimeSpan>()));
        foreach (var payload in payloads)
        {
            sequence = sequence.Returns(() =>
            {
                if (payload != null)
                {
                    registry.Record(actionName, Channels.ConsumerItem, payload.Body);
                }

                return payload;
            });
        }

        mock.Setup(reader => reader.Connect());
        mock.Setup(reader => reader.Disconnect());
        mock.Setup(reader => reader.GetSerializationType())
            .Returns(useBinarySerialization ? SerializationType.Binary : null);
        return mock;
    }

    private static Mock<IChunkReader> CreateChunkReaderMock(string actionName, MockRuntimeRegistry registry,
        bool useBinarySerialization)
    {
        var mock = new Mock<IChunkReader>(MockBehavior.Strict);
        mock.Setup(reader => reader.Connect());
        mock.Setup(reader => reader.Disconnect());
        mock.Setup(reader => reader.GetSerializationType())
            .Returns(useBinarySerialization ? SerializationType.Binary : null);
        mock.Setup(reader => reader.ReadChunk(It.IsAny<TimeSpan>()))
            .Returns(() =>
            {
                registry.Record(actionName, Channels.ConsumerChunkCall);
                return ConsumerOutputs.Select(output =>
                {
                    var body = MaybeSerialize(output, useBinarySerialization);
                    registry.Record(actionName, Channels.ConsumerItem, body);
                    return new DetailedData<object> { Body = body };
                });
            });
        return mock;
    }

    private static Mock<ISender> CreateSenderMock(string actionName, MockRuntimeRegistry registry,
        bool useBinarySerialization)
    {
        var mock = new Mock<ISender>(MockBehavior.Strict);
        mock.Setup(sender => sender.Connect());
        mock.Setup(sender => sender.Disconnect());
        mock.Setup(sender => sender.GetSerializationType())
            .Returns(useBinarySerialization ? SerializationType.Binary : null);
        mock.Setup(sender => sender.Send(It.IsAny<Data<object>>()))
            .Returns<Data<object>>(data =>
            {
                registry.Record(actionName, Channels.PublisherItem, data.Body);
                return new DetailedData<object> { Body = data.Body, Timestamp = DateTime.UtcNow };
            });
        return mock;
    }

    private static Mock<IChunkSender> CreateChunkSenderMock(string actionName, MockRuntimeRegistry registry,
        bool useBinarySerialization)
    {
        var mock = new Mock<IChunkSender>(MockBehavior.Strict);
        mock.Setup(sender => sender.Connect());
        mock.Setup(sender => sender.Disconnect());
        mock.Setup(sender => sender.GetSerializationType())
            .Returns(useBinarySerialization ? SerializationType.Binary : null);
        mock.Setup(sender => sender.SendChunk(It.IsAny<IEnumerable<Data<object>>>()))
            .Returns<IEnumerable<Data<object>>>(data =>
            {
                registry.Record(actionName, Channels.PublisherChunkCall);
                var sentData = data.ToArray();
                foreach (var item in sentData)
                {
                    registry.Record(actionName, Channels.PublisherItem, item.Body);
                }

                return sentData.Select(item => new DetailedData<object>
                {
                    Body = item.Body,
                    Timestamp = DateTime.UtcNow
                });
            });
        return mock;
    }

    private static Mock<ITransactor> CreateTransactorMock(string actionName, MockRuntimeRegistry registry,
        bool useBinarySerialization)
    {
        var mock = new Mock<ITransactor>(MockBehavior.Strict);
        var callIndex = 0;
        mock.Setup(transactor => transactor.GetInputCommunicationSerializationType())
            .Returns(useBinarySerialization ? SerializationType.Binary : null);
        mock.Setup(transactor => transactor.GetOutputCommunicationSerializationType())
            .Returns(useBinarySerialization ? SerializationType.Binary : null);
        mock.Setup(transactor => transactor.Transact(It.IsAny<Data<object>>()))
            .Returns<Data<object>>(data =>
            {
                var responseValue = $"tx-response-{callIndex++}";
                registry.Record(actionName, Channels.TransactionInput, data.Body);
                var outputBody = MaybeSerialize(responseValue, useBinarySerialization);
                registry.Record(actionName, Channels.TransactionOutput, outputBody);
                return new Tuple<DetailedData<object>, DetailedData<object>?>(
                    new DetailedData<object> { Body = data.Body, Timestamp = DateTime.UtcNow },
                    new DetailedData<object> { Body = outputBody, Timestamp = DateTime.UtcNow });
            });
        return mock;
    }

    private static Mock<IFetcher> CreateFetcherMock(string actionName, MockRuntimeRegistry registry,
        bool useBinarySerialization)
    {
        var mock = new Mock<IFetcher>(MockBehavior.Strict);
        mock.Setup(fetcher => fetcher.GetSerializationType())
            .Returns(useBinarySerialization ? SerializationType.Binary : null);
        mock.Setup(fetcher => fetcher.Collect(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns(() =>
            {
                registry.Record(actionName, Channels.CollectorCall);
                return CollectorOutputs.Select(output =>
                {
                    var body = MaybeSerialize(output, useBinarySerialization);
                    registry.Record(actionName, Channels.CollectorItem, body);
                    return new DetailedData<object> { Body = body };
                });
            });
        return mock;
    }

    private static void AssertScenario(SessionData sessionData, MockRuntimeRegistry registry,
        SessionScenarioDefinition scenario)
    {
        Assert.Multiple(() =>
        {
            Assert.That(sessionData.SessionFailures, Is.Empty);

            if (scenario.Publisher != null)
            {
                var expectedInputs = Enumerable.Range(0, scenario.Publisher.Iterations)
                    .SelectMany(_ => GetSelectedPayloads(scenario.Publisher.UseDataSourcePatterns))
                    .ToArray();
                var publishedBodies = registry.Values(scenario.Publisher.ActionName, Channels.PublisherItem);
                Assert.That(publishedBodies, Has.Count.EqualTo(expectedInputs.Length));
                if (scenario.Publisher.UseBinarySerialization)
                {
                    Assert.That(publishedBodies, Has.All.InstanceOf<byte[]>());
                }
                else
                {
                    Assert.That(publishedBodies.Select(value => value!.ToString()).ToArray(),
                        Is.EquivalentTo(expectedInputs));
                }
                Assert.That(GetBodies(sessionData.Inputs, scenario.Publisher.ActionName),
                    Is.EquivalentTo(expectedInputs));

                if (scenario.Publisher.UseChunkSending)
                {
                    var expectedChunkCalls = (int)Math.Ceiling(expectedInputs.Length / 2d);
                    Assert.That(registry.Count(scenario.Publisher.ActionName, Channels.PublisherChunkCall),
                        Is.EqualTo(expectedChunkCalls));
                }
            }

            if (scenario.Consumer != null)
            {
                Assert.That(GetBodies(sessionData.Outputs, scenario.Consumer.ActionName), Is.EqualTo(ConsumerOutputs));
                Assert.That(registry.Count(scenario.Consumer.ActionName, Channels.ConsumerItem),
                    Is.EqualTo(ConsumerOutputs.Length));
                if (scenario.Consumer.UseChunkReading)
                {
                    Assert.That(registry.Count(scenario.Consumer.ActionName, Channels.ConsumerChunkCall), Is.EqualTo(1));
                }
            }

            if (scenario.Transaction != null)
            {
                var expectedInputs = Enumerable.Range(0, scenario.Transaction.Iterations)
                    .SelectMany(_ => GetSelectedPayloads(scenario.Transaction.UseDataSourcePatterns))
                    .ToArray();
                var expectedOutputs = Enumerable.Range(0, expectedInputs.Length)
                    .Select(index => $"tx-response-{index}")
                    .ToArray();
                var transactedBodies = registry.Values(scenario.Transaction.ActionName, Channels.TransactionInput);
                Assert.That(transactedBodies, Has.Count.EqualTo(expectedInputs.Length));
                if (scenario.Transaction.UseBinarySerialization)
                {
                    Assert.That(transactedBodies, Has.All.InstanceOf<byte[]>());
                }
                else
                {
                    Assert.That(transactedBodies.Select(value => value!.ToString()).ToArray(),
                        Is.EquivalentTo(expectedInputs));
                }
                Assert.That(GetBodies(sessionData.Inputs, scenario.Transaction.ActionName),
                    Is.EquivalentTo(expectedInputs));
                Assert.That(GetBodies(sessionData.Outputs, scenario.Transaction.ActionName),
                    Is.EquivalentTo(expectedOutputs));
            }

            if (scenario.Collector != null)
            {
                Assert.That(registry.Count(scenario.Collector.ActionName, Channels.CollectorCall), Is.EqualTo(1));
                Assert.That(GetBodies(sessionData.Outputs, scenario.Collector.ActionName),
                    Is.EqualTo(CollectorOutputs));
            }

            if (scenario.Mocker != null)
            {
                Assert.That(registry.Count(scenario.Mocker.ActionName, Channels.MockerCall), Is.EqualTo(1));
                if (scenario.Mocker.Flavor == CommandFlavor.Consume)
                {
                    Assert.That(GetBodies(sessionData.Inputs, scenario.Mocker.ActionName), Is.EqualTo(MockerInputs));
                    Assert.That(GetBodies(sessionData.Outputs, scenario.Mocker.ActionName), Is.EqualTo(MockerOutputs));
                }
                else
                {
                    Assert.That(GetBodies(sessionData.Inputs, scenario.Mocker.ActionName), Is.Empty);
                    Assert.That(GetBodies(sessionData.Outputs, scenario.Mocker.ActionName), Is.Empty);
                }
            }
        });
    }

    private static void AssertStageLayoutLogged(string sessionLog, string sessionName, ActionStageLayout layout)
    {
        var expectedStageCounts = new Dictionary<int, int>();

        AddStageCount(expectedStageCounts, layout.ConsumerStage);
        AddStageCount(expectedStageCounts, layout.PublisherStage);
        AddStageCount(expectedStageCounts, layout.TransactionStage);
        AddStageCount(expectedStageCounts, layout.MockerStage);

        Assert.Multiple(() =>
        {
            foreach (var expectedStageCount in expectedStageCounts.OrderBy(item => item.Key))
            {
                Assert.That(sessionLog, Does.Contain(
                    $"Starting action stage {expectedStageCount.Key} for session {sessionName} with {expectedStageCount.Value} action(s)"));
                Assert.That(sessionLog, Does.Contain(
                    $"Finished action stage {expectedStageCount.Key} for session {sessionName}"));
            }
        });
    }

    private static void AddStageCount(IDictionary<int, int> expectedStageCounts, int stage)
    {
        if (!expectedStageCounts.TryAdd(stage, 1))
        {
            expectedStageCounts[stage]++;
        }
    }

    private static string[] GetBodies(IEnumerable<CommunicationData<object>>? communicationData, string actionName)
    {
        return communicationData?
            .Where(data => data.Name == actionName)
            .SelectMany(data => data.Data)
            .OfType<DetailedData<object>>()
            .Select(item => NormalizeBody(item.Body))
            .ToArray() ?? [];
    }

    private static string NormalizeBody(object? body)
    {
        if (body is byte[] bytes)
        {
            try
            {
                return new QaaS.Framework.Serialization.Deserializers.Binary()
                           .Deserialize(bytes, typeof(string))
                           ?.ToString() ?? string.Empty;
            }
            catch
            {
                return Convert.ToBase64String(bytes);
            }
        }

        return body?.ToString() ?? string.Empty;
    }

    private static string[] GetSelectedPayloads(bool usePatterns)
    {
        return ScenarioDataSources
            .Where(dataSource => usePatterns
                ? IncludedDataSourcePatterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(dataSource.Name,
                    pattern))
                : IncludedDataSourceNames.Contains(dataSource.Name, StringComparer.Ordinal))
            .SelectMany(dataSource => dataSource.Payloads)
            .ToArray();
    }

    private static object MaybeSerialize(string value, bool useBinarySerialization)
    {
        if (!useBinarySerialization)
        {
            return value;
        }

        var serializer = SerializerFactory.BuildSerializer(SerializationType.Binary);
        return serializer?.Serialize(value) as byte[]
               ?? throw new InvalidOperationException("Failed to create binary test payload.");
    }

    private static string BuildYamlConfiguration(IEnumerable<SessionScenarioDefinition> scenarios)
    {
        var lines = new List<string>
        {
            "MetaData:",
            "  Team: \"Smoke\"",
            "  System: \"QaaS\"",
            "Sessions:"
        };

        foreach (var scenario in scenarios)
        {
            lines.Add($"  - Name: \"{scenario.Name}\"");
            lines.Add("    Stage: 0");
            lines.Add($"    SaveData: {scenario.SaveData.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(scenario.Category))
            {
                lines.Add($"    Category: \"{scenario.Category}\"");
            }

            if (scenario.Publisher != null)
            {
                lines.Add("    Publishers:");
                lines.Add($"      - Name: \"{scenario.Publisher.ActionName}\"");
                if (scenario.Publisher.UseDataSourcePatterns)
                {
                    lines.Add("        DataSourcePatterns:");
                    foreach (var pattern in IncludedDataSourcePatterns)
                    {
                        lines.Add($"          - \"{pattern}\"");
                    }
                }
                else
                {
                    lines.Add("        DataSourceNames:");
                    foreach (var name in IncludedDataSourceNames)
                    {
                        lines.Add($"          - \"{name}\"");
                    }
                }

                lines.Add($"        Iterations: {scenario.Publisher.Iterations}");
                if (scenario.Publisher.Parallelism.HasValue)
                {
                    lines.Add("        Parallel:");
                    lines.Add($"          Parallelism: {scenario.Publisher.Parallelism.Value}");
                }

                if (scenario.Publisher.UseChunkSending)
                {
                    lines.Add("        Chunk:");
                    lines.Add("          ChunkSize: 2");
                }

                if (scenario.Publisher.UseBinarySerialization)
                {
                    lines.Add("        Serialize:");
                    lines.Add("          Serializer: Binary");
                }

                lines.AddRange(ToYamlLines(scenario.Publisher.ConfigKey, scenario.Publisher.Configuration, 8));
            }

            if (scenario.Consumer != null)
            {
                lines.Add("    Consumers:");
                lines.Add($"      - Name: \"{scenario.Consumer.ActionName}\"");
                lines.Add($"        TimeoutMs: {MinimalActionTimeoutMs}");
                if (scenario.Consumer.UseBinarySerialization)
                {
                    lines.Add("        Deserialize:");
                    lines.Add("          Deserializer: Binary");
                }

                lines.AddRange(ToYamlLines(scenario.Consumer.ConfigKey, scenario.Consumer.Configuration, 8));
            }

            if (scenario.Transaction != null)
            {
                lines.Add("    Transactions:");
                lines.Add($"      - Name: \"{scenario.Transaction.ActionName}\"");
                lines.Add($"        TimeoutMs: {MinimalActionTimeoutMs}");
                lines.Add($"        Iterations: {scenario.Transaction.Iterations}");
                if (scenario.Transaction.UseDataSourcePatterns)
                {
                    lines.Add("        DataSourcePatterns:");
                    foreach (var pattern in IncludedDataSourcePatterns)
                    {
                        lines.Add($"          - \"{pattern}\"");
                    }
                }
                else
                {
                    lines.Add("        DataSourceNames:");
                    foreach (var name in IncludedDataSourceNames)
                    {
                        lines.Add($"          - \"{name}\"");
                    }
                }

                if (scenario.Transaction.UseBinarySerialization)
                {
                    lines.Add("        InputSerialize:");
                    lines.Add("          Serializer: Binary");
                    lines.Add("        OutputDeserialize:");
                    lines.Add("          Deserializer: Binary");
                }

                lines.AddRange(ToYamlLines(scenario.Transaction.ConfigKey, scenario.Transaction.Configuration, 8));
            }

            if (scenario.Collector != null)
            {
                lines.Add("    Collectors:");
                lines.Add($"      - Name: \"{scenario.Collector.ActionName}\"");
                lines.AddRange(ToYamlLines(scenario.Collector.ConfigKey, scenario.Collector.Configuration, 8));
            }

            if (scenario.Mocker != null)
            {
                lines.Add("    MockerCommands:");
                lines.Add($"      - Name: \"{scenario.Mocker.ActionName}\"");
                lines.Add("        ServerName: \"test-server\"");
                lines.Add("        Redis:");
                lines.Add("          Host: \"localhost:6379\"");
                lines.Add($"        RequestDurationMs: {MinimalMockerRequestDurationMs}");
                lines.Add("        RequestRetries: 1");
                lines.AddRange(ToYamlLines("Command", scenario.Mocker.Command, 8));
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static IEnumerable<string> ToYamlLines(string propertyName, object? value, int indent)
    {
        var prefix = new string(' ', indent);
        if (value == null)
        {
            yield break;
        }

        if (TryFormatScalar(value, out var scalar))
        {
            yield return $"{prefix}{propertyName}: {scalar}";
            yield break;
        }

        if (IsEmptyYamlObject(value))
        {
            yield return $"{prefix}{propertyName}: {{}}";
            yield break;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var items = enumerable.Cast<object?>().ToArray();
            if (items.Length == 0)
            {
                yield return $"{prefix}{propertyName}: []";
                yield break;
            }

            yield return $"{prefix}{propertyName}:";
            foreach (var item in items)
            {
                if (item == null)
                {
                    yield return $"{prefix}  - null";
                    continue;
                }

                if (TryFormatScalar(item, out var itemScalar))
                {
                    yield return $"{prefix}  - {itemScalar}";
                    continue;
                }

                if (IsEmptyYamlObject(item))
                {
                    yield return $"{prefix}  - {{}}";
                    continue;
                }

                yield return $"{prefix}  -";
                foreach (var line in ToYamlObjectLines(item, indent + 4))
                {
                    yield return line;
                }
            }

            yield break;
        }

        yield return $"{prefix}{propertyName}:";
        foreach (var line in ToYamlObjectLines(value, indent + 2))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> ToYamlObjectLines(object value, int indent)
    {
        var properties = GetYamlProperties(value);
        if (properties.Length == 0)
        {
            properties = GetPlaceholderYamlProperties(value);
        }

        if (properties.Length == 0)
        {
            yield return $"{new string(' ', indent - 2)}{{}}";
            yield break;
        }

        foreach (var property in properties)
        {
            foreach (var line in ToYamlLines(property.Name, property.Value, indent))
            {
                yield return line;
            }
        }
    }

    private static bool TryFormatScalar(object value, out string scalar)
    {
        switch (value)
        {
            case string stringValue:
                scalar = $"\"{stringValue.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
                return true;
            case bool boolValue:
                scalar = boolValue.ToString().ToLowerInvariant();
                return true;
            case Enum enumValue:
                scalar = enumValue.ToString();
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                scalar = Convert.ToString(value, CultureInfo.InvariantCulture)!;
                return true;
            default:
                scalar = string.Empty;
                return false;
        }
    }

    private static bool IsEmptyYamlObject(object value)
    {
        return value is not IEnumerable || value is string
            ? GetYamlProperties(value).Length == 0 && GetPlaceholderYamlProperties(value).Length == 0
            : false;
    }

    private static (string Name, object Value)[] GetYamlProperties(object value)
    {
        return GetYamlReflectionProperties(value.GetType())
            .Select(property => (
                property.Name,
                Value: property.GetValue(value)
            ))
            .Where(property => property.Value != null)
            .Select(property => (property.Name, property.Value!))
            .ToArray();
    }

    private static (string Name, object Value)[] GetPlaceholderYamlProperties(object value)
    {
        return GetYamlReflectionProperties(value.GetType())
            .Where(property => property.CanWrite || HasAutoPropertyBackingField(property))
            .Select(property => (property.Name, Value: CreatePlaceholderValue(property.PropertyType)))
            .Where(property => property.Value != null)
            .ToArray()!;
    }

    private static IEnumerable<PropertyInfo> GetYamlReflectionProperties(Type type)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var currentType = type; currentType != null && currentType != typeof(object);
             currentType = currentType.BaseType)
        {
            foreach (var property in currentType.GetProperties(BindingFlags.Instance | BindingFlags.Public |
                                                               BindingFlags.NonPublic |
                                                               BindingFlags.DeclaredOnly))
            {
                if (!seenNames.Add(property.Name) || !property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (property.GetMethod?.IsPublic == true || property.CanWrite || HasAutoPropertyBackingField(property))
                {
                    yield return property;
                }
            }
        }
    }

    private static bool HasAutoPropertyBackingField(PropertyInfo property)
    {
        const BindingFlags backingFieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        return property.DeclaringType?.GetField($"<{property.Name}>k__BackingField", backingFieldFlags) != null ||
               property.DeclaringType?.GetField($"<{property.Name}>i__Field", backingFieldFlags) != null;
    }

    private static object? CreatePlaceholderValue(Type type)
    {
        if (type == typeof(string))
        {
            return "placeholder";
        }

        if (type == typeof(bool) || type == typeof(bool?))
        {
            return true;
        }

        if (type == typeof(int) || type == typeof(int?))
        {
            return 1;
        }

        if (type == typeof(long) || type == typeof(long?))
        {
            return 1L;
        }

        if (type == typeof(uint) || type == typeof(uint?))
        {
            return 1U;
        }

        if (type == typeof(ulong) || type == typeof(ulong?))
        {
            return 1UL;
        }

        if (type == typeof(double) || type == typeof(double?))
        {
            return 1D;
        }

        if (type == typeof(float) || type == typeof(float?))
        {
            return 1F;
        }

        if (type == typeof(decimal) || type == typeof(decimal?))
        {
            return 1M;
        }

        if (type != typeof(string) && typeof(IEnumerable<string>).IsAssignableFrom(type))
        {
            return new[] { "placeholder" };
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(0);
        }

        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType?.IsEnum == true)
        {
            return Enum.GetValues(underlyingType).GetValue(0);
        }

        return null;
    }

    private sealed class StaticGenerator(IReadOnlyList<string> payloads) : IGenerator
    {
        public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
        {
            return null;
        }

        public Context Context { get; set; } = null!;

        public IEnumerable<Data<object>> Generate(IImmutableList<SessionData> sessionDataList,
            IImmutableList<DataSource> dataSourceList)
        {
            return payloads.Select(payload => new Data<object> { Body = payload });
        }
    }

    private sealed class FakeMockerAction(string name, int stage, MockRuntimeRegistry registry, CommandFlavor flavor,
        bool useBinarySerialization)
        : StagedAction(name, stage, null, Globals.Logger)
    {
        private RunningCommunicationData<object> _inputs = null!;
        private RunningCommunicationData<object> _outputs = null!;

        internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
        {
            _inputs = new RunningCommunicationData<object>
            {
                Name = Name,
                SerializationType = useBinarySerialization ? SerializationType.Binary : null
            };
            _outputs = new RunningCommunicationData<object>
            {
                Name = Name,
                SerializationType = useBinarySerialization ? SerializationType.Binary : null
            };
            var runningSession = context.GetRunningSession(sessionName);
            runningSession.Inputs!.Add(_inputs);
            runningSession.Outputs!.Add(_outputs);
        }

        internal override InternalCommunicationData<object> Act()
        {
            registry.Record(Name, Channels.MockerCall);
            IReadOnlyList<DetailedData<object>>? inputs = null;
            IReadOnlyList<DetailedData<object>>? outputs = null;

            if (flavor == CommandFlavor.Consume)
            {
                inputs = MockerInputs
                    .Select(input => new DetailedData<object> { Body = MaybeSerialize(input, useBinarySerialization) })
                    .ToArray();
                outputs = MockerOutputs
                    .Select(output => new DetailedData<object> { Body = MaybeSerialize(output, useBinarySerialization) })
                    .ToArray();

                foreach (var input in inputs)
                {
                    _inputs.Data.Add(input);
                    _inputs.Queue.Enqueue(input);
                }

                foreach (var output in outputs)
                {
                    _outputs.Data.Add(output);
                    _outputs.Queue.Enqueue(output);
                }
            }

            _inputs.Data.CompleteAdding();
            _outputs.Data.CompleteAdding();

            return new InternalCommunicationData<object>
            {
                Input = inputs?.ToList(),
                Output = outputs?.Cast<DetailedData<object>?>().ToList(),
                InputSerializationType = useBinarySerialization ? SerializationType.Binary : null,
                OutputSerializationType = useBinarySerialization ? SerializationType.Binary : null
            };
        }

        protected internal override void LogData(InternalCommunicationData<object> actData,
            DetailedData<object> itemBeforeSerialization, InputOutputState? saveAt = null)
        {
        }
    }

    private sealed class MockRuntimeRegistry
    {
        private readonly ConcurrentBag<ObservedCall> _calls = [];

        public void Record(string actionName, string channel, object? payload = null)
        {
            _calls.Add(new ObservedCall(actionName, channel, payload));
        }

        public int Count(string actionName, string channel)
        {
            return _calls.Count(call => call.ActionName == actionName && call.Channel == channel);
        }

        public IReadOnlyList<object?> Values(string actionName, string channel)
        {
            return _calls
                .Where(call => call.ActionName == actionName && call.Channel == channel)
                .Select(call => call.Payload)
                .ToArray();
        }
    }

    private sealed record ObservedCall(string ActionName, string Channel, object? Payload);

    private sealed record ScenarioDataSource(string Name, IReadOnlyList<string> Payloads);

    private sealed record SessionScenarioDefinition(
        string Name,
        string? Category = null,
        bool SaveData = true,
        PublisherDefinition? Publisher = null,
        ConsumerDefinition? Consumer = null,
        TransactionDefinition? Transaction = null,
        CollectorDefinition? Collector = null,
        MockerDefinition? Mocker = null);

    private sealed record PublisherDefinition(string ActionName, string ConfigKey, ISenderConfig Configuration,
        bool UseChunkSending, bool UseDataSourcePatterns, bool UseBinarySerialization, int Iterations,
        int? Parallelism);

    private sealed record ConsumerDefinition(string ActionName, string ConfigKey, IReaderConfig Configuration,
        bool UseChunkReading, bool UseBinarySerialization, int MessageCount);

    private sealed record TransactionDefinition(string ActionName, string ConfigKey, ITransactorConfig Configuration,
        bool UseDataSourcePatterns, bool UseBinarySerialization, int Iterations);

    private sealed record CollectorDefinition(string ActionName, string ConfigKey, IFetcherConfig Configuration,
        bool UseBinarySerialization);

    private sealed record MockerDefinition(string ActionName, MockerCommandConfig Command, CommandFlavor Flavor,
        bool UseBinarySerialization = false);

    private sealed record ActionStageLayout(string Name, int ConsumerStage, int PublisherStage, int TransactionStage,
        int MockerStage);

    private enum CommandFlavor
    {
        ChangeActionStub,
        TriggerAction,
        Consume
    }

    private enum ScenarioSource
    {
        Code,
        Yaml
    }

    [Flags]
    private enum SessionActionFlags
    {
        Publisher = 1,
        Consumer = 2,
        Transaction = 4,
        Collector = 8,
        Mocker = 16
    }

    private static class Channels
    {
        public const string ConsumerChunkCall = "consumer-chunk-call";
        public const string ConsumerItem = "consumer-item";
        public const string PublisherChunkCall = "publisher-chunk-call";
        public const string PublisherItem = "publisher-item";
        public const string TransactionInput = "transaction-input";
        public const string TransactionOutput = "transaction-output";
        public const string CollectorCall = "collector-call";
        public const string CollectorItem = "collector-item";
        public const string MockerCall = "mocker-call";
    }
}



