using Microsoft.Extensions.Logging;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.ConfigurationObjects;

namespace QaaS.Runner.Sessions.Actions.MockerCommands;

public class TriggerActionMockerCommand(
    string name,
    int stage,
    TriggerAction commandConfig,
    RedisConfig redisConfig,
    string serverName,
    int requestDurationMs,
    int requestRetries,
    ILogger logger) :
    MockerCommand(name, stage, commandConfig, redisConfig, serverName, requestDurationMs, requestRetries,
        logger)
{
    /// <inheritdoc />
    protected override bool HandlesData => false;

    /// <inheritdoc />
    protected override CommandType CommandType => CommandType.TriggerAction;


    /// <inheritdoc />
    protected override SerializationType? GetInputCommunicationSerializationType()
    {
        return null;
    }

    /// <inheritdoc />
    protected override SerializationType? GetOutputCommunicationSerializationType()
    {
        return null;
    }
}

