using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ConfigurationObjects;
using Qaas.Mocker.CommunicationObjects;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Ping;
using QaaS.Framework.Serialization;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.ConfigurationObjects;
using StackExchange.Redis;
using SessionAction = QaaS.Runner.Sessions.Actions.Action;
using CommunicationInputOutputState = QaaS.Framework.SDK.ConfigurationObjects.InputOutputState;

namespace QaaS.Runner.Sessions.Tests.Actions.MockerCommands;

[TestFixture]
public class MockerCommandInternalsTests
{
    private const string SessionName = "session-a";

    [Test]
    public void PingResponseHandler_WithMatchingResponse_AddsServerInstanceAndSetsServerState()
    {
        var command = CreateUninitializedTriggerCommand();

        InvokeNonPublicMethod(typeof(MockerCommand), command, "PingResponseHandler",
            RedisChannel.Literal("ping-response"),
            (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new PingResponse
            {
                Id = "cmd-id",
                ServerName = "server-a",
                ServerInstanceId = "instance-1",
                ServerInputOutputState = InputOutputState.OnlyInput
            }));

        var instances = (IList<string>)GetField(typeof(MockerCommand), command, "_serverInstanceNames");
        var ioState = (InputOutputState?)typeof(MockerCommand)
            .GetProperty("ServerInputOutputState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(command);

        Assert.That(instances, Has.Count.EqualTo(1));
        Assert.That(instances[0], Is.EqualTo("instance-1"));
        Assert.That(ioState, Is.EqualTo(InputOutputState.OnlyInput));
    }

    [Test]
    public void PingResponseHandler_WithDuplicateResponse_AddsServerInstanceOnlyOnce()
    {
        var command = CreateUninitializedTriggerCommand();
        var response = (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new PingResponse
        {
            Id = "cmd-id",
            ServerName = "server-a",
            ServerInstanceId = "instance-1",
            ServerInputOutputState = InputOutputState.OnlyInput
        });

        InvokeNonPublicMethod(typeof(MockerCommand), command, "PingResponseHandler",
            RedisChannel.Literal("ping-response"), response);
        InvokeNonPublicMethod(typeof(MockerCommand), command, "PingResponseHandler",
            RedisChannel.Literal("ping-response"), response);

        var instances = (IList<string>)GetField(typeof(MockerCommand), command, "_serverInstanceNames");

        Assert.That(instances, Has.Count.EqualTo(1));
    }

    [Test]
    public void PingResponseHandler_WithDifferentCommandId_IgnoresResponse()
    {
        var command = CreateUninitializedTriggerCommand();

        InvokeNonPublicMethod(typeof(MockerCommand), command, "PingResponseHandler",
            RedisChannel.Literal("ping-response"),
            (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new PingResponse
            {
                Id = "other-id",
                ServerName = "server-a",
                ServerInstanceId = "instance-1",
                ServerInputOutputState = InputOutputState.OnlyInput
            }));

        var instances = (IList<string>)GetField(typeof(MockerCommand), command, "_serverInstanceNames");
        var ioState = (InputOutputState?)typeof(MockerCommand)
            .GetProperty("ServerInputOutputState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(command);

        Assert.That(instances, Is.Empty);
        Assert.That(ioState, Is.Null);
    }

    [Test]
    public void PingResponseHandler_WithDifferentServerName_IgnoresResponse()
    {
        var command = CreateUninitializedTriggerCommand();

        InvokeNonPublicMethod(typeof(MockerCommand), command, "PingResponseHandler",
            RedisChannel.Literal("ping-response"),
            (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new PingResponse
            {
                Id = "cmd-id",
                ServerName = "other-server",
                ServerInstanceId = "instance-1",
                ServerInputOutputState = InputOutputState.OnlyInput
            }));

        var instances = (IList<string>)GetField(typeof(MockerCommand), command, "_serverInstanceNames");
        var ioState = (InputOutputState?)typeof(MockerCommand)
            .GetProperty("ServerInputOutputState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(command);

        Assert.That(instances, Is.Empty);
        Assert.That(ioState, Is.Null);
    }

    [Test]
    public void PingResponseHandler_WithDifferentServerState_ThrowsInvalidOperationException()
    {
        var command = CreateUninitializedTriggerCommand();

        InvokeNonPublicMethod(typeof(MockerCommand), command, "PingResponseHandler",
            RedisChannel.Literal("ping-response"),
            (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new PingResponse
            {
                Id = "cmd-id",
                ServerName = "server-a",
                ServerInstanceId = "instance-1",
                ServerInputOutputState = InputOutputState.OnlyInput
            }));

        var exception = Assert.Throws<TargetInvocationException>(() =>
            InvokeNonPublicMethod(typeof(MockerCommand), command, "PingResponseHandler",
                RedisChannel.Literal("ping-response"),
                (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new PingResponse
                {
                    Id = "cmd-id",
                    ServerName = "server-a",
                    ServerInstanceId = "instance-2",
                    ServerInputOutputState = InputOutputState.OnlyOutput
                })));

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void CommandResponseHandler_WithSucceededStatus_AddsSuccessfulInstance()
    {
        var command = CreateUninitializedTriggerCommand();

        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandResponseHandler",
            RedisChannel.Literal("command-response"),
            (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new CommandResponse
            {
                Id = "cmd-id",
                Command = CommandType.TriggerAction,
                ServerInstanceId = "instance-1",
                Status = Status.Succeeded
            }));

        var successfulInstances =
            (IList<string>)GetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames");
        Assert.That(successfulInstances, Has.Count.EqualTo(1));
        Assert.That(successfulInstances[0], Is.EqualTo("instance-1"));
    }

    [Test]
    public void CommandResponseHandler_WithDuplicateSuccessResponse_AddsSuccessfulInstanceOnlyOnce()
    {
        var command = CreateUninitializedTriggerCommand();
        var response = (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new CommandResponse
        {
            Id = "cmd-id",
            Command = CommandType.TriggerAction,
            ServerInstanceId = "instance-1",
            Status = Status.Succeeded
        });

        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandResponseHandler",
            RedisChannel.Literal("command-response"), response);
        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandResponseHandler",
            RedisChannel.Literal("command-response"), response);

        var successfulInstances =
            (IList<string>)GetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames");

        Assert.That(successfulInstances, Has.Count.EqualTo(1));
    }

    [Test]
    public void CommandResponseHandler_WithDifferentCommandId_DoesNotRecordResponse()
    {
        var command = CreateUninitializedTriggerCommand();

        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandResponseHandler",
            RedisChannel.Literal("command-response"),
            (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new CommandResponse
            {
                Id = "different-id",
                Command = CommandType.TriggerAction,
                ServerInstanceId = "instance-1",
                Status = Status.Succeeded
            }));

        var successfulInstances =
            (IList<string>)GetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames");
        var failedResponses = (IList<string>)GetField(typeof(MockerCommand), command, "_failedCommandResponses");

        Assert.That(successfulInstances, Is.Empty);
        Assert.That(failedResponses, Is.Empty);
    }

    [Test]
    public void CommandResponseHandler_WithDifferentCommandType_DoesNotRecordResponse()
    {
        var command = CreateUninitializedTriggerCommand();

        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandResponseHandler",
            RedisChannel.Literal("command-response"),
            (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new CommandResponse
            {
                Id = "cmd-id",
                Command = CommandType.ChangeActionStub,
                ServerInstanceId = "instance-1",
                Status = Status.Succeeded
            }));

        var successfulInstances =
            (IList<string>)GetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames");
        var failedResponses = (IList<string>)GetField(typeof(MockerCommand), command, "_failedCommandResponses");

        Assert.That(successfulInstances, Is.Empty);
        Assert.That(failedResponses, Is.Empty);
    }

    [Test]
    public void CommandResponseHandler_WithFailedStatus_StoresFailureWithoutThrowing()
    {
        var command = CreateUninitializedTriggerCommand();

        Assert.DoesNotThrow(() =>
            InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandResponseHandler",
                RedisChannel.Literal("command-response"),
                (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new CommandResponse
                {
                    Id = "cmd-id",
                    Command = CommandType.TriggerAction,
                    ServerInstanceId = "instance-1",
                    Status = Status.Failed,
                    ExceptionMessage = "request failed"
                })));

        var failedResponses = (IList<string>)GetField(typeof(MockerCommand), command, "_failedCommandResponses");
        Assert.That(failedResponses, Has.Count.EqualTo(1));
        Assert.That(failedResponses[0], Does.Contain("request failed"));
    }

    [Test]
    public void ScanForMockerInstances_DeduplicatesInstancesAndUsesRedisSubscriber()
    {
        var command = CreateUninitializedTriggerCommand();
        var subscriberMock = new Mock<ISubscriber>();
        SetField(typeof(MockerCommand), command, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string> { "instance-1", "instance-1" });

        InvokeNonPublicMethod(typeof(MockerCommand), command, "ScanForMockerInstances");

        var instances = (IList<string>)GetField(typeof(MockerCommand), command, "_serverInstanceNames");

        Assert.That(instances, Has.Count.EqualTo(1));
        subscriberMock.Verify(subscriber => subscriber.Subscribe(
            It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()),
            Times.Once);
        subscriberMock.Verify(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
        subscriberMock.Verify(subscriber => subscriber.Unsubscribe(
            It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Test]
    public void CommandTheMockerInstances_PublishesCommandForEachServerInstance()
    {
        var command = CreateUninitializedTriggerCommand();
        var subscriberMock = new Mock<ISubscriber>();
        SetField(typeof(MockerCommand), command, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string> { "instance-1", "instance-2" });
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames", new List<string>());

        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandTheMockerInstances");

        subscriberMock.Verify(subscriber => subscriber.Subscribe(
                It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()),
            Times.Exactly(2));
        subscriberMock.Verify(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Exactly(2));
        subscriberMock.Verify(subscriber => subscriber.Unsubscribe(
                It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()),
            Times.Exactly(2));
    }

    [Test]
    public void RequestConstructors_CreateExpectedPayloads()
    {
        var command = CreateUninitializedTriggerCommand();

        var pingPayload = (string)InvokeNonPublicMethod(typeof(MockerCommand), command, "PingRequestConstructor")!;
        var commandPayload = (string)InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandRequestConstructor")!;

        var parsedPing = JsonSerializer.Deserialize<PingRequest>(pingPayload);
        var parsedCommand = JsonSerializer.Deserialize<CommandRequest>(commandPayload);

        Assert.That(parsedPing, Is.Not.Null);
        Assert.That(parsedPing!.Id, Is.EqualTo("cmd-id"));

        Assert.That(parsedCommand, Is.Not.Null);
        Assert.That(parsedCommand!.Id, Is.EqualTo("cmd-id"));
        Assert.That(parsedCommand.Command, Is.EqualTo(CommandType.TriggerAction));
    }

    [Test]
    public void CommandRequestConstructor_IncludesRelevantCommandConfiguration()
    {
        var command = CreateUninitializedTriggerCommand();
        SetField(typeof(MockerCommand), command, "CommandConfig",
            new TriggerAction { ActionName = "trigger-a", TimeoutMs = 321 });

        var commandPayload = (string)InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandRequestConstructor")!;
        var parsedCommand = JsonSerializer.Deserialize<CommandRequest>(commandPayload);

        Assert.That(parsedCommand, Is.Not.Null);
        Assert.That(parsedCommand!.TriggerAction, Is.Not.Null);
        Assert.That(parsedCommand.TriggerAction!.ActionName, Is.EqualTo("trigger-a"));
        Assert.That(parsedCommand.TriggerAction.TimeoutMs, Is.EqualTo(321));
    }

    [Test]
    public void Command_WithNoFoundServerInstances_ThrowsArgumentException()
    {
        var command = CreateUninitializedTriggerCommand();
        SetField(typeof(MockerCommand), command, "_requestRetries", 0);
        SetField(typeof(MockerCommand), command, "_redisSubscriber", new Mock<ISubscriber>().Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string>());

        var exception = Assert.Throws<TargetInvocationException>(() =>
            InvokeNonPublicMethod(typeof(MockerCommand), command, "Command"));

        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentException>());
    }

    [Test]
    public void Command_WhenNotAllResponsesSucceeded_ThrowsInvalidOperationException()
    {
        var command = CreateUninitializedTriggerCommand();
        SetField(typeof(MockerCommand), command, "_requestRetries", 0);
        SetField(typeof(MockerCommand), command, "_redisSubscriber", new Mock<ISubscriber>().Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string> { "instance-1" });
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames", new List<string>());

        var exception = Assert.Throws<TargetInvocationException>(() =>
            InvokeNonPublicMethod(typeof(MockerCommand), command, "Command"));

        Assert.That(exception!.InnerException, Is.TypeOf<MockerCommandRequestFailedException>());
    }

    [Test]
    public void Act_WithHandleDataFalse_ReturnsNullInputAndOutput()
    {
        var command = CreateUninitializedTriggerCommand();
        var subscriberMock = new Mock<ISubscriber>();
        SetField(typeof(MockerCommand), command, "_requestRetries", 0);
        SetField(typeof(MockerCommand), command, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string> { "instance-1" });
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames",
            new List<string> { "instance-1" });

        var context = CreateContext();
        InvokeNonPublicMethod(typeof(MockerCommand), command, "ExportRunningCommunicationData", context, SessionName);

        var result =
            (InternalCommunicationData<object>)InvokeNonPublicMethod(typeof(MockerCommand), command, "Act")!;

        Assert.That(result.Input, Is.Null);
        Assert.That(result.Output, Is.Null);
    }

    [Test]
    public void BaseAdditionalDataExchangeWithTheMocker_ReturnsNullTuple()
    {
        var command = CreateUninitializedTriggerCommand();

        var result =
            (ValueTuple<IEnumerable<DetailedData<object>>?, IEnumerable<DetailedData<object>>?>)
            InvokeNonPublicMethod(typeof(MockerCommand), command, "AdditionalDataExchangeWithTheMocker")!;

        Assert.That(result.Item1, Is.Null);
        Assert.That(result.Item2, Is.Null);
    }

    [Test]
    public void Dispose_WhenConnectionWasNotInitialized_DoesNotThrow()
    {
        var command = CreateUninitializedTriggerCommand();
        SetField(typeof(MockerCommand), command, "_redisConnection", null);

        Assert.DoesNotThrow(() => command.Dispose());
    }

    [Test]
    public void LogData_BaseImplementation_DoesNotThrow()
    {
        var command = CreateUninitializedTriggerCommand();
        var data = new InternalCommunicationData<object>();
        var detailedData = new DetailedData<object> { Body = "body" };

        Assert.DoesNotThrow(() =>
            InvokeNonPublicMethod(typeof(MockerCommand), command, "LogData", data, detailedData, default(InputOutputState?)));
    }

    [Test]
    public void TriggerActionMockerCommand_HandlesData_IsFalse()
    {
        var command = CreateUninitializedTriggerCommand();

        var handlesData = (bool)InvokeNonPublicMethod(typeof(TriggerActionMockerCommand), command, "get_HandlesData")!;

        Assert.That(handlesData, Is.False);
    }

    [Test]
    public void ChangeActionStubMockerCommand_MethodsReturnExpectedDefaults()
    {
        var command = CreateUninitializedChangeActionCommand();

        var handlesData = (bool)InvokeNonPublicMethod(typeof(ChangeActionStubMockerCommand), command, "get_HandlesData")!;
        var commandType =
            (CommandType)InvokeNonPublicMethod(typeof(ChangeActionStubMockerCommand), command, "get_CommandType")!;
        var inputSerialization = InvokeNonPublicMethod(typeof(ChangeActionStubMockerCommand), command,
            "GetInputCommunicationSerializationType");
        var outputSerialization = InvokeNonPublicMethod(typeof(ChangeActionStubMockerCommand), command,
            "GetOutputCommunicationSerializationType");

        Assert.That(handlesData, Is.False);
        Assert.That(commandType, Is.EqualTo(CommandType.ChangeActionStub));
        Assert.That(inputSerialization, Is.Null);
        Assert.That(outputSerialization, Is.Null);
    }

    [Test]
    public void ConsumeMockerCommand_MethodGettersReturnConfiguredValues()
    {
        var consumeCommand = CreateUninitializedConsumeCommand();
        SetField(typeof(ConsumeMockerCommand), consumeCommand, "_consumeConfig", new ConsumeConfig
        {
            InputDeserialize = new DeserializeConfig { Deserializer = SerializationType.Json },
            OutputDeserialize = new DeserializeConfig { Deserializer = SerializationType.Xml }
        });

        var handlesData = (bool)InvokeNonPublicMethod(typeof(ConsumeMockerCommand), consumeCommand, "get_HandlesData")!;
        var commandType = (CommandType)InvokeNonPublicMethod(typeof(ConsumeMockerCommand), consumeCommand, "get_CommandType")!;
        var inputSerialization = (SerializationType?)InvokeNonPublicMethod(typeof(ConsumeMockerCommand), consumeCommand,
            "GetInputCommunicationSerializationType");
        var outputSerialization = (SerializationType?)InvokeNonPublicMethod(typeof(ConsumeMockerCommand), consumeCommand,
            "GetOutputCommunicationSerializationType");

        Assert.That(handlesData, Is.True);
        Assert.That(commandType, Is.EqualTo(CommandType.Consume));
        Assert.That(inputSerialization, Is.EqualTo(SerializationType.Json));
        Assert.That(outputSerialization, Is.EqualTo(SerializationType.Xml));
    }

    [Test]
    public void ConsumeMockerCommand_AdditionalDataExchange_WithOnlyInput_ReturnsOnlyInputData()
    {
        var consumeCommand = CreateUninitializedConsumeCommand();
        typeof(MockerCommand).GetProperty("ServerInputOutputState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(consumeCommand, InputOutputState.OnlyInput);

        var inputQueue = CommunicationMethods.CreateConsumerEndpointInput("server-a");
        var dbMock = CreateRedisDbWithQueueData(new Dictionary<string, Queue<RedisValue>>
        {
            [inputQueue] = new Queue<RedisValue>(new[]
            {
                (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new DetailedData<byte[]>
                    { Body = [1, 2], Timestamp = DateTime.UtcNow }),
                RedisValue.Null
            })
        });
        SetField(typeof(MockerCommand), consumeCommand, "RedisDatabase", dbMock.Object);

        var result =
            (ValueTuple<IEnumerable<DetailedData<object>>?, IEnumerable<DetailedData<object>>?>)
            InvokeNonPublicMethod(typeof(ConsumeMockerCommand), consumeCommand,
                "AdditionalDataExchangeWithTheMocker")!;

        Assert.That(result.Item1!.ToList(), Has.Count.EqualTo(1));
        Assert.That(result.Item2, Is.Null);
    }

    [Test]
    public void ConsumeMockerCommand_AdditionalDataExchange_WithOnlyOutput_ReturnsOnlyOutputData()
    {
        var consumeCommand = CreateUninitializedConsumeCommand();
        typeof(MockerCommand).GetProperty("ServerInputOutputState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(consumeCommand, InputOutputState.OnlyOutput);

        var outputQueue = CommunicationMethods.CreateConsumerEndpointOutput("server-a");
        var dbMock = CreateRedisDbWithQueueData(new Dictionary<string, Queue<RedisValue>>
        {
            [outputQueue] = new Queue<RedisValue>(new[]
            {
                (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new DetailedData<byte[]>
                    { Body = [9], Timestamp = DateTime.UtcNow }),
                RedisValue.Null
            })
        });
        SetField(typeof(MockerCommand), consumeCommand, "RedisDatabase", dbMock.Object);

        var result =
            (ValueTuple<IEnumerable<DetailedData<object>>?, IEnumerable<DetailedData<object>>?>)
            InvokeNonPublicMethod(typeof(ConsumeMockerCommand), consumeCommand,
                "AdditionalDataExchangeWithTheMocker")!;

        Assert.That(result.Item1, Is.Null);
        Assert.That(result.Item2!.ToList(), Has.Count.EqualTo(1));
    }

    [Test]
    public void ExportRunningCommunicationData_AddsInputAndOutputChannelsToRunningSession()
    {
        var command = CreateUninitializedTriggerCommand();
        var context = CreateContext();

        InvokeNonPublicMethod(typeof(MockerCommand), command, "ExportRunningCommunicationData", context, SessionName);

        var runningSession = context.InternalRunningSessions.RunningSessionsDict[SessionName];

        Assert.That(runningSession.Inputs, Has.Count.EqualTo(1));
        Assert.That(runningSession.Outputs, Has.Count.EqualTo(1));
        Assert.That(runningSession.Inputs![0].Name, Is.EqualTo("TestMocker"));
        Assert.That(runningSession.Outputs![0].Name, Is.EqualTo("TestMocker"));
    }

    [Test]
    public void ToString_ReturnsReadableDescription()
    {
        var command = CreateUninitializedTriggerCommand();

        var text = command.ToString();

        Assert.That(text, Does.Contain("Mocker Command TestMocker"));
    }

    [Test]
    public void ConsumeMockerCommand_AdditionalDataExchange_NoInputOutput_ReturnsNullTuple()
    {
        var consumeCommand = CreateUninitializedConsumeCommand();
        typeof(MockerCommand).GetProperty("ServerInputOutputState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(consumeCommand, InputOutputState.NoInputOutput);

        var result =
            (ValueTuple<IEnumerable<DetailedData<object>>?, IEnumerable<DetailedData<object>>?>)
            InvokeNonPublicMethod(typeof(ConsumeMockerCommand), consumeCommand,
                "AdditionalDataExchangeWithTheMocker")!;

        Assert.That(result.Item1, Is.Null);
        Assert.That(result.Item2, Is.Null);
    }

    [Test]
    public void ConsumeMockerCommand_Consume_YieldsDataFromRedisUntilTimeout()
    {
        var consumeCommand = CreateUninitializedConsumeCommand();
        var dbMock = new Mock<IDatabase>();

        var detailedData = new DetailedData<byte[]> { Body = [1, 2, 3], Timestamp = DateTime.UtcNow };
        var payload = JsonSerializer.SerializeToUtf8Bytes(detailedData);
        var queue = new Queue<RedisValue>(new[] { (RedisValue)payload, RedisValue.Null });

        dbMock.Setup(database => database.ListLeftPop(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns(() => queue.Count > 0 ? queue.Dequeue() : RedisValue.Null);
        SetField(typeof(MockerCommand), consumeCommand, "RedisDatabase", dbMock.Object);

        var consumed = ((IEnumerable<DetailedData<byte[]>>)InvokeNonPublicMethod(typeof(ConsumeMockerCommand),
            consumeCommand, "Consume", "server:queue", 5)!).ToList();

        Assert.That(consumed, Has.Count.EqualTo(1));
        Assert.That(consumed[0].Body, Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void ScanForMockerInstances_WhenServersFound_BreaksRetryLoopAfterFirstAttempt()
    {
        var command = CreateUninitializedTriggerCommand();
        var subscriberMock = new Mock<ISubscriber>();
        SetField(typeof(MockerCommand), command, "_requestRetries", 3);
        SetField(typeof(MockerCommand), command, "_requestDurationMs", 0);
        SetField(typeof(MockerCommand), command, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string> { "instance-1" });

        InvokeNonPublicMethod(typeof(MockerCommand), command, "ScanForMockerInstances");

        subscriberMock.Verify(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public void ScanForMockerInstances_WhenNoServersFound_PublishesForEachRetry()
    {
        var command = CreateUninitializedTriggerCommand();
        var subscriberMock = new Mock<ISubscriber>();
        SetField(typeof(MockerCommand), command, "_requestRetries", 3);
        SetField(typeof(MockerCommand), command, "_requestDurationMs", 0);
        SetField(typeof(MockerCommand), command, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string>());

        InvokeNonPublicMethod(typeof(MockerCommand), command, "ScanForMockerInstances");

        subscriberMock.Verify(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Exactly(3));
    }

    [Test]
    public void CommandTheMockerInstances_WhenAllResponsesAreAlreadySuccessful_DoesNotRepublish()
    {
        var command = CreateUninitializedTriggerCommand();
        var subscriberMock = new Mock<ISubscriber>();
        SetField(typeof(MockerCommand), command, "_requestRetries", 3);
        SetField(typeof(MockerCommand), command, "_requestDurationMs", 0);
        SetField(typeof(MockerCommand), command, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string> { "instance-1", "instance-2" });
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames",
            new List<string> { "instance-1", "instance-2" });

        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandTheMockerInstances");

        subscriberMock.Verify(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Test]
    public void CommandTheMockerInstances_WhenResponsesAreMissing_RetriesForAllInstances()
    {
        var command = CreateUninitializedTriggerCommand();
        var subscriberMock = new Mock<ISubscriber>();
        SetField(typeof(MockerCommand), command, "_requestRetries", 2);
        SetField(typeof(MockerCommand), command, "_requestDurationMs", 0);
        SetField(typeof(MockerCommand), command, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string> { "instance-1", "instance-2" });
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames", new List<string>());

        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandTheMockerInstances");

        subscriberMock.Verify(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Exactly(4));
    }

    [Test]
    public void CommandTheMockerInstances_WhenSomeResponsesAlreadySucceeded_RetriesOnlyPendingInstances()
    {
        var command = CreateUninitializedTriggerCommand();
        var subscriberMock = new Mock<ISubscriber>();
        var publishCount = 0;
        subscriberMock.Setup(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback(() =>
            {
                publishCount++;
                if (publishCount == 2)
                {
                    SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames",
                        new List<string> { "instance-1" });
                }
            });

        SetField(typeof(MockerCommand), command, "_requestRetries", 2);
        SetField(typeof(MockerCommand), command, "_requestDurationMs", 0);
        SetField(typeof(MockerCommand), command, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string> { "instance-1", "instance-2" });
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames", new List<string>());

        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandTheMockerInstances");

        subscriberMock.Verify(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Exactly(3));
    }

    #pragma warning disable CS8602
    [Test]
    public void Act_WithHandleDataTrue_AddsReturnedDataToRunningCommunicationCollections()
    {
        var consumeCommand = CreateUninitializedConsumeCommand();
        var subscriberMock = new Mock<ISubscriber>();
        SetField(typeof(MockerCommand), consumeCommand, "_requestRetries", 0);
        SetField(typeof(MockerCommand), consumeCommand, "_requestDurationMs", 0);
        SetField(typeof(MockerCommand), consumeCommand, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), consumeCommand, "_serverInstanceNames", new List<string> { "instance-1" });
        SetField(typeof(MockerCommand), consumeCommand, "_successfulCommandResponseToServerInstanceNames",
            new List<string> { "instance-1" });
        typeof(MockerCommand).GetProperty("ServerInputOutputState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(consumeCommand, InputOutputState.BothInputOutput);

        SetField(typeof(ConsumeMockerCommand), consumeCommand, "_consumeConfig", new ConsumeConfig
        {
            TimeoutMs = 5,
            InputDeserialize = new DeserializeConfig { Deserializer = SerializationType.Json }
        });
        SetField(typeof(ConsumeMockerCommand), consumeCommand, "_inputDeserializer",
            DeserializerFactory.BuildDeserializer(SerializationType.Json));

        var inputQueue = CommunicationMethods.CreateConsumerEndpointInput("server-a");
        var outputQueue = CommunicationMethods.CreateConsumerEndpointOutput("server-a");
        var dbMock = CreateRedisDbWithQueueData(new Dictionary<string, Queue<RedisValue>>
        {
            [inputQueue] = new Queue<RedisValue>(new[]
            {
                (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new DetailedData<byte[]>
                {
                    Body = JsonSerializer.SerializeToUtf8Bytes(new { value = 1 }),
                    Timestamp = DateTime.UtcNow
                }),
                RedisValue.Null
            }),
            [outputQueue] = new Queue<RedisValue>(new[]
            {
                (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new DetailedData<byte[]>
                {
                    Body = [7, 8, 9],
                    Timestamp = DateTime.UtcNow
                }),
                RedisValue.Null
            })
        });
        SetField(typeof(MockerCommand), consumeCommand, "RedisDatabase", dbMock.Object);

        var context = CreateContext();
        InvokeNonPublicMethod(typeof(MockerCommand), consumeCommand, "ExportRunningCommunicationData", context, SessionName);
        var result =
            (InternalCommunicationData<object>)InvokeNonPublicMethod(typeof(MockerCommand), consumeCommand, "Act")!;
        var sentRunningData = (RunningCommunicationData<object>)GetField(typeof(MockerCommand), consumeCommand,
            "_sentRunningCommunicationData");
        var receivedRunningData = (RunningCommunicationData<object>)GetField(typeof(MockerCommand), consumeCommand,
            "_receivedRunningCommunicationData");

        Assert.That(result.Input, Is.Not.Null);
        Assert.That(result.Output, Is.Not.Null);
        Assert.That(sentRunningData.Data, Has.Count.EqualTo(1));
        Assert.That(receivedRunningData.Data, Has.Count.EqualTo(1));
        Assert.That(sentRunningData.Data!.Single().Body, Is.Not.TypeOf<byte[]>());
    }
    #pragma warning restore CS8602

    [Test]
    public void ConsumeMockerCommand_AdditionalDataExchange_WithOutputDeserializer_DeserializesOutputBody()
    {
        var consumeCommand = CreateUninitializedConsumeCommand();
        typeof(MockerCommand).GetProperty("ServerInputOutputState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(consumeCommand, InputOutputState.OnlyOutput);

        SetField(typeof(ConsumeMockerCommand), consumeCommand, "_consumeConfig", new ConsumeConfig
        {
            TimeoutMs = 5,
            OutputDeserialize = new DeserializeConfig { Deserializer = SerializationType.Json }
        });
        SetField(typeof(ConsumeMockerCommand), consumeCommand, "_outputDeserializer",
            DeserializerFactory.BuildDeserializer(SerializationType.Json));

        var outputQueue = CommunicationMethods.CreateConsumerEndpointOutput("server-a");
        var dbMock = CreateRedisDbWithQueueData(new Dictionary<string, Queue<RedisValue>>
        {
            [outputQueue] = new Queue<RedisValue>(new[]
            {
                (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new DetailedData<byte[]>
                {
                    Body = JsonSerializer.SerializeToUtf8Bytes(new { value = 42 }),
                    Timestamp = DateTime.UtcNow
                }),
                RedisValue.Null
            })
        });
        SetField(typeof(MockerCommand), consumeCommand, "RedisDatabase", dbMock.Object);

        var result =
            (ValueTuple<IEnumerable<DetailedData<object>>?, IEnumerable<DetailedData<object>>?>)
            InvokeNonPublicMethod(typeof(ConsumeMockerCommand), consumeCommand,
                "AdditionalDataExchangeWithTheMocker")!;

        var output = result.Item2!.ToList();
        Assert.That(result.Item1, Is.Null);
        Assert.That(output, Has.Count.EqualTo(1));
        Assert.That(output[0].Body, Is.Not.TypeOf<byte[]>());
    }

    private static Mock<IDatabase> CreateRedisDbWithQueueData(Dictionary<string, Queue<RedisValue>> queues)
    {
        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(database => database.ListLeftPop(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, CommandFlags _) =>
            {
                var name = (string)key!;
                if (!queues.TryGetValue(name, out var queue) || queue is null || queue.Count == 0)
                {
                    return RedisValue.Null;
                }

                return queue.Dequeue();
            });
        return dbMock;
    }

    private static TriggerActionMockerCommand CreateUninitializedTriggerCommand()
    {
        var command = (TriggerActionMockerCommand)RuntimeHelpers.GetUninitializedObject(
            typeof(TriggerActionMockerCommand));

        SetField(typeof(SessionAction), command, "<Name>k__BackingField", "TestMocker");
        SetField(typeof(SessionAction), command, "Logger", Globals.Logger);
        SetField(typeof(StagedAction), command, "<Stage>k__BackingField", 0);
        SetField(typeof(MockerCommand), command, "_commandId", "cmd-id");
        SetField(typeof(MockerCommand), command, "ServerName", "server-a");
        SetField(typeof(MockerCommand), command, "CommandConfig", new TriggerAction());
        SetField(typeof(MockerCommand), command, "_requestDurationMs", 0);
        SetField(typeof(MockerCommand), command, "_requestRetries", 1);
        SetField(typeof(MockerCommand), command, "_redisHost", "localhost");
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string>());
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames", new List<string>());
        SetField(typeof(MockerCommand), command, "_failedCommandResponses", new List<string>());
        SetField(typeof(MockerCommand), command, "_receivedRunningCommunicationData", new RunningCommunicationData<object>());
        SetField(typeof(MockerCommand), command, "_sentRunningCommunicationData", new RunningCommunicationData<object>());
        SetField(typeof(MockerCommand), command, "RedisDatabase", new Mock<IDatabase>().Object);

        return command;
    }

    private static ChangeActionStubMockerCommand CreateUninitializedChangeActionCommand()
    {
        var command = (ChangeActionStubMockerCommand)RuntimeHelpers.GetUninitializedObject(
            typeof(ChangeActionStubMockerCommand));

        SetField(typeof(SessionAction), command, "<Name>k__BackingField", "ChangeAction");
        SetField(typeof(SessionAction), command, "Logger", Globals.Logger);
        SetField(typeof(StagedAction), command, "<Stage>k__BackingField", 0);
        SetField(typeof(MockerCommand), command, "_commandId", "cmd-id");
        SetField(typeof(MockerCommand), command, "ServerName", "server-a");
        SetField(typeof(MockerCommand), command, "CommandConfig", new ChangeActionStub());
        SetField(typeof(MockerCommand), command, "_requestDurationMs", 0);
        SetField(typeof(MockerCommand), command, "_requestRetries", 1);
        SetField(typeof(MockerCommand), command, "_redisHost", "localhost");
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string>());
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames", new List<string>());
        SetField(typeof(MockerCommand), command, "_failedCommandResponses", new List<string>());
        SetField(typeof(MockerCommand), command, "_receivedRunningCommunicationData", new RunningCommunicationData<object>());
        SetField(typeof(MockerCommand), command, "_sentRunningCommunicationData", new RunningCommunicationData<object>());
        SetField(typeof(MockerCommand), command, "RedisDatabase", new Mock<IDatabase>().Object);

        return command;
    }

    private static ConsumeMockerCommand CreateUninitializedConsumeCommand()
    {
        var command = (ConsumeMockerCommand)RuntimeHelpers.GetUninitializedObject(typeof(ConsumeMockerCommand));

        SetField(typeof(SessionAction), command, "<Name>k__BackingField", "ConsumeMocker");
        SetField(typeof(SessionAction), command, "Logger", Globals.Logger);
        SetField(typeof(StagedAction), command, "<Stage>k__BackingField", 0);
        SetField(typeof(MockerCommand), command, "_commandId", "cmd-id");
        SetField(typeof(MockerCommand), command, "ServerName", "server-a");
        SetField(typeof(MockerCommand), command, "CommandConfig", new ConsumeConfig());
        SetField(typeof(MockerCommand), command, "_requestDurationMs", 0);
        SetField(typeof(MockerCommand), command, "_requestRetries", 1);
        SetField(typeof(MockerCommand), command, "_redisHost", "localhost");
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string>());
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames", new List<string>());
        SetField(typeof(MockerCommand), command, "_failedCommandResponses", new List<string>());
        SetField(typeof(MockerCommand), command, "_receivedRunningCommunicationData", new RunningCommunicationData<object>());
        SetField(typeof(MockerCommand), command, "_sentRunningCommunicationData", new RunningCommunicationData<object>());
        SetField(typeof(MockerCommand), command, "RedisDatabase", new Mock<IDatabase>().Object);

        SetField(typeof(ConsumeMockerCommand), command, "_consumeConfig", new ConsumeConfig { TimeoutMs = 5 });
        SetField(typeof(ConsumeMockerCommand), command, "_inputDataFilter", new DataFilter());
        SetField(typeof(ConsumeMockerCommand), command, "_outputDataFilter", new DataFilter());

        return command;
    }

    private static InternalContext CreateContext()
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>
            {
                {
                    SessionName, new RunningSessionData<object, object>
                    {
                        Inputs = [],
                        Outputs = []
                    }
                }
            })
        };
        return context;
    }

    private static object? InvokeNonPublicMethod(Type declaringType, object target, string methodName,
        params object?[]? parameters)
    {
        var method = declaringType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return method.Invoke(target, parameters);
    }

    private static void SetField(Type declaringType, object target, string fieldName, object? value)
    {
        var field = declaringType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }

    private static object GetField(Type declaringType, object target, string fieldName)
    {
        var field = declaringType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return field.GetValue(target)!;
    }
}

