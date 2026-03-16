using Microsoft.Extensions.Logging;
using MoreLinq.Extensions;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;
using Qaas.Mocker.CommunicationObjects;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Ping;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using StackExchange.Redis;
using System.Diagnostics;
using System.Text.Json;
using CommunicationInputOutputState = QaaS.Framework.SDK.ConfigurationObjects.InputOutputState;

namespace QaaS.Runner.Sessions.Actions.MockerCommands;

/// <summary>
/// Base runtime for commands sent to mocker instances through redis ping/request-response channels.
/// </summary>
public abstract class MockerCommand : StagedAction
{
    private const string PingContentType = "ping";
    private const string CommandContentType = "command";
    private const int ResponsePollingIntervalMs = 10;
    private const int ResponseQuietPeriodMs = 50;

    private readonly string _commandId;

    private readonly ConnectionMultiplexer _redisConnection;
    private readonly string _redisHost;
    private readonly ISubscriber _redisSubscriber;
    private readonly int _requestDurationMs;
    private readonly int _requestRetries;
    private readonly object _responseStateLock = new();
    private readonly IList<string> _failedCommandResponses;
    private readonly IList<string> _successfulCommandResponseToServerInstanceNames;
    protected readonly object SupportedCommandConfiguration;
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

        SupportedCommandConfiguration = commandConfig;

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
    protected InputOutputState? ServerInputOutputState { get; set; }

    /// <summary>
    ///     The type of the mocker action.
    /// </summary>
    protected abstract CommandType CommandType { get; }

    /// <summary>
    /// Executes discovery, command dispatch and optional data exchange with the mocker instances.
    /// </summary>
    /// <returns>Input and output data returned by the command, when applicable.</returns>
    private (IEnumerable<DetailedData<object>>?, IEnumerable<DetailedData<object>>?) Command()
    {
        ScanForMockerInstances();
        var serverInstanceNames = GetServerInstanceNamesSnapshot();

        if (serverInstanceNames.Count == 0)
        {
            Logger.LogDebug(
                "No mocker instances of {ServerName} found in redis server {RedisServer} and database {RedisDataBase}",
                ServerName, _redisHost, RedisDatabase.Database);
            throw new ArgumentException($"No mocker server instances found for '{ServerName}'");
        }

        Logger.LogInformation("Found {ServerInstances} instances for server {ServerName}", serverInstanceNames.Count,
            ServerName);
        Logger.LogDebug("Discovered mocker instances for server {ServerName}: {ServerInstances}",
            ServerName, string.Join(", ", serverInstanceNames));

        CommandTheMockerInstances();

        var allCommandRequestsAreSuccessful = AllCommandsRequestsAreSuccessful();

        if (!allCommandRequestsAreSuccessful)
        {
            var successfulInstances = GetSuccessfulResponseNamesSnapshot();
            var failedResponses = GetFailedResponsesSnapshot();
            Logger.LogDebug(
                "Received {SuccessfulCommandResponses} successful commands responses from {ServerInstancesCount} server instances",
                successfulInstances.Count, serverInstanceNames.Count);
            throw new MockerCommandRequestFailedException(
                $"Not all command requests were successful for server '{ServerName}'. " +
                $"Expected instances: {string.Join(", ", serverInstanceNames)}. " +
                $"Succeeded: {string.Join(", ", successfulInstances.Distinct(StringComparer.Ordinal))}. " +
                $"Failures: {string.Join(" | ", failedResponses.Distinct(StringComparer.Ordinal))}");
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

    /// <summary>
    /// Discovers mocker instances by publishing ping requests and collecting responses with retries.
    /// </summary>
    private void ScanForMockerInstances()
    {
        var pingRequestChannel = CommunicationMethods.CreateChannelRunnerToMocker(PingContentType, ServerName);
        var pingResponseChannel = CommunicationMethods.CreateChannelMockerToRunner(PingContentType, ServerName);
        var responseChannel = RedisChannel.Literal(pingResponseChannel);

        _redisSubscriber.Subscribe(responseChannel, PingResponseHandler);
        Logger.LogInformation("Subscribed to ping response channel '{PingResponseChannel}'", pingResponseChannel);

        try
        {
            for (var retryIndex = 1; retryIndex <= _requestRetries; retryIndex++)
            {
                Logger.LogDebug("Publishing ping request attempt {Attempt}/{MaxAttempts} for server {ServerName}",
                    retryIndex, _requestRetries, ServerName);
                _redisSubscriber.Publish(RedisChannel.Literal(pingRequestChannel), PingRequestConstructor());
                WaitForDiscoveryResponses();
                if (GetServerInstanceNamesSnapshot().Count != 0)
                    break;
            }
        }
        finally
        {
            _redisSubscriber.Unsubscribe(responseChannel, PingResponseHandler);
            Logger.LogDebug("Unsubscribed from ping response channels for server {ServerName}", ServerName);

            lock (_responseStateLock)
            {
                _serverInstanceNames = _serverInstanceNames
                    .Where(serverInstance => !string.IsNullOrWhiteSpace(serverInstance))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Creates a serialized ping request for mocker instance discovery.
    /// </summary>
    private string PingRequestConstructor()
    {
        return JsonSerializer.Serialize(new PingRequest
        {
            Id = _commandId
        });
    }

    /// <summary>
    /// Processes ping responses and records matching server instances for this command id/server pair.
    /// </summary>
    private void PingResponseHandler(RedisChannel channel, RedisValue serializedMessage)
    {
        Logger.LogDebug("Received ping response on channel {Channel}", channel);
        var pingResponse = JsonSerializer.Deserialize<PingResponse>((byte[])serializedMessage!)!;
        if (pingResponse.Id != _commandId) return;
        if (pingResponse.ServerName != ServerName) return;

        if (string.IsNullOrWhiteSpace(pingResponse.ServerInstanceId))
            return;

        lock (_responseStateLock)
        {
            if (!_serverInstanceNames.Contains(pingResponse.ServerInstanceId, StringComparer.Ordinal))
            {
                _serverInstanceNames.Add(pingResponse.ServerInstanceId);
            }

            if (ServerInputOutputState == null)
            {
                ServerInputOutputState = pingResponse.ServerInputOutputState;
            }
            else if (ServerInputOutputState != pingResponse.ServerInputOutputState)
            {
                throw new InvalidOperationException(
                    $"Mocker Server instances does not have matching {nameof(InputOutputState)} across all ping responses");
            }
        }

        Logger.LogInformation("Discovered mocker server instance '{ServerInstanceId}' for server '{ServerName}'",
            pingResponse.ServerInstanceId, ServerName);
    }

    /// <summary>
    /// Sends the configured command to each discovered instance and waits for response acknowledgements.
    /// </summary>
    private void CommandTheMockerInstances()
    {
        var serverInstances = GetServerInstanceNamesSnapshot();
        var responseChannels = serverInstances.ToDictionary(
            serverInstance => serverInstance,
            serverInstance => RedisChannel.Literal(CommunicationMethods.CreateChannelMockerToRunner(CommandContentType,
                ServerName, serverInstance)),
            StringComparer.Ordinal);

        foreach (var (serverInstance, responseChannel) in responseChannels)
        {
            _redisSubscriber.Subscribe(responseChannel, CommandResponseHandler);
            Logger.LogInformation(
                "Subscribed to command response channel '{ResponseChannel}' for server instance '{ServerInstance}'",
                responseChannel, serverInstance);
        }

        try
        {
            for (var retryIndex = 1; retryIndex <= _requestRetries; retryIndex++)
            {
                Logger.LogDebug("Publishing command request attempt {Attempt}/{MaxAttempts} for server {ServerName}",
                    retryIndex, _requestRetries, ServerName);
                var pendingInstances = GetPendingCommandResponseNamesSnapshot(serverInstances);
                foreach (var serverInstance in pendingInstances)
                {
                    var requestChannel = CommunicationMethods.CreateChannelRunnerToMocker(CommandContentType,
                        ServerName, serverInstance);
                    Logger.LogDebug("Publishing command request to channel '{RequestChannel}'", requestChannel);
                    _redisSubscriber.Publish(RedisChannel.Literal(requestChannel), CommandRequestConstructor());
                }

                if (pendingInstances.Count == 0)
                    break;

                WaitForResponsesUntilTimeout(AllCommandsRequestsAreSuccessful);
                if (AllCommandsRequestsAreSuccessful())
                    break;
            }
        }
        finally
        {
            foreach (var responseChannel in responseChannels.Values)
                _redisSubscriber.Unsubscribe(responseChannel, CommandResponseHandler);

            Logger.LogDebug("Unsubscribed from command response channels for server {ServerName}", ServerName);
        }
    }

    private bool AllCommandsRequestsAreSuccessful()
    {
        lock (_responseStateLock)
        {
            return _serverInstanceNames
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(_successfulCommandResponseToServerInstanceNames
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Creates a serialized command request populated with command-specific configuration.
    /// </summary>
    private string CommandRequestConstructor()
    {
        var commandRequest = new CommandRequest
        {
            Id = _commandId, Command = CommandType
        };
        commandRequest.AppendObjectToRelevantCommandConfig(SupportedCommandConfiguration);
        return JsonSerializer.Serialize(commandRequest);
    }

    /// <summary>
    /// Processes command responses and tracks success/failure by server instance.
    /// </summary>
    private void CommandResponseHandler(RedisChannel channel, RedisValue serializedMessage)
    {
        Logger.LogDebug("Received command response on channel {Channel}", channel);
        var commandResponse = JsonSerializer.Deserialize<CommandResponse>((byte[])serializedMessage!)!;
        if (commandResponse.Id != _commandId) return;
        if (commandResponse.Command != CommandType) return;
        if (commandResponse.Status == Status.Succeeded)
        {
            if (string.IsNullOrWhiteSpace(commandResponse.ServerInstanceId))
                return;

            lock (_responseStateLock)
            {
                if (!_successfulCommandResponseToServerInstanceNames.Contains(commandResponse.ServerInstanceId,
                        StringComparer.Ordinal))
                {
                    _successfulCommandResponseToServerInstanceNames.Add(commandResponse.ServerInstanceId);
                }
            }

            Logger.LogInformation(
                "Command '{CommandType}' succeeded on server instance '{ServerInstanceId}'",
                commandResponse.Command, commandResponse.ServerInstanceId);
        }
        else
        {
            lock (_responseStateLock)
            {
                _failedCommandResponses.Add(
                    $"{commandResponse.ServerInstanceId}: {commandResponse.ExceptionMessage ?? "Unknown command error"}");
            }

            Logger.LogWarning(
                "Command '{CommandType}' failed on server instance '{ServerInstanceId}' with error '{Error}'",
                commandResponse.Command, commandResponse.ServerInstanceId,
                commandResponse.ExceptionMessage ?? "Unknown command error");
        }
    }

    /// <summary>
    /// Returns the expected serialization type for command input data saved by the runner.
    /// </summary>
    protected abstract SerializationType? GetInputCommunicationSerializationType();

    /// <summary>
    /// Returns the expected serialization type for command output data saved by the runner.
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

    public override void Dispose()
    {
        _redisConnection?.Dispose();
    }

    protected internal override void LogData(InternalCommunicationData<object> actData,
        DetailedData<object> itemBeforeSerialization, InputOutputState? saveData = null) { }

    internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
    {
        _sentRunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name, SerializationType = GetInputCommunicationSerializationType()
        };
        _receivedRunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name, SerializationType = GetOutputCommunicationSerializationType()
        };

        context.AddRunningInputData(sessionName, _sentRunningCommunicationData);
        context.AddRunningOutputData(sessionName, _receivedRunningCommunicationData);
    }

    public sealed override string ToString()
    {
        return $"Mocker Command {Name} of type {GetType}";
    }

    private List<string> GetServerInstanceNamesSnapshot()
    {
        lock (_responseStateLock)
        {
            return _serverInstanceNames.ToList();
        }
    }

    private List<string> GetSuccessfulResponseNamesSnapshot()
    {
        lock (_responseStateLock)
        {
            return _successfulCommandResponseToServerInstanceNames.ToList();
        }
    }

    private List<string> GetFailedResponsesSnapshot()
    {
        lock (_responseStateLock)
        {
            return _failedCommandResponses.ToList();
        }
    }

    private List<string> GetPendingCommandResponseNamesSnapshot(IEnumerable<string> expectedServerInstances)
    {
        lock (_responseStateLock)
        {
            var successfulInstances = new HashSet<string>(_successfulCommandResponseToServerInstanceNames
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal), StringComparer.Ordinal);
            return expectedServerInstances
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Where(id => !successfulInstances.Contains(id))
                .ToList();
        }
    }

    private void WaitForResponsesUntilTimeout(Func<bool> completedPredicate)
    {
        if (_requestDurationMs <= 0)
            return;

        var timeoutStopwatch = Stopwatch.StartNew();
        while (!completedPredicate() && timeoutStopwatch.ElapsedMilliseconds < _requestDurationMs)
            Thread.Sleep(ResponsePollingIntervalMs);
    }

    /// <summary>
    /// Waits for the discovery response window to complete, or until the discovered instance count
    /// remains stable for a short quiet period after at least one response arrives.
    /// </summary>
    private void WaitForDiscoveryResponses()
    {
        if (_requestDurationMs <= 0)
            return;

        var timeoutStopwatch = Stopwatch.StartNew();
        var quietPeriodMs = Math.Min(ResponseQuietPeriodMs, _requestDurationMs);
        var lastKnownCount = GetServerInstanceNamesSnapshot().Count;
        var unchangedSinceStopwatch = lastKnownCount > 0 ? Stopwatch.StartNew() : null;

        while (timeoutStopwatch.ElapsedMilliseconds < _requestDurationMs)
        {
            Thread.Sleep(ResponsePollingIntervalMs);

            var currentCount = GetServerInstanceNamesSnapshot().Count;
            if (currentCount != lastKnownCount)
            {
                lastKnownCount = currentCount;
                unchangedSinceStopwatch = currentCount > 0 ? Stopwatch.StartNew() : null;
                continue;
            }

            if (currentCount > 0 &&
                unchangedSinceStopwatch?.ElapsedMilliseconds >= quietPeriodMs)
            {
                break;
            }
        }
    }
}
