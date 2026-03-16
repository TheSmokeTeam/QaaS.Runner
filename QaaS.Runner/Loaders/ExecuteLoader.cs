using Autofac;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.ConfigurationBuilderExtensions;
using QaaS.Framework.Executions.Loaders;
using QaaS.Runner.ConfigurationObjects;
using ExecuteOptions = QaaS.Runner.Options.ExecuteOptions;

namespace QaaS.Runner.Loaders;

/// <summary>
/// Loads an `execute` command that fans out into multiple nested run-like commands.
/// Unlike <see cref="RunLoader{TRunner,TOptions}" />, this loader first parses an execution YAML file, bootstraps a
/// runner per command, and then flattens all child execution builders into one outer runner.
/// </summary>
/// <typeparam name="TRunner">The type of runner to instantiate, which must inherit from <see cref="Runner" /></typeparam>
public class ExecuteLoader<TRunner> : BaseLoader<ExecuteOptions, TRunner> where TRunner : Runner
{
    private readonly ILifetimeScope _runScope;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ExecuteLoader{TRunner}" /> class
    /// </summary>
    /// <param name="options">The execution options containing configuration and command settings</param>
    /// <param name="executionId">An optional ID to identify the execution session</param>
    public ExecuteLoader(ExecuteOptions options, string? executionId = null) : base(options, executionId)
    {
        _runScope = Bootstrap.CreateRunnerScope();
    }

    /// <summary>
    ///     Filters the list of available commands to only those specified in <see cref="ExecuteOptions.CommandIdsToRun" />
    ///     Validates that all requested command IDs exist and throws an exception if any are missing
    /// </summary>
    /// <param name="allCommandsClustered">The full list of available commands from the configuration</param>
    /// <returns>A filtered list of commands to execute based on the provided IDs</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if any command ID in <see cref="ExecuteOptions.CommandIdsToRun" />
    ///     does not exist
    /// </exception>
    private IEnumerable<CommandConfig> GetCommandsToRun(List<CommandConfig> allCommandsClustered)
    {
        // Find if none existing command ids were given to run
        var executableCommandIds = allCommandsClustered.Select(command => command.Id!).ToList();
        var notFoundCommandsIdsToRun = Options.CommandIdsToRun.Except(executableCommandIds).ToList();
        if (notFoundCommandsIdsToRun.Any())
        {
            Logger.LogDebug("Existing command ids received: {ExistingCommandIds}",
                string.Join(", ",
                    executableCommandIds.Intersect(Options.CommandIdsToRun)));
            throw new InvalidOperationException("Received non-existing commands from `command-ids-to-run` flag: " +
                                                string.Join(", ", notFoundCommandsIdsToRun));
        }

        // Find if no command ids were given to run which means to run all command ids
        if (!Options.CommandIdsToRun.Any()) return allCommandsClustered;

        return allCommandsClustered.Where(command => Options.CommandIdsToRun.Contains(command.Id!)).ToList();
    }

    /// <summary>
    ///     Constructs and returns a runner of type <typeparamref name="TRunner" /> configured with execution builders from the
    ///     specified commands.
    ///     Loads and validates the configuration file, filters commands by ID, and bootstraps runners for each command.
    ///     Aggregates all execution builders into a single list and passes them to the runner constructor.
    /// </summary>
    /// <returns>A new instance of <typeparamref name="TRunner" /> configured with all required dependencies.</returns>
    public override TRunner GetLoadedRunner()
    {
        // Load executable YAML configuration
        var executableYaml = new ConfigurationBuilder()
            .AddYaml(Options.ConfigurationFile!)
            .AddEnvironmentVariables().AddPlaceholderResolver().Build()
            .LoadAndValidateConfiguration<ExecuteConfigurations>();

        // Filter commands based on command-ids-to-run
        var commandsToRun = GetCommandsToRun(executableYaml.Commands!.ToList());

        // Bootstrap a runner for each command
        var runs = commandsToRun.Select(command =>
        {
            var stringCommand = CommandLineParser.SplitCommandLineIntoArguments(command.Command!, true).ToArray();
            if (stringCommand[0] == "execute")
                throw new ArgumentException($"The command {command.Id} in Execute cannot be execute itself");
            return Bootstrap.GetRunner<TRunner>(stringCommand, command.Id);
        });

        var allExecutions = runs.Select(run => run.ExecutionBuilders).SelectMany(runExecutions => runExecutions)
            .ToList();

        var runner = Bootstrap.CreateRunner<TRunner>(
            _runScope,
            allExecutions,
            Logger,
            SerilogLogger,
            Options.EmptyAllureDirectory,
            Options.AutoServeTestResults);
        runner.ExitProcessOnCompletion = !Options.NoProcessExit;
        return runner;
    }
}
