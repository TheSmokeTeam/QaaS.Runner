using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.RuntimeOverrides;

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
    internal MockerCommandConfig? Command { get; set; }

    [Description("The duration the runner will try to request the mocker server instances")]
    [DefaultValue(3000)]
    internal int RequestDurationMs { get; set; } = 3000;

    [Description("The amount of retries the runner will try to request the mocker server instances")]
    [DefaultValue(3)]
    internal int RequestRetries { get; set; } = 3;

    /// <summary>
    /// Sets the name used for the current Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the stage used by the current Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    /// <summary>
    /// Configures server name on the current Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder WithServerName(string serverName)
    {
        ServerName = serverName;
        return this;
    }

    /// <summary>
    /// Configures redis on the current Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder WithRedis(RedisConfig redis)
    {
        Redis = redis;
        return this;
    }

    /// <summary>
    /// Configures request duration ms on the current Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder WithRequestDurationMs(int requestDurationMs)
    {
        RequestDurationMs = requestDurationMs;
        return this;
    }

    /// <summary>
    /// Configures request retries on the current Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder WithRequestRetries(int requestRetries)
    {
        RequestRetries = requestRetries;
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder Configure(MockerCommandConfig command)
    {
        Command = command;
        return this;
    }

    /// <summary>
    /// Configures command on the current Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder WithCommand(MockerCommandConfig command)
    {
        return Configure(command);
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder CreateConfiguration(MockerCommandConfig command)
    {
        return Configure(command);
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder Create(MockerCommandConfig command)
    {
        return CreateConfiguration(command);
    }

    /// <summary>
    /// Returns the configuration currently stored on the Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandConfig? ReadConfiguration()
    {
        return Command;
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder UpdateConfiguration(Func<MockerCommandConfig, MockerCommandConfig> update)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Command configuration is not set");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder UpdateConfiguration(MockerCommandConfig command)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Command configuration is not set");
        Command = ConfigurationUpdateExtensions.UpdateConfiguration(currentConfig, command);
        return this;
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder UpdateConfiguration(object configuration)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Command configuration is not set");
        Command = ConfigurationUpdateExtensions.UpdateConfiguration(currentConfig, configuration);
        return this;
    }

    /// <summary>
    /// Clears the configuration currently stored on the Runner mocker command builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner mocker command builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Mocker Commands" />
    public MockerCommandBuilder DeleteConfiguration()
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

            var overrideRequest = new MockerCommandOverrideRequest(Name!, Stage, type, Command, Redis!, ServerName!,
                RequestDurationMs, RequestRetries, context.Logger);

            return context.GetSessionActionOverrides()?.MockerCommand?.Invoke(overrideRequest) ?? type switch
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

