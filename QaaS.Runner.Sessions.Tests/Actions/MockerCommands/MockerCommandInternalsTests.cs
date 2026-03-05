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
using QaaS.Framework.SDK.MockerObjects;
using QaaS.Framework.SDK.MockerObjects.ConfigurationObjects.Command;
using QaaS.Framework.SDK.MockerObjects.ConfigurationObjects.Ping;
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
    public void CommandResponseHandler_WithFailedStatus_ThrowsException()
    {
        var command = CreateUninitializedTriggerCommand();

        var exception = Assert.Throws<TargetInvocationException>(() =>
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

        Assert.That(exception!.InnerException, Is.TypeOf<Exception>());
        Assert.That(exception.InnerException!.Message, Is.EqualTo("request failed"));
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
        subscriberMock.Verify(subscriber => subscriber.SubscribeAsync(
            It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()),
            Times.Once);
        subscriberMock.Verify(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
        subscriberMock.Verify(subscriber => subscriber.UnsubscribeAllAsync(It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public void CommandTheMockerInstances_PublishesCommandForEachServerInstance()
    {
        var command = CreateUninitializedTriggerCommand();
        var subscriberMock = new Mock<ISubscriber>();
        SetField(typeof(MockerCommand), command, "_redisSubscriber", subscriberMock.Object);
        SetField(typeof(MockerCommand), command, "_serverInstanceNames", new List<string> { "instance-1", "instance-2" });
        SetField(typeof(MockerCommand), command, "_successfulCommandResponseToServerInstanceNames",
            new List<string> { "instance-1", "instance-2" });

        InvokeNonPublicMethod(typeof(MockerCommand), command, "CommandTheMockerInstances");

        subscriberMock.Verify(subscriber => subscriber.SubscribeAsync(
                It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()),
            Times.Exactly(2));
        subscriberMock.Verify(subscriber => subscriber.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Exactly(2));
        subscriberMock.Verify(subscriber => subscriber.UnsubscribeAllAsync(It.IsAny<CommandFlags>()), Times.Once);
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
        SetField(typeof(MockerCommand), command, "_receivedRunningCommunicationData", new RunningCommunicationData<object>());
        SetField(typeof(MockerCommand), command, "_sentRunningCommunicationData", new RunningCommunicationData<object>());

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
        SetField(typeof(MockerCommand), command, "_receivedRunningCommunicationData", new RunningCommunicationData<object>());
        SetField(typeof(MockerCommand), command, "_sentRunningCommunicationData", new RunningCommunicationData<object>());

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
        params object[]? parameters)
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
