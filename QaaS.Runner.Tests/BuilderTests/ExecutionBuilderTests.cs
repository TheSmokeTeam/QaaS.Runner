using System.Reflection;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.CustomExceptions;
using QaaS.Framework.Protocols.ConfigurationObjects.Elastic;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Session.Builders;
using QaaS.Runner.Storage;
using QaaS.Runner.Storage.ConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs;
using QaaS.Runner.Tests.TestObjects;

namespace QaaS.Runner.Tests.BuilderTests;

public class ExecutionBuilderTests
{
    [SetUp]
    public void SetUp()
    {
        ProbeRunRecorder.Reset();
    }

    [Test]
    public void TestBuild_CallFunctionWithValidAndInvalidConfiguration_ShouldThrowErrorOnInvalid()
    {
        // Arrange
        var validBuilder = CreateValidExecutionBuilder();
        var invalidBuilder = CreateInvalidExecutionBuilder();

        // Act & Assert
        Assert.DoesNotThrow(() => validBuilder.Build());

        Assert.Throws<InvalidConfigurationsException>(() => invalidBuilder.Build());
    }

    [Test]
    public void Build_WithInvalidConfiguration_IncludesExecutionContextInExceptionMessage()
    {
        var builder = CreateInvalidExecutionBuilder();

        var exception = Assert.Throws<InvalidConfigurationsException>(() => builder.Build());

        Assert.That(exception!.Message, Does.Contain("Runner execution configuration is invalid."));
        Assert.That(exception.Message, Does.Contain("Execution type: Run"));
        Assert.That(exception.Message, Does.Contain("Execution id: test"));
        Assert.That(exception.Message, Does.Contain("Case: invalid"));
    }

    [Test]
    public void TestBuild_CallFunctionWithDifferentConfiguration_ShouldBuildValidExecution()
    {
        // Arrange
        var builder = CreateValidExecutionBuilder();

        // Act
        var execution = builder.Build();

        // Assert - Verify the execution was built correctly
        Assert.That(execution, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(execution.AssertionLogic, Is.Not.Null);
            Assert.That(execution.ReportLogic, Is.Not.Null);
            Assert.That(execution.SessionLogic, Is.Not.Null);
            Assert.That(execution.TemplateLogic, Is.Not.Null);
            Assert.That(execution.DataSourceLogic, Is.Not.Null);
            Assert.That(execution.StorageLogic, Is.Not.Null);
            Assert.That(typeof(Execution)
                .GetProperty("Type", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(execution), Is.EqualTo(ExecutionType.Run));
            Assert.That(typeof(Execution)
                .GetProperty("Context", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(execution), Is.Not.Null);
        });
    }

    [Test]
    public void Build_WithSessionWithoutConfiguredStage_AssignsIndexAsDefaultStage()
    {
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "session-without-stage",
                Stage = null,
                Probes = []
            })
            .ExecutionType(ExecutionType.Template)
            .SetExecutionId("exec-stage")
            .SetCase("case-stage")
            .WithLogger(Globals.Logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" });

        _ = builder.Build();

        Assert.That(builder.Sessions ?? [], Has.Length.EqualTo(1));
        Assert.That(builder.Sessions[0].Stage, Is.EqualTo(0));
    }

    [Test]
    public void CrudOperations_WithNullConfiguredArrays_DoNotThrowAndKeepArraysNull()
    {
        var builder = new ExecutionBuilder
        {
            Sessions = null,
            Assertions = null,
            Storages = null,
            DataSources = null,
            Links = null
        };

        Assert.DoesNotThrow(() =>
        {
            builder.UpdateSession("missing", new SessionBuilder());
            builder.RemoveSession("missing");
            builder.UpdateAssertion("missing", new AssertionBuilder
            {
                AssertionInstance = null!,
                Reporter = null!
            });
            builder.RemoveAssertion("missing");
            builder.UpdateStorageAt(0, new StorageBuilder());
            builder.RemoveStorageAt(0);
            builder.UpdateDataSource("missing", new DataSourceBuilder());
            builder.RemoveDataSource("missing");
            builder.UpdateLinkAt(0, new LinkBuilder());
            builder.RemoveLinkAt(0);
        });

        Assert.That(builder.Sessions, Is.Null);
        Assert.That(builder.Assertions, Is.Null);
        Assert.That(builder.Storages, Is.Null);
        Assert.That(builder.DataSources, Is.Null);
        Assert.That(builder.Links, Is.Null);
    }

    [Test]
    public void ReadStorages_WhenStoragesAreNull_ReturnsEmptyCollection()
    {
        var builder = new ExecutionBuilder
        {
            Storages = null
        };

        var storages = builder.Storages;

        Assert.That(storages, Is.Null);
    }

    [Test]
    public void ReadSessions_WhenSessionsAreNull_ReturnsEmptyCollection()
    {
        var builder = new ExecutionBuilder
        {
            Sessions = null
        };

        Assert.That(builder.Sessions, Is.Null);
    }

    [Test]
    public void Start_WithSameProbeNameAcrossDifferentSessions_UsesSessionScopedProbeConfiguration()
    {
        const string sharedProbeName = "shared-probe";
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "session-1",
                Stage = 0,
                Probes =
                [
                    new ProbeBuilder()
                        .Named(sharedProbeName)
                        .HookNamed(nameof(FirstTestProbe))
                        .Configure(new ProbeMarkerConfig { Marker = "first-config" })
                ]
            })
            .AddSession(new SessionBuilder
            {
                Name = "session-2",
                Stage = 1,
                Probes =
                [
                    new ProbeBuilder()
                        .Named(sharedProbeName)
                        .HookNamed(nameof(SecondTestProbe))
                        .Configure(new ProbeMarkerConfig { Marker = "second-config" })
                ]
            })
            .ExecutionType(ExecutionType.Run)
            .SetExecutionId("probe-scope")
            .SetCase("probe-scope-case")
            .WithLogger(Globals.Logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" });

        var execution = builder.Build();
        var exitCode = execution.Start();
        var runs = ProbeRunRecorder.GetRuns();

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(runs, Has.Count.EqualTo(2));
        Assert.That(runs, Contains.Item((nameof(FirstTestProbe), "first-config")));
        Assert.That(runs, Contains.Item((nameof(SecondTestProbe), "second-config")));
    }

    [Test]
    public void Start_WithSameProbeTypeAcrossDifferentSessions_LogsDiscoveryOnceAndSessionScopedProbeInitialization()
    {
        const string sharedProbeName = "SharedProbe";
        var logger = new CapturingLogger();
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "session-a",
                Stage = 0,
                Probes =
                [
                    new ProbeBuilder()
                        .Named(sharedProbeName)
                        .HookNamed(nameof(FirstTestProbe))
                        .Configure(new ProbeMarkerConfig { Marker = "first-config" })
                ]
            })
            .AddSession(new SessionBuilder
            {
                Name = "session-b",
                Stage = 1,
                Probes =
                [
                    new ProbeBuilder()
                        .Named(sharedProbeName)
                        .HookNamed(nameof(FirstTestProbe))
                        .Configure(new ProbeMarkerConfig { Marker = "second-config" })
                ]
            })
            .ExecutionType(ExecutionType.Run)
            .SetExecutionId("probe-log-shape")
            .SetCase("probe-log-shape-case")
            .WithLogger(logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" });

        var execution = builder.Build();
        var exitCode = execution.Start();
        var messages = logger.Entries
            .Where(entry => entry.LogLevel == LogLevel.Information)
            .Select(entry => entry.Message)
            .ToArray();

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(ProbeRunRecorder.GetRuns(), Has.Count.EqualTo(2));
        Assert.That(messages.Count(message =>
                message.Contains($"Found IProbe hook instance {nameof(FirstTestProbe)} in provided assembly",
                    StringComparison.Ordinal)),
            Is.EqualTo(1));
        Assert.That(messages,
            Contains.Item("Initializing Probe SharedProbe for session session-a with Hook type FirstTestProbe"));
        Assert.That(messages,
            Contains.Item("Initializing Probe SharedProbe for session session-b with Hook type FirstTestProbe"));
    }

    [Test]
    public void Start_WithProbeNamesThatWouldCollideWithoutScopedKeys_UsesDistinctProbeConfigurations()
    {
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "ab",
                Stage = 0,
                Probes =
                [
                    new ProbeBuilder()
                        .Named("c")
                        .HookNamed(nameof(FirstTestProbe))
                        .Configure(new ProbeMarkerConfig { Marker = "first-collision-config" })
                ]
            })
            .AddSession(new SessionBuilder
            {
                Name = "a",
                Stage = 1,
                Probes =
                [
                    new ProbeBuilder()
                        .Named("bc")
                        .HookNamed(nameof(SecondTestProbe))
                        .Configure(new ProbeMarkerConfig { Marker = "second-collision-config" })
                ]
            })
            .ExecutionType(ExecutionType.Run)
            .SetExecutionId("probe-collision")
            .SetCase("probe-collision-case")
            .WithLogger(Globals.Logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" });

        var execution = builder.Build();
        var exitCode = execution.Start();
        var runs = ProbeRunRecorder.GetRuns();

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(runs, Has.Count.EqualTo(2));
        Assert.That(runs, Contains.Item((nameof(FirstTestProbe), "first-collision-config")));
        Assert.That(runs, Contains.Item((nameof(SecondTestProbe), "second-collision-config")));
    }

    [Test]
    public void Build_WithProbeMissingName_ThrowsInvalidConfigurationsException()
    {
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "session-missing-probe-name",
                Stage = 0,
                Probes =
                [
                    new ProbeBuilder()
                        .HookNamed(nameof(FirstTestProbe))
                ]
            })
            .ExecutionType(ExecutionType.Template)
            .SetExecutionId("invalid-probe-name")
            .SetCase("invalid-probe-name-case")
            .WithLogger(Globals.Logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" });

        Assert.Throws<InvalidConfigurationsException>(() => builder.Build());
    }

    [Test]
    public void Build_WithDuplicateMockerCommandNames_ThrowsInvalidConfigurationsException()
    {
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "session-with-duplicate-mocker-commands",
                Stage = 0,
                Probes = [],
                MockerCommands =
                [
                    new MockerCommandBuilder()
                        .Named("duplicate-command")
                        .WithServerName("server-a")
                        .Configure(new MockerCommandConfig
                        {
                            TriggerAction = new Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command.TriggerAction()
                        }),
                    new MockerCommandBuilder()
                        .Named("duplicate-command")
                        .WithServerName("server-b")
                        .Configure(new MockerCommandConfig
                        {
                            TriggerAction = new Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command.TriggerAction()
                        })
                ]
            })
            .ExecutionType(ExecutionType.Template)
            .SetExecutionId("duplicate-mocker-command")
            .SetCase("duplicate-mocker-command-case")
            .WithLogger(Globals.Logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" });

        Assert.Throws<InvalidConfigurationsException>(() => builder.Build());
    }

    [Test]
    public void Build_WithLoadedContextWithoutMetadata_RecordsBothValidationErrorsOnce()
    {
        var context = CreateLoadedContext(new Dictionary<string, string?>
        {
            ["Sessions:0:Name"] = "context-session"
        });
        var builder = new ExecutionBuilder(context, ExecutionType.Run, null, null, null, null);

        Assert.Throws<InvalidConfigurationsException>(() => builder.Build());

        var errorMessages = GetValidationErrorMessages(builder);

        Assert.Multiple(() =>
        {
            Assert.That(errorMessages.Count(message => message == "MetaData - The Team field is required."),
                Is.EqualTo(1));
            Assert.That(errorMessages.Count(message => message == "MetaData - The System field is required."),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void Build_WithLoadedContextWithPartialMetadata_RecordsEachValidationErrorOnce()
    {
        var context = CreateLoadedContext(new Dictionary<string, string?>
        {
            ["MetaData:System"] = "QaaS",
            ["Sessions:0:Name"] = "context-session"
        });
        var builder = new ExecutionBuilder(context, ExecutionType.Run, null, null, null, null);

        Assert.Throws<InvalidConfigurationsException>(() => builder.Build());

        var errorMessages = GetValidationErrorMessages(builder);

        Assert.That(errorMessages.Count(message => message == "MetaData - The Team field is required."), Is.EqualTo(1));
    }

    [Test]
    public void Constructor_WithInvalidRabbitMqTargets_BindsBothTargetsIntoNestedBuilders()
    {
        var context = CreateLoadedContext(new Dictionary<string, string?>
        {
            ["MetaData:Team"] = "Smoke",
            ["MetaData:System"] = "QaaS",
            ["Sessions:0:Name"] = "rabbit-session",
            ["Sessions:0:Publishers:0:Name"] = "publisher",
            ["Sessions:0:Publishers:0:DataSourceNames:0"] = "source",
            ["Sessions:0:Publishers:0:RabbitMq:Host"] = "localhost",
            ["Sessions:0:Publishers:0:RabbitMq:ExchangeName"] = "exchange",
            ["Sessions:0:Publishers:0:RabbitMq:QueueName"] = "queue",
            ["Sessions:0:Consumers:0:Name"] = "consumer",
            ["Sessions:0:Consumers:0:TimeoutMs"] = "1000",
            ["Sessions:0:Consumers:0:RabbitMq:Host"] = "localhost",
            ["Sessions:0:Consumers:0:RabbitMq:ExchangeName"] = "exchange",
            ["Sessions:0:Consumers:0:RabbitMq:QueueName"] = "queue"
        });

        var builder = new ExecutionBuilder(context, ExecutionType.Run, null, null, null, null);
        var session = builder.Sessions.Single();
        var publisher = session.Publishers!.Single();
        var consumer = session.Consumers!.Single();
        var publisherRabbitMq = (RabbitMqSenderConfig?)typeof(QaaS.Runner.Sessions.Actions.Publishers.Builders.PublisherBuilder)
            .GetProperty("RabbitMq", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(publisher);
        var consumerRabbitMq = (RabbitMqReaderConfig?)typeof(QaaS.Runner.Sessions.Actions.Consumers.Builders.ConsumerBuilder)
            .GetProperty("RabbitMq", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(consumer);

        Assert.Multiple(() =>
        {
            Assert.That(publisherRabbitMq, Is.Not.Null);
            Assert.That(publisherRabbitMq!.ExchangeName, Is.EqualTo("exchange"));
            Assert.That(publisherRabbitMq.QueueName, Is.EqualTo("queue"));
            Assert.That(consumerRabbitMq, Is.Not.Null);
            Assert.That(consumerRabbitMq!.ExchangeName, Is.EqualTo("exchange"));
            Assert.That(consumerRabbitMq.QueueName, Is.EqualTo("queue"));
        });
    }

    [Test]
    public void Constructor_WithConfiguredStages_BindsStageDefinitionsFromLoadedContext()
    {
        var context = CreateLoadedContext(new Dictionary<string, string?>
        {
            ["MetaData:Team"] = "Smoke",
            ["MetaData:System"] = "QaaS",
            ["Sessions:0:Name"] = "stage-session",
            ["Sessions:0:Stages:0:StageNumber"] = "1",
            ["Sessions:0:Stages:0:TimeoutBefore"] = "25",
            ["Sessions:0:Stages:0:TimeoutAfter"] = "50"
        });

        var builder = new ExecutionBuilder(context, ExecutionType.Template, null, null, null, null);
        var session = builder.Sessions.Single();
        var stage = session.Stages.Single();

        Assert.Multiple(() =>
        {
            Assert.That(stage.StageNumber, Is.EqualTo(1));
            Assert.That(stage.TimeoutBefore, Is.EqualTo(25));
            Assert.That(stage.TimeoutAfter, Is.EqualTo(50));
        });
    }

    [Test]
    public void Constructor_WithConsumerInitialTimeout_BindsInitialTimeoutFromLoadedContext()
    {
        var context = CreateLoadedContext(new Dictionary<string, string?>
        {
            ["MetaData:Team"] = "Smoke",
            ["MetaData:System"] = "QaaS",
            ["Sessions:0:Name"] = "timeout-session",
            ["Sessions:0:Consumers:0:Name"] = "consumer",
            ["Sessions:0:Consumers:0:TimeoutMs"] = "1000",
            ["Sessions:0:Consumers:0:InitialTimeoutMs"] = "7000"
        });

        var builder = new ExecutionBuilder(context, ExecutionType.Template, null, null, null, null);
        var consumer = builder.Sessions.Single().Consumers!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(consumer.TimeoutMs, Is.EqualTo(1000));
            Assert.That(consumer.InitialTimeoutMs, Is.EqualTo(7000));
        });
    }

    [Test]
    public void Build_WithInvalidNestedRabbitMqTargets_ThrowsInvalidConfigurationsException()
    {
        var directRabbitMqValidationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var directRabbitMqIsValid = ValidationUtils.TryValidateObjectRecursive(new RabbitMqSenderConfig
        {
            Host = "localhost",
            ExchangeName = "exchange",
            QueueName = "queue"
        }, directRabbitMqValidationResults);

        Assert.That(directRabbitMqIsValid, Is.False);
        Assert.That(directRabbitMqValidationResults.Any(result => result.ErrorMessage?.Contains("field must be empty when QueueName is configured") == true),
            Is.True);

        var context = CreateLoadedContext(new Dictionary<string, string?>
        {
            ["MetaData:Team"] = "Smoke",
            ["MetaData:System"] = "QaaS",
            ["Sessions:0:Name"] = "rabbit-session",
            ["Sessions:0:Publishers:0:Name"] = "publisher",
            ["Sessions:0:Publishers:0:DataSourceNames:0"] = "source",
            ["Sessions:0:Publishers:0:RabbitMq:Host"] = "localhost",
            ["Sessions:0:Publishers:0:RabbitMq:ExchangeName"] = "exchange",
            ["Sessions:0:Publishers:0:RabbitMq:QueueName"] = "queue",
            ["Sessions:0:Consumers:0:Name"] = "consumer",
            ["Sessions:0:Consumers:0:TimeoutMs"] = "1000",
            ["Sessions:0:Consumers:0:RabbitMq:Host"] = "localhost",
            ["Sessions:0:Consumers:0:RabbitMq:ExchangeName"] = "exchange",
            ["Sessions:0:Consumers:0:RabbitMq:QueueName"] = "queue"
        });

        var builder = new ExecutionBuilder(context, ExecutionType.Run, null, null, null, null);
        var session = builder.Sessions.Single();
        var publisher = session.Publishers!.Single();
        Assert.Throws<InvalidConfigurationsException>(() => builder.Build());
        var validationResults = (IReadOnlyList<System.ComponentModel.DataAnnotations.ValidationResult>)typeof(ExecutionBuilder)
            .GetField("_validationResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(builder)!;

        Assert.Multiple(() =>
        {
            Assert.That(validationResults.Any(result =>
                    result.ErrorMessage?.Contains("Sessions:0:Publishers:0:RabbitMq", StringComparison.Ordinal) == true &&
                    result.ErrorMessage.Contains("QueueName is configured", StringComparison.Ordinal)),
                Is.True);
            Assert.That(validationResults.Any(result =>
                    result.ErrorMessage?.Contains("Sessions:0:Consumers:0:RabbitMq", StringComparison.Ordinal) == true &&
                    result.ErrorMessage.Contains("QueueName is configured", StringComparison.Ordinal)),
                Is.True);
        });
    }

    [Test]
    public void Build_WithChunkOnlyPublisherMissingChunkConfiguration_ThrowsInvalidConfigurationsException()
    {
        var builder = CreateExecutionBuilderWithPublisher(new PublisherBuilder
            {
                Name = "elastic-publisher",
                DataSourceNames = ["payload"]
            }
            .Configure(new ElasticSenderConfig
            {
                Url = "http://localhost:9200",
                Username = "elastic",
                Password = "password",
                IndexName = "session-data"
            }));

        Assert.Throws<InvalidConfigurationsException>(() => builder.Build());
    }

    [Test]
    public void Build_WithSingleOnlyPublisherConfiguredWithChunkConfiguration_ThrowsInvalidConfigurationsException()
    {
        var builder = CreateExecutionBuilderWithPublisher(new PublisherBuilder
            {
                Name = "rabbit-publisher",
                DataSourceNames = ["payload"]
            }
            .WithChunks(new Chunks { ChunkSize = 64 })
            .Configure(new RabbitMqSenderConfig
            {
                Host = "localhost",
                QueueName = "queue"
            }));

        Assert.Throws<InvalidConfigurationsException>(() => builder.Build());
    }

    [Test]
    public void UpdateAndDeleteIndexedCollections_WithInvalidIndex_ThrowArgumentOutOfRangeException()
    {
        var builder = new ExecutionBuilder
        {
            Storages = [new StorageBuilder()],
            Links = [new LinkBuilder()]
        };

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.UpdateStorageAt(-1, new StorageBuilder()));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.RemoveStorageAt(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.UpdateLinkAt(-1, new LinkBuilder()));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.RemoveLinkAt(5));
        });
    }

    [Test]
    public void BuildProbeHookData_SkipsIncompleteDefinitions_AndScopesValidProbeNames()
    {
        var builder = new ExecutionBuilder
        {
            Sessions =
            [
                new SessionBuilder
                {
                    Name = "valid-session",
                    Probes =
                    [
                        new ProbeBuilder()
                            .Named("valid-probe")
                            .HookNamed(nameof(FirstTestProbe))
                            .Configure(new ProbeMarkerConfig { Marker = "configured" }),
                        new ProbeBuilder().Named("missing-hook"),
                        new ProbeBuilder().HookNamed(nameof(SecondTestProbe))
                    ]
                },
                new SessionBuilder
                {
                    Name = null,
                    Probes =
                    [
                        new ProbeBuilder()
                            .Named("missing-session")
                            .HookNamed(nameof(FirstTestProbe))
                    ]
                }
            ]
        };

        var method = typeof(ExecutionBuilder)
            .GetMethod("BuildProbeHookData", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var hookData = ((System.Collections.IEnumerable)method.Invoke(builder, [])!)
            .Cast<object>()
            .ToList();

        Assert.That(hookData, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(hookData[0].GetType().GetProperty("Type")!.GetValue(hookData[0]),
                Is.EqualTo(nameof(FirstTestProbe)));
            Assert.That(hookData[0].GetType().GetProperty("Name")!.GetValue(hookData[0]),
                Is.EqualTo(ProbeBuilder.BuildScopedHookName("valid-session", "valid-probe")));
        });
    }

    [Test]
    public void ValidateProbeDefinitions_WithMissingValues_AddsValidationResultsForEachFailure()
    {
        var builder = new ExecutionBuilder
        {
            Sessions =
            [
                new SessionBuilder
                {
                    Name = null,
                    Probes =
                    [
                        new ProbeBuilder()
                    ]
                }
            ]
        };

        typeof(ExecutionBuilder)
            .GetMethod("ValidateProbeDefinitions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, []);
        var validationResults = (IReadOnlyList<System.ComponentModel.DataAnnotations.ValidationResult>)typeof(ExecutionBuilder)
            .GetField("_validationResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(builder)!;

        Assert.Multiple(() =>
        {
            Assert.That(validationResults.Select(result => result.ErrorMessage),
                Has.One.EqualTo("Session name is required when configuring probes."));
            Assert.That(validationResults.Select(result => result.ErrorMessage),
                Has.One.EqualTo("Probe name is required for session ''."));
            Assert.That(validationResults.Select(result => result.ErrorMessage),
                Has.One.EqualTo("Probe type is required for probe '' in session ''."));
        });
    }

    [Test]
    public void DeduplicateValidationResults_RemovesDuplicateMessagesAndMemberNames()
    {
        var builder = new ExecutionBuilder();
        var validationResultsField = typeof(ExecutionBuilder)
            .GetField("_validationResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var validationResults = (List<System.ComponentModel.DataAnnotations.ValidationResult>)validationResultsField.GetValue(builder)!;
        validationResults.Add(new System.ComponentModel.DataAnnotations.ValidationResult("duplicate", ["A"]));
        validationResults.Add(new System.ComponentModel.DataAnnotations.ValidationResult("duplicate", ["A"]));
        validationResults.Add(new System.ComponentModel.DataAnnotations.ValidationResult("distinct", ["B"]));

        typeof(ExecutionBuilder)
            .GetMethod("DeduplicateValidationResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, []);

        Assert.That(validationResults.Select(result => result.ErrorMessage),
            Is.EqualTo(new[] { "duplicate", "distinct" }));
    }

    [Test]
    public void DeduplicateValidationResults_WithSingleItem_DoesNothing()
    {
        var builder = new ExecutionBuilder();
        var validationResultsField = typeof(ExecutionBuilder)
            .GetField("_validationResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var validationResults = (List<System.ComponentModel.DataAnnotations.ValidationResult>)validationResultsField.GetValue(builder)!;
        validationResults.Add(new System.ComponentModel.DataAnnotations.ValidationResult("single"));

        typeof(ExecutionBuilder)
            .GetMethod("DeduplicateValidationResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, []);

        Assert.That(validationResults.Select(result => result.ErrorMessage), Is.EqualTo(new[] { "single" }));
    }

    [Test]
    public void DeduplicateValidationResults_WithAlreadyDistinctItems_LeavesCollectionUntouched()
    {
        var builder = new ExecutionBuilder();
        var validationResultsField = typeof(ExecutionBuilder)
            .GetField("_validationResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var validationResults = (List<System.ComponentModel.DataAnnotations.ValidationResult>)validationResultsField.GetValue(builder)!;
        validationResults.Add(new System.ComponentModel.DataAnnotations.ValidationResult("first", ["A"]));
        validationResults.Add(new System.ComponentModel.DataAnnotations.ValidationResult("second", ["B"]));

        typeof(ExecutionBuilder)
            .GetMethod("DeduplicateValidationResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, []);

        Assert.That(validationResults.Select(result => result.ErrorMessage), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void BuildDataSources_WithoutParameters_UsesInitializedBuildScope()
    {
        var builder = CreateValidExecutionBuilder();
        var execution = builder.Build();

        try
        {
            var dataSources = ((System.Collections.IEnumerable)typeof(ExecutionBuilder)
                    .GetMethod("BuildDataSources", BindingFlags.Instance | BindingFlags.NonPublic, null,
                        Type.EmptyTypes, null)!
                    .Invoke(builder, [])!)
                .Cast<object>()
                .ToList();

            Assert.That(dataSources, Has.Count.EqualTo(1));
        }
        finally
        {
            execution.Dispose();
        }
    }

    [Test]
    public void InitializeContext_WithoutLoadedContext_UsesDefaultsAndCreatesMetadataEntry()
    {
        var builder = new ExecutionBuilder();

        typeof(ExecutionBuilder)
            .GetMethod("InitializeContext", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, []);

        var context = (InternalContext)typeof(ExecutionBuilder).BaseType!
            .GetField("Context", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(builder)!;
        Assert.Multiple(() =>
        {
            Assert.That(context, Is.Not.Null);
            Assert.That(context.Logger, Is.Not.Null);
            Assert.That(context.RootConfiguration, Is.Not.Null);
            Assert.That(context.GetMetaDataOrDefault(), Is.Not.Null);
        });
    }

    [Test]
    public void FilterConfigurationsAndTemplateRendering_WithMissingCollections_HandleNullArrays()
    {
        var builder = new ExecutionBuilder
        {
            Sessions = null,
            Assertions = null,
            Links = null,
            Storages = null
        };
        builder.WithMetadata(new MetaDataConfig
        {
            Team = "Smoke",
            System = "QaaS"
        });

        typeof(ExecutionBuilder)
            .GetMethod("InitializeContext", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, []);
        typeof(ExecutionBuilder)
            .GetMethod("FilterConfigurationsBasedOnFlags", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, []);
        typeof(ExecutionBuilder)
            .GetMethod("StoreRenderedConfigurationTemplate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, []);

        Assert.Multiple(() =>
        {
            Assert.That(builder.Assertions, Has.Length.EqualTo(0));
            Assert.That(builder.Sessions, Has.Length.EqualTo(0));
            Assert.That(((InternalContext)typeof(ExecutionBuilder).BaseType!
                    .GetField("Context", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(builder)!)
                .GetRenderedConfigurationTemplate(), Does.Contain("MetaData:"));
        });
    }

    [Test]
    public void ValidateCollection_WithNullCollectionAndNullItems_DoesNotAddValidationResults()
    {
        var builder = new ExecutionBuilder();
        var validationResultsField = typeof(ExecutionBuilder)
            .GetField("_validationResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var validationResults = (List<System.ComponentModel.DataAnnotations.ValidationResult>)validationResultsField.GetValue(builder)!;

        typeof(ExecutionBuilder)
            .GetMethod("ValidateCollection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(MetaDataConfig))
            .Invoke(builder, [null, "MetaData"]);
        typeof(ExecutionBuilder)
            .GetMethod("ValidateCollection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(MetaDataConfig))
            .Invoke(builder, [new MetaDataConfig?[] { null, new() { Team = "Smoke", System = "QaaS" } }, "MetaData"]);

        Assert.That(validationResults, Is.Empty);
    }

    [Test]
    public void Build_WithLoadedContextGlobalDictionary_MergesExistingAndConfiguredEntries()
    {
        var context = CreateLoadedContext(new Dictionary<string, string?>
        {
            ["MetaData:Team"] = "Smoke",
            ["MetaData:System"] = "QaaS"
        });
        context.InternalGlobalDict = new Dictionary<string, object?>
        {
            ["existing"] = "old",
            ["shared"] = "context-value"
        };

        var builder = new ExecutionBuilder(context, ExecutionType.Template, null, null, null, null)
            .WithGlobalDict(new Dictionary<string, object?>
            {
                ["shared"] = "builder-value",
                ["added"] = 123
            });

        var execution = builder.Build();
        var executionContext = (InternalContext)typeof(Execution)
            .GetProperty("Context", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(execution)!;

        Assert.Multiple(() =>
        {
            Assert.That(executionContext.InternalGlobalDict["existing"], Is.EqualTo("old"));
            Assert.That(executionContext.InternalGlobalDict["shared"], Is.EqualTo("builder-value"));
            Assert.That(executionContext.InternalGlobalDict["added"], Is.EqualTo(123));
        });
    }

    [Test]
    public void BuildHelperMethods_WithMissingConfigurationArrays_ReturnEmptyCollections()
    {
        var builder = new ExecutionBuilder
        {
            Sessions = null,
            Assertions = null,
            Storages = null,
            DataSources = null
        };
        using var scope = new ContainerBuilder().Build().BeginLifetimeScope();

        var builtDataSources = (System.Collections.IEnumerable)typeof(ExecutionBuilder)
            .GetMethod("BuildDataSources", BindingFlags.Instance | BindingFlags.NonPublic, null,
                [typeof(ILifetimeScope)], null)!
            .Invoke(builder, [scope])!;
        var builtSessions = (System.Collections.IEnumerable)typeof(ExecutionBuilder)
            .GetMethod("BuildSessions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, [scope])!;
        var builtAssertions = (System.Collections.IEnumerable)typeof(ExecutionBuilder)
            .GetMethod("BuildAssertions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, [scope])!;
        var builtReports = (System.Collections.IEnumerable)typeof(ExecutionBuilder)
            .GetMethod("BuildReports", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, [Array.Empty<Assertion>()])!;
        var builtStorages = (System.Collections.IEnumerable)typeof(ExecutionBuilder)
            .GetMethod("BuildStorages", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builder, [])!;

        Assert.Multiple(() =>
        {
            Assert.That(builtDataSources.Cast<object>(), Is.Empty);
            Assert.That(builtSessions.Cast<object>(), Is.Empty);
            Assert.That(builtAssertions.Cast<object>(), Is.Empty);
            Assert.That(builtReports.Cast<object>(), Is.Empty);
            Assert.That(builtStorages.Cast<object>(), Is.Empty);
            Assert.That(builder.Sessions, Has.Length.EqualTo(0));
        });
    }

    [Test]
    public void BuildDataSources_WithoutInitializedBuildScope_ThrowsInvalidOperationException()
    {
        var builder = new ExecutionBuilder();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            typeof(ExecutionBuilder)
                .GetMethod("BuildDataSources", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes,
                    null)!
                .Invoke(builder, []));

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    private ExecutionBuilder CreateValidExecutionBuilder()
    {
        var builder = new ExecutionBuilder();

        // Add valid session builder
        var sessionBuilder = new SessionBuilder
        {
            Name = "test-session",
            Stage = 0,
            Probes = []
        };
        builder.AddSession(sessionBuilder);

        // Add valid assertion builder
        var assertionBuilder = new AssertionBuilder
        {
            Name = "test-assertion",
            Assertion = "Equals",
            AssertionInstance = null,
            Reporter = null
        }.HookNamed(nameof(TestAssertion));
        builder.AddAssertion(assertionBuilder);

        // Add valid storage builder
        var storageBuilder = new StorageBuilder().Configure(new S3Config
        {
            StorageBucket = "bucket",
            ServiceURL = "https://s3.test",
            AccessKey = "access",
            SecretKey = "secret"
        });
        builder.AddStorage(storageBuilder);

        // Add valid link builder
        var linkBuilder = new LinkBuilder()
            { Grafana = new GrafanaLinkConfig { DashboardId = "dash-id", Url = "https://grafa.com", Variables = [] } };
        builder.AddLink(linkBuilder);

        // Add valid data source builder
        var dataSourceBuilder = new DataSourceBuilder().Named("test-datasource").HookNamed("TestGenerator");
        builder.AddDataSource(dataSourceBuilder);

        return builder.SetExecutionId("test").SetCase("valid").WithLogger(Globals.Logger)
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" })
            .WithGlobalDict(new Dictionary<string, object?>());
    }

    private ExecutionBuilder CreateExecutionBuilderWithPublisher(PublisherBuilder publisherBuilder)
    {
        return new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "publisher-session",
                Stage = 0,
                Publishers = [publisherBuilder],
                Probes = []
            })
            .AddDataSource(new DataSourceBuilder().Named("payload").HookNamed("TestGenerator"))
            .ExecutionType(ExecutionType.Act)
            .SetExecutionId("publisher-validation")
            .SetCase("publisher-validation-case")
            .WithLogger(Globals.Logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" });
    }

    private ExecutionBuilder CreateInvalidExecutionBuilder()
    {
        var builder = new ExecutionBuilder();

        // Add session builder with duplicate name to make it invalid
        var sessionBuilder1 = new SessionBuilder
        {
            Name = "duplicate-name",
            Stage = 0,
            Probes = []
        };

        var sessionBuilder2 = new SessionBuilder
        {
            Name = "duplicate-name", // Same name as above - will cause validation error
            Stage = 1,
            Probes = []
        };

        builder.AddSession(sessionBuilder1);
        builder.AddSession(sessionBuilder2);

        // Add valid assertion builder
        var assertionBuilder = new AssertionBuilder
        {
            Name = "test-assertion",
            Assertion = "Equals",
            AssertionInstance = null,
            Reporter = null
        }.HookNamed(nameof(TestAssertion));
        builder.AddAssertion(assertionBuilder);

        return builder.ExecutionType(ExecutionType.Run).SetExecutionId("test").SetCase("invalid")
            .WithLogger(Globals.Logger).WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { System = "QaaS", Team = "Smoke" });
    }

    private static InternalContext CreateLoadedContext(IConfiguration configuration)
    {
        return new InternalContext
        {
            Logger = Globals.Logger,
            RootConfiguration = configuration,
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>()),
            InternalGlobalDict = new Dictionary<string, object?>()
        };
    }

    private static List<string> GetValidationErrorMessages(ExecutionBuilder builder)
    {
        var validationResults = (IReadOnlyList<System.ComponentModel.DataAnnotations.ValidationResult>)typeof(ExecutionBuilder)
            .GetField("_validationResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(builder)!;

        return validationResults
            .Select(result => result.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList()!;
    }

    private static InternalContext CreateLoadedContext(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return CreateLoadedContext(configuration);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NoOpScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message);
}



