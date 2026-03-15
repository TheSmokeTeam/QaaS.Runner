using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.Testing;

namespace QaaS.Runner.Sessions.Actions.MockerCommands;

/// <summary>
/// Fluent builder for mocker command actions that validates command shape and creates the matching command runtime.
/// </summary>
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

    /// <summary>
    /// Sets the command name.
    /// </summary>
    public MockerCommandBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the stage in which this command runs.
    /// </summary>
    public MockerCommandBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    /// <summary>
    /// Sets the target mocker server name.
    /// </summary>
    public MockerCommandBuilder WithServerName(string serverName)
    {
        ServerName = serverName;
        return this;
    }

    /// <summary>
    /// Sets redis connectivity used to communicate with the mocker.
    /// </summary>
    public MockerCommandBuilder WithRedis(RedisConfig redis)
    {
        Redis = redis;
        return this;
    }

    /// <summary>
    /// Sets per-request wait duration in milliseconds between retries.
    /// </summary>
    public MockerCommandBuilder WithRequestDurationMs(int requestDurationMs)
    {
        RequestDurationMs = requestDurationMs;
        return this;
    }

    /// <summary>
    /// Sets retry count for ping/command requests.
    /// </summary>
    public MockerCommandBuilder WithRequestRetries(int requestRetries)
    {
        RequestRetries = requestRetries;
        return this;
    }

    /// <summary>
    /// Sets command-specific configuration. Exactly one supported command type must be configured.
    /// </summary>
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

    public MockerCommandBuilder UpsertCommand(CommandConfig command)
    {
        Command = command;
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
    internal StagedAction? Build(InternalContext context, IList<ActionFailure> actionFailures, string sessionName)
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
            var commandTypeName = type.GetType().Name;
            context.Logger.LogDebugWithMetaData("Started building MockerCommand of type {type}",
                context.GetMetaDataOrDefault(), new object?[] { commandTypeName });

            var factoryRequest = new MockerCommandFactoryRequest(Name!, Stage, type, Command, Redis!, ServerName!,
                RequestDurationMs, RequestRetries, context.Logger);

            return context.GetSessionActionFactoryOverrides()?.MockerCommandFactory?.Invoke(factoryRequest) ?? type
                switch
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
                type?.GetType().Name);
        }

        return null;
    }
}

