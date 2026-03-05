using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.MockerObjects.ConfigurationObjects.Command;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;

namespace QaaS.Runner.Sessions.Actions.MockerCommands;

public class MockerCommandBuilder
{
    [Required]
    [Description("The name of the mocker command")]
    public string? Name { get; internal set; }

    [DefaultValue((int)OrderedActions.MockerCommands), Description("The stage in which the Mocker Command runs at")]
    internal int Stage { get; set; } = (int)OrderedActions.MockerCommands;

    [Required]
    [Description("The name of the mocker server to interact with")]
    internal string? ServerName { get; set; }

    [Required]
    [Description("The server controller redis API")]
    internal RedisConfig? Redis { get; set; }

    [Required]
    [Description("The command action to commit")]
    internal CommandConfig? Command { get; set; }

    [Description("The duration the runner will try to request the mocker server instances")]
    [DefaultValue(3000)]
    internal int RequestDurationMs { get; set; } = 3000;

    [Description("The amount of retries the runner will try to request the mocker server instances")]
    [DefaultValue(3)]
    internal int RequestRetries { get; set; } = 3;

    public MockerCommandBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    public MockerCommandBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    public MockerCommandBuilder WithServerName(string serverName)
    {
        ServerName = serverName;
        return this;
    }

    public MockerCommandBuilder WithRedis(RedisConfig redis)
    {
        Redis = redis;
        return this;
    }

    public MockerCommandBuilder WithRequestDurationMs(int requestDurationMs)
    {
        RequestDurationMs = requestDurationMs;
        return this;
    }

    public MockerCommandBuilder WithRequestRetries(int requestRetries)
    {
        RequestRetries = requestRetries;
        return this;
    }

    public MockerCommandBuilder WithCommand(CommandConfig command)
    {
        Command = command;
        return this;
    }

    public MockerCommandBuilder CreateCommand(CommandConfig command)
    {
        return WithCommand(command);
    }

    public CommandConfig? ReadCommand()
    {
        return Command;
    }

    public MockerCommandBuilder UpdateCommand(Func<CommandConfig, CommandConfig> update)
    {
        Command = update(Command ?? throw new InvalidOperationException("Command configuration is not set"));
        return this;
    }

    public MockerCommandBuilder DeleteCommand()
    {
        Command = null;
        return this;
    }


    /// <summary>
    /// Builds the configured mocker command type and writes recoverable build failures to <paramref name="actionFailures"/>.
    /// </summary>
    internal MockerCommand? Build(InternalContext context, IList<ActionFailure> actionFailures, string sessionName)
    {
        object? type = null;
        try
        {
            if (Command == null)
                throw new InvalidOperationException($"Missing command configuration in Mocker Command {Name}");

            var supportedCommands = new List<object?>
                { Command.ChangeActionStub, Command.TriggerAction, Command.Consume };
            type = supportedCommands.FirstOrDefault(configuredType => configuredType != null) ??
                   throw new InvalidOperationException($"Missing supported type in Mocker Command {Name}");
            if (supportedCommands.Count(config => config != null) > 1)
            {
                var conflictingConfigs = supportedCommands
                    .Where(config => config != null)
                    .Select(config => config!.GetType().Name)
                    .ToArray();
                throw new InvalidOperationException(
                    $"Multiple configurations provided for Command '{Name}': {string.Join(", ", conflictingConfigs)}. " +
                    "Only one type is allowed at a time.");
            }
            context.Logger.LogDebugWithMetaData("Started building MockerCommand of type {type}", context.GetMetaDataFromContext(), type.ToString());

            return type switch
            {
                ChangeActionStub => new ChangeActionStubMockerCommand(Name!, Stage,
                    Command.ChangeActionStub!, Redis!, ServerName!,
                    RequestDurationMs, RequestRetries, context.Logger),
                Consume => new ConsumeMockerCommand(Name!, Stage, Command.Consume!,
                    Redis!, ServerName!, RequestDurationMs,
                    RequestRetries, context.Logger),
                TriggerAction => new TriggerActionMockerCommand(Name!, Stage,
                    Command.TriggerAction!, Redis!, ServerName!,
                    RequestDurationMs, RequestRetries, context.Logger),
                _ => throw new InvalidOperationException("Mocker command type not supported")
            };
        }
        catch (Exception e)
        {
            actionFailures.AppendActionFailure(e, sessionName, context.Logger, nameof(MockerCommand), Name!,
                type?.GetType().ToString());
        }

        return null;
    }
}
