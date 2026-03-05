using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.Extensions;
using Qaas.Mocker.CommunicationObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;
using QaaS.Framework.Serialization.Deserializers;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using QaaS.Runner.Sessions.ConfigurationObjects;
using CommunicationInputOutputState = Qaas.Mocker.CommunicationObjects.ConfigurationObjects.InputOutputState;

namespace QaaS.Runner.Sessions.Actions.MockerCommands;

/// <summary>
/// Mocker command that pulls captured input/output payloads from mocker redis queues.
/// </summary>
public class ConsumeMockerCommand : MockerCommand
{
    private readonly ConsumeConfig _consumeConfig;

    private readonly DataFilter _inputDataFilter;

    private readonly IDeserializer? _inputDeserializer;

    private readonly DataFilter _outputDataFilter;

    private readonly IDeserializer? _outputDeserializer;

    public ConsumeMockerCommand(string name, int stage, ConsumeConfig commandConfig, RedisConfig redisConfig,
        string serverName,
        int requestDurationMs, int requestRetries, ILogger logger) : base(
        name, stage, commandConfig, redisConfig, serverName,
        requestDurationMs, requestRetries, logger)
    {
        _inputDeserializer = DeserializerFactory.BuildDeserializer(commandConfig.InputDeserialize?.Deserializer);
        _outputDeserializer = DeserializerFactory.BuildDeserializer(commandConfig.OutputDeserialize?.Deserializer);

        _inputDataFilter = commandConfig.InputDataFilter;
        _outputDataFilter = commandConfig.OutputDataFilter;
        _consumeConfig = (ConsumeConfig)CommandConfig;
    }

    /// <inheritdoc />
    protected override bool HandlesData => true;

    /// <inheritdoc />
    protected override CommandType CommandType => CommandType.Consume;

    /// <summary>
    /// Consumes mocker-captured inputs/outputs, filters them and applies optional deserialization.
    /// </summary>
    protected override (IEnumerable<DetailedData<object>>?, IEnumerable<DetailedData<object>>?)
        AdditionalDataExchangeWithTheMocker()
    {
        if (ServerInputOutputState == CommunicationInputOutputState.NoInputOutput)
        {
            Logger.LogWarning("Server '{ServerName}' has not Inputs or outputs, nothing to consume from", ServerName);
            return (null, null);
        }

        var inputTypeToDeserializeTo = _consumeConfig.InputDeserialize?.SpecificType?.GetConfiguredType();
        var outputTypeToDeserializeTo = _consumeConfig.OutputDeserialize?.SpecificType?.GetConfiguredType();

        var consumedInputData = ServerInputOutputState is CommunicationInputOutputState.OnlyInput or CommunicationInputOutputState.BothInputOutput
            ? Consume(CommunicationMethods.CreateConsumerEndpointInput(ServerName),
                _consumeConfig.TimeoutMs).Select(d => d.FilterData(_inputDataFilter))
            : null;

        var consumedOutputData =
            ServerInputOutputState is CommunicationInputOutputState.OnlyOutput or CommunicationInputOutputState.BothInputOutput
                ? Consume(CommunicationMethods.CreateConsumerEndpointOutput(ServerName),
                    _consumeConfig.TimeoutMs).Select(d => d.FilterData(_outputDataFilter))
                : null;

        return (
            _inputDeserializer == null
                ? consumedInputData?.Select(d => d.CastToObjectDetailedData())
                : consumedInputData?.Select(d => d.CastToObjectDetailedData() with
                {
                    Body = _inputDeserializer!.Deserialize(d.Body, inputTypeToDeserializeTo)
                }),
            _outputDeserializer == null
                ? consumedOutputData?.Select(d => d.CastToObjectDetailedData())
                : consumedOutputData?.Select(d => d.CastToObjectDetailedData() with
                {
                    Body = _outputDeserializer!.Deserialize(d.Body, outputTypeToDeserializeTo)
                }));
    }

    /// <inheritdoc />
    protected override SerializationType? GetInputCommunicationSerializationType()
    {
        return _consumeConfig.InputDeserialize?.Deserializer;
    }

    /// <inheritdoc />
    protected override SerializationType? GetOutputCommunicationSerializationType()
    {
        return _consumeConfig.OutputDeserialize?.Deserializer;
    }

    /// <summary>
    /// Reads queue items until timeout; timeout is reset after each successful pop.
    /// </summary>
    private IEnumerable<DetailedData<byte[]>> Consume(string queueName, int timeoutMs)
    {
        Logger.LogDebug("Started consuming from Server - '{QueueName}'", queueName);
        var stopwatch = new Stopwatch();
        stopwatch.Restart();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            var serializedMessage = RedisDatabase.ListLeftPop(queueName);
            if (serializedMessage.IsNullOrEmpty) continue;
            var message = JsonSerializer.Deserialize<DetailedData<byte[]>>((byte[])serializedMessage!);
            yield return message!;
            stopwatch.Restart();
        }

        Logger.LogInformation("Stopped consuming from Server - '{QueueName}'", queueName);
    }
}

