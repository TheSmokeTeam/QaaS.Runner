using Autofac;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.ConfigurationBuilderExtensions;
using QaaS.Framework.Configurations.CustomExceptions;
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
            throw new InvalidOperationException(RunnerDiagnosticMessageFormatter.Format(
                "The command-ids-to-run filter contains command ids that do not exist in the execute configuration.",
                [
                    $"Requested command ids not found: {RunnerDiagnosticMessageFormatter.SummarizeValues(notFoundCommandsIdsToRun)}",
                    $"Available command ids: {RunnerDiagnosticMessageFormatter.SummarizeValues(executableCommandIds)}",
                    $"Execute configuration file: {Options.ConfigurationFile}"
                ],
                null,
                null,
                [
                    "Update the command-ids-to-run values or the execute configuration file and retry."
                ]));
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
        var reporterMode = GetReporterModeOrThrow();
        ValidateReporterSpecificOptions(reporterMode);
        var executeConfigurationPath = GetExecuteConfigurationPathOrThrow();

        ExecuteConfigurations executableYaml;
        try
        {
            // Load executable YAML configuration
            executableYaml = new ConfigurationBuilder()
                .AddYaml(executeConfigurationPath)
                .EnrichedBuild(addEnvironmentVariables: true)
                .LoadAndValidateConfiguration<ExecuteConfigurations>();
        }
        catch (Exception exception) when (RunnerYamlConfigurationExceptionFactory.ShouldWrap(exception))
        {
            throw RunnerYamlConfigurationExceptionFactory.CreateLocalFileLoadException(
                executeConfigurationPath,
                exception);
        }

        // Filter commands based on command-ids-to-run
        var commandsToRun = GetCommandsToRun(executableYaml.Commands!.ToList());

        // Bootstrap a runner for each command
        var runs = commandsToRun.Select(command =>
        {
            var stringCommand = RemoveInvocationScopedOptions(
                CommandLineParser.SplitCommandLineIntoArguments(command.Command!, true).ToArray());
            if (stringCommand[0] == "execute")
                throw new ArgumentException(RunnerDiagnosticMessageFormatter.Format(
                    "Execute configurations cannot contain nested execute commands.",
                    [
                        $"Command id: {command.Id ?? "<none>"}",
                        $"Command text: {command.Command ?? "<none>"}",
                        $"Execute configuration file: {Options.ConfigurationFile}"
                    ],
                    null,
                    null,
                    [
                        "Use run, act, assert, or template inside Commands instead of nesting execute."
                    ]));
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
        runner.WithServeResultsFolder(Options.AutoServeTestResults ? Options.GetServeResultsFolderOrDefault() : null);
        runner.WithReporterMode(reporterMode);
        runner.ExitProcessOnCompletion = !Options.NoProcessExit;
        return runner;
    }

    private static string[] RemoveInvocationScopedOptions(IReadOnlyList<string> commandArguments)
    {
        var filteredArguments = new List<string>(commandArguments.Count);

        for (var index = 0; index < commandArguments.Count; index++)
        {
            var argument = commandArguments[index];

            if (TryMatchOption(argument, "--reporter"))
            {
                if (index + 1 < commandArguments.Count && !IsOption(commandArguments[index + 1]))
                    index++;

                continue;
            }

            if (TryMatchOption(argument, "-s", "--serve-results"))
            {
                if (index + 1 < commandArguments.Count &&
                    !IsOption(commandArguments[index + 1]) &&
                    !LooksLikeConfigurationPath(commandArguments[index + 1]))
                {
                    index++;
                }

                continue;
            }

            if (TryMatchOption(argument, "-e", "--empty-results-directory", "--empty-allure-directory"))
                continue;

            filteredArguments.Add(argument);
        }

        return filteredArguments.ToArray();
    }

    private static bool TryMatchOption(string argument, params string[] optionNames)
    {
        foreach (var optionName in optionNames)
        {
            if (string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (optionName.StartsWith("--", StringComparison.Ordinal) &&
                argument.StartsWith($"{optionName}=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOption(string argument)
    {
        return argument.StartsWith("-", StringComparison.Ordinal);
    }

    private static bool LooksLikeConfigurationPath(string argument)
    {
        if (Path.IsPathRooted(argument))
            return true;

        if (argument.IndexOfAny(['\\', '/']) >= 0)
            return true;

        var extension = Path.GetExtension(argument);
        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }

    private ReporterMode GetReporterModeOrThrow()
    {
        try
        {
            return Options.GetReporterModeOrDefault();
        }
        catch (ArgumentException exception)
        {
            throw new InvalidConfigurationsException(exception.Message, exception);
        }
    }

    private void ValidateReporterSpecificOptions(ReporterMode reporterMode)
    {
        if (reporterMode != ReporterMode.ReportPortal)
            return;

        if (Options.AutoServeTestResults)
            throw new InvalidConfigurationsException(
                "The '--serve-results' flag is only valid when Allure reporting is enabled.");

        if (Options.EmptyAllureDirectory)
            throw new InvalidConfigurationsException(
                "The '--empty-allure-directory' flag is only valid when Allure reporting is enabled.");
    }

    private string GetExecuteConfigurationPathOrThrow()
    {
        if (PathUtils.IsPathHttpUrl(Options.ConfigurationFile))
            return Options.ConfigurationFile!;

        var resolvedConfigurationFilePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,
            Options.ConfigurationFile!));

        try
        {
            using var _ = File.Open(resolvedConfigurationFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Options.ConfigurationFile!;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CouldNotFindConfigurationException(
                RunnerDiagnosticMessageFormatter.Format(
                    "Execute configuration file was not found.",
                    [
                        $"Configured path: {Options.ConfigurationFile}",
                        $"Resolved local path: {resolvedConfigurationFilePath}"
                    ],
                    null,
                    null,
                    [
                        "Provide a valid execute YAML file and retry."
                    ]),
                exception);
        }
    }
}
