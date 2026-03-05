using System.Text.Json;
using Microsoft.Extensions.Logging;
using MoreLinq.Extensions;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using Qaas.Mocker.CommunicationObjects;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Ping;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.ConfigurationObjects;
using StackExchange.Redis;
using CommunicationInputOutputState = Qaas.Mocker.CommunicationObjects.ConfigurationObjects.InputOutputState;

namespace QaaS.Runner.Sessions.Actions.MockerCommands;

public abstract class MockerCommand : StagedAction
{
    private const string PingContentType = "ping";
    private const string CommandContentType = "command";

    private readonly string _commandId;

    private readonly ConnectionMultiplexer _redisConnection;
    private readonly string _redisHost;
    private readonly ISubscriber _redisSubscriber;
    private readonly int _requestDurationMs;
    private readonly int _requestRetries;
    private readonly IList<string> _failedCommandResponses;
    private readonly IList<string> _successfulCommandResponseToServerInstanceNames;
    protected readonly object CommandConfig;
    protected readonly IDatabase RedisDatabase;

    protected readonly string ServerName;
    private RunningCommunicationData<object> _receivedRunningCommunicationData;
    private RunningCommunicationData<object> _sentRunningCommunicationData = default!;
    private IList<string> _serverInstanceNames;

    protected MockerCommand(string name, int stage, object commandConfig, RedisConfig redisConfig, string serverName,
        int requestDurationMs, int requestRetries, ILogger logger) : base(name, stage, null, logger)
    {
        _redisConnection = ConnectionMultiplexer.Connect(redisConfig.CreateRedisConfigurationOptions());
        _redisHost = redisConfig.Host!;
        _redisSubscriber = _redisConnection.GetSubscriber();
        RedisDatabase = _redisConnection.GetDatabase(redisConfig.RedisDataBase);

        ServerName = serverName;
        _requestDurationMs = requestDurationMs;
        _requestRetries = requestRetries;

        _commandId = Guid.NewGuid().ToString();
        _serverInstanceNames = new List<string>();
        _successfulCommandResponseToServerInstanceNames = new List<string>();
        _failedCommandResponses = new List<string>();

        CommandConfig = commandConfig;

        _receivedRunningCommunicationData = new RunningCommunicationData<object>();

        Logger.LogDebug("Initializing Mocker Command {Name} of type {MockerCommandType}", Name, GetType());
    }

    /// <summary>
    ///     Whether the command handles data
    /// </summary>
    protected abstract bool HandlesData { get; }

    /// <summary>
    ///     Whether the mocker's response containing input/outputs
    /// </summary>
    protected CommunicationInputOutputState? ServerInputOutputState { get; set; }

    /// <summary>
    ///     The type of the mocker action.
    /// </summary>
    protected abstract CommandType CommandType { get; }

    /// <summary>
    ///     Executes the mocker command
    /// </summary>
    /// <returns> Act results </returns>
    private (IEnumerable<DetailedData<object>>?, IEnumerable<DetailedData<object>>?) Command()
    {
        ScanForMockerInstances();

        if (_serverInstanceNames.Count == 0)
        {
            Logger.LogDebug(
                "No mocker instances of {ServerName} found in redis server {RedisServer} and database {RedisDataBase}",
                ServerName, _redisHost, RedisDatabase.Database);
            throw new ArgumentException($"No mocker server instances found for '{ServerName}'");
        }

        Logger.LogInformation("Found {ServerInstances} instances for server {ServerName}", _serverInstanceNames.Count,
            ServerName);
        Logger.LogDebug("All servers instances found for server {ServerName}: {ServerInstances}",
            ServerName, string.Join(", ", _serverInstanceNames));

        CommandTheMockerInstances();

        var allCommandRequestsAreSuccessful = AllCommandsRequestsAreSuccessful();

        if (!allCommandRequestsAreSuccessful)
        {
            Logger.LogDebug(
                "Received {SuccessfulCommandResponses} successful commands responses from {ServerInstancesCount} server instances",
                _successfulCommandResponseToServerInstanceNames.Count, _serverInstanceNames.Count);
            throw new MockerCommandRequestFailedException(
                $"Not all command requests were successful for server '{ServerName}'. " +
                $"Expected instances: {string.Join(", ", _serverInstanceNames)}. " +
                $"Succeeded: {string.Join(", ", _successfulCommandResponseToServerInstanceNames.Distinct())}. " +
                $"Failures: {string.Join(" | ", _failedCommandResponses.Distinct())}");
        }

        if (!HandlesData)
            return (null, null);

        var commandResult = AdditionalDataExchangeWithTheMocker();
        commandResult.Item1?.ForEach(inputData =>
        {
            _sentRunningCommunicationData.Data.Add(inputData);
            _sentRunningCommunicationData.Queue.Enqueue(inputData);
        });
        commandResult.Item2?.ForEach(outputData =>
        {
            _receivedRunningCommunicationData.Data.Add(outputData);
            _receivedRunningCommunicationData.Queue.Enqueue(outputData);
        });
        return commandResult;
    }

    private void ScanForMockerInstances()
    {
        var pingRequestChannel = CommunicationMethods.CreateChannelRunnerToMocker(PingContentType, ServerName);
        var pingResponseChannel = CommunicationMethods.CreateChannelMockerToRunner(PingContentType, ServerName);

        _redisSubscriber.SubscribeAsync(RedisChannel.Literal(pingResponseChannel), PingResponseHandler);
        Logger.LogInformation("Subscribed to ping response channel '{PingResponseChannel}'", pingResponseChannel);

        for (var retryIndex = 1; retryIndex <= _requestRetries; retryIndex++)
        {
            Logger.LogDebug("Ping Request {RetryIndex}", retryIndex);
            _redisSubscriber.Publish(RedisChannel.Literal(pingRequestChannel), PingRequestConstructor());
            if (_serverInstanceNames.Count != 0)
                break;
            if (retryIndex < _requestRetries)
                Thread.Sleep(_requestDurationMs);
        }

        _redisSubscriber.UnsubscribeAllAsync();
        Logger.LogDebug("Unsubscribed to all channels");

        _serverInstanceNames = _serverInstanceNames.Distinct().ToList();
    }

    private string PingRequestConstructor()
    {
        return JsonSerializer.Serialize(new PingRequest { Id = _commandId });
    }

    private void PingResponseHandler(RedisChannel channel, RedisValue serializedMessage)
    {
        Logger.LogDebug("Ping channel '{Channel}' response: '{SerializedMessage}'", channel, serializedMessage);
        var pingResponse = JsonSerializer.Deserialize<PingResponse>((byte[])serializedMessage!)!;
        if (pingResponse.Id != _commandId) return;
        if (pingResponse.ServerName != ServerName) return;
        _serverInstanceNames.Add(pingResponse.ServerInstanceId);
        Logger.LogInformation("Discovered mocker server instance '{ServerInstanceId}' for server '{ServerName}'",
            pingResponse.ServerInstanceId, ServerName);
        if (ServerInputOutputState == null) ServerInputOutputState = pingResponse.ServerInputOutputState;
        else if (ServerInputOutputState != pingResponse.ServerInputOutputState)
            throw new InvalidOperationException(
                $"Mocker Server instances does not have matching {nameof(CommunicationInputOutputState)} across all ping responses");
    }

    private void CommandTheMockerInstances()
    {
        foreach (var serverInstance in _serverInstanceNames)
        {
            var responseChannel = CommunicationMethods.CreateChannelMockerToRunner(CommandContentType,
                ServerName, serverInstance);
            _redisSubscriber.SubscribeAsync(RedisChannel.Literal(responseChannel), CommandResponseHandler);
            Logger.LogInformation("Subscribed to command response channel '{ResponseChannel}' for server instance '{ServerInstance}'",
                responseChannel, serverInstance);
        }

        for (var retryIndex = 1; retryIndex <= _requestRetries; retryIndex++)
        {
            Logger.LogDebug("Command Request {RetryIndex}", retryIndex);
            foreach (var serverInstance in _serverInstanceNames)
            {
                var requestChannel = CommunicationMethods.CreateChannelRunnerToMocker(CommandContentType,
                    ServerName, serverInstance);
                Logger.LogDebug("Publishing command request to channel '{RequestChannel}'", requestChannel);
                _redisSubscriber.Publish(RedisChannel.Literal(requestChannel), CommandRequestConstructor());
            }

            if (AllCommandsRequestsAreSuccessful())
                break;
            if (retryIndex < _requestRetries)
                Thread.Sleep(_requestDurationMs);
        }

        _redisSubscriber.UnsubscribeAllAsync();
        Logger.LogDebug("Unsubscribed to all channels");
    }

    private bool AllCommandsRequestsAreSuccessful()
    {
        return _serverInstanceNames.OrderBy(id => id)
            .SequenceEqual(_successfulCommandResponseToServerInstanceNames.OrderBy(id => id));
    }

    private string CommandRequestConstructor()
    {
        var commandRequest = new CommandRequest
        {
            Id = _commandId,
            Command = CommandType
        };
        commandRequest.AppendObjectToRelevantCommandConfig(CommandConfig);
        return JsonSerializer.Serialize(commandRequest);
    }

    private void CommandResponseHandler(RedisChannel channel, RedisValue serializedMessage)
    {
        Logger.LogDebug("Command channel '{Channel}' response: '{SerializedMessage}'", channel, serializedMessage);
        var commandResponse = JsonSerializer.Deserialize<CommandResponse>((byte[])serializedMessage!)!;
        if (commandResponse.Id != _commandId) return;
        if (commandResponse.Command != CommandType) return;
        if (commandResponse.Status == Status.Succeeded)
        {
            _successfulCommandResponseToServerInstanceNames.Add(commandResponse.ServerInstanceId);
            Logger.LogInformation(
                "Command '{CommandType}' succeeded on server instance '{ServerInstanceId}'",
                commandResponse.Command, commandResponse.ServerInstanceId);
        }
        else
        {
            _failedCommandResponses.Add(
                $"{commandResponse.ServerInstanceId}: {commandResponse.ExceptionMessage ?? "Unknown command error"}");
            Logger.LogWarning(
                "Command '{CommandType}' failed on server instance '{ServerInstanceId}' with error '{Error}'",
                commandResponse.Command, commandResponse.ServerInstanceId,
                commandResponse.ExceptionMessage ?? "Unknown command error");
        }
    }

    /// <summary>
    ///     Returns the serialization type the output data used in this transaction should be serializable to
    /// </summary>
    protected abstract SerializationType? GetInputCommunicationSerializationType();

    /// <summary>
    ///     Returns the serialization type the input data used in this transaction should be serializable to
    /// </summary>
    protected abstract SerializationType? GetOutputCommunicationSerializationType();

    protected virtual (IEnumerable<DetailedData<object>>?, IEnumerable<DetailedData<object>>?)
        AdditionalDataExchangeWithTheMocker()
    {
        return (null, null);
    }

    internal override InternalCommunicationData<object> Act()
    {
        var commandResults = Command();
        _receivedRunningCommunicationData.Data.CompleteAdding();
        _sentRunningCommunicationData.Data.CompleteAdding();

        // mocker command initializes both input and output
        return new InternalCommunicationData<object>
        {
            Input = commandResults.Item1?.ToList(),
            Output = commandResults.Item2?.ToList()!,
            InputSerializationType = GetInputCommunicationSerializationType(),
            OutputSerializationType = GetOutputCommunicationSerializationType()
        };
    }

    public void Dispose()
    {
        _redisConnection?.Dispose();
    }

    protected internal override void LogData(InternalCommunicationData<object> actData,
        DetailedData<object> itemBeforeSerialization, InputOutputState? saveData = null)
    {
    }

    internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
    {
        _sentRunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetInputCommunicationSerializationType()
        };
        _receivedRunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetOutputCommunicationSerializationType()
        };

        context.InternalRunningSessions.RunningSessionsDict[sessionName].Inputs!.Add(_sentRunningCommunicationData);
        context.InternalRunningSessions.RunningSessionsDict[sessionName].Outputs!
            .Add(_receivedRunningCommunicationData);
    }

    public sealed override string ToString()
    {
        return $"Mocker Command {Name} of type {GetType}";
    }
}

