using System.Reflection;
using Autofac;
using CommandLine;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions.CommandLineBuilders;
using QaaS.Runner.Loaders;
using QaaS.Runner.Options;
using Serilog;
using Serilog.Extensions.Logging;
using ExecuteOptions = QaaS.Runner.Options.ExecuteOptions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace QaaS.Runner;

/// <summary>
/// Bootstrap class responsible for initializing core QaaS objects
/// </summary>
public static class Bootstrap
{
    private static readonly ILifetimeScope DefaultRootScope = BuildParentContainer();
    private static readonly Lazy<bool> ShouldForceDisableSendLogs = new(() => !CanUseFrameworkDefaultLoggers());

    /// <summary>
    /// Creates a new <see cref="Runner" /> instance using default <see cref="Runner" /> type
    /// </summary>
    /// <param name="args">Command-line arguments</param>
    /// <returns>A <see cref="Runner" /> instance</returns>
    public static Runner New(IEnumerable<string>? args = null)
    {
        return GetRunner<Runner>(args);
    }

    /// <summary>
    /// Creates new <see cref="Runner" /> instance filled with QaaS objects based on parsed command-line arguments
    /// </summary>
    /// <param name="args">Command-line arguments</param>
    /// <returns>A <see cref="Runner" /> instance (or derived type)</returns>
    public static Runner New<TRunner>(IEnumerable<string>? args = null) where TRunner : Runner
    {
        return GetRunner<TRunner>(args);
    }

    /// <summary>
    /// Creates a new runner instance with custom logic
    /// </summary>
    /// <param name="args">Command-line arguments</param>
    /// <param name="executionId">Optional execution ID</param>
    /// <returns>A <see cref="Runner" /> instance</returns>
    internal static TRunner GetRunner<TRunner>(IEnumerable<string>? args, string? executionId = null)
        where TRunner : Runner
    {
        var commandLineArgs = args?.ToArray();
        if (commandLineArgs == null || commandLineArgs.Length == 0)
        {
            using var emptyArgsParser = ParserBuilder.BuildParser();
            return WriteHelpAndCreateBootstrapHandledRunner<TRunner>(emptyArgsParser);
        }

        // Build CLI parser and parse supported verbs
        using var cliParser = ParserBuilder.BuildParser();
        var cliParserResult = cliParser.ParseArguments<RunOptions,
            ActOptions, AssertOptions, TemplateOptions, ExecuteOptions, int>(commandLineArgs);

        var runner = cliParserResult
            .WithNotParsed(errors =>
            {
                var parseErrors = errors.ToArray();
                if (parseErrors.All(error => error.Tag is ErrorType.VersionRequestedError))
                    return;

                Console.Out.WriteLine(HelpTextBuilder.BuildHelpText(cliParserResult));
            })
            .MapResult(
                (TemplateOptions options) =>
                    new RunLoader<TRunner, TemplateOptions>(GetSafeLoggerOptions(options), executionId).GetLoadedRunner(),
                (RunOptions options) =>
                    new RunLoader<TRunner, RunOptions>(GetSafeLoggerOptions(options), executionId).GetLoadedRunner(),
                (ActOptions options) =>
                    new RunLoader<TRunner, ActOptions>(GetSafeLoggerOptions(options), executionId).GetLoadedRunner(),
                (AssertOptions options) =>
                    new RunLoader<TRunner, AssertOptions>(GetSafeLoggerOptions(options), executionId).GetLoadedRunner(),
                (ExecuteOptions options) => new ExecuteLoader<TRunner>(GetSafeLoggerOptions(options)).GetLoadedRunner(),
                HandleParseError<TRunner>);

        return runner;
    }

    /// <summary>
    /// Handles parsing errors and returns an empty <see cref="Runner" /> object
    /// </summary>
    private static TRunner HandleParseError<TRunner>(IEnumerable<Error> errors) where TRunner : Runner
    {
        var errorsArray = errors.ToArray();
        var (logger, serilogLogger, ownsSerilogLogger) = GetDefaultLoggers();

        // If all errors are version requests handle the version request case
        if (errorsArray.All(err => err.Tag is ErrorType.VersionRequestedError))
        {
            const string qaasFrameworkAssemblyName = "QaaS.Framework.Executions";
            logger.LogInformation($"\nQaaS Framework Versions:\n" +
                                                   $"{qaasFrameworkAssemblyName} {GetAssemblyVersionFromName(qaasFrameworkAssemblyName)}\n");
            return CreateBootstrapHandledRunner<TRunner>(0, logger, serilogLogger, ownsSerilogLogger);
        }

        // Help requests were already printed to stdout and should not continue into the runner lifecycle.
        if (errorsArray.All(err => err.Tag is ErrorType.HelpRequestedError or ErrorType.HelpVerbRequestedError))
            return CreateBootstrapHandledRunner<TRunner>(0, logger, serilogLogger, ownsSerilogLogger);

        logger.LogCritical("Failed to parse/process the command line arguments");
        return CreateBootstrapHandledRunner<TRunner>(1, logger, serilogLogger, ownsSerilogLogger);
    }

    // Create a custom runner using activator to dynamically get a new instance of any implementation of TRunner
    private static TRunner CreateRunner<TRunner>(ILifetimeScope scope, List<ExecutionBuilder> executionBuilders,
        ILogger logger, Serilog.ILogger serilogLogger, bool emptyResults = false, bool serveResults = false,
        bool disposeSerilogLogger = true)
        where TRunner : Runner
    {
        var runner = (TRunner)Activator.CreateInstance(
            typeof(TRunner),
            scope,
            executionBuilders,
            logger,
            serilogLogger,
            emptyResults,
            serveResults
        )!;
        runner.WithSerilogLoggerDisposal(disposeSerilogLogger);
        return runner;
    }

    private static string GetAssemblyVersionFromName(string assemblyName)
    {
        return Assembly.Load(assemblyName)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    }

    private static bool CanUseFrameworkDefaultLoggers()
    {
        try
        {
            _ = Framework.Executions.Constants.DefaultLogger;
            _ = Framework.Executions.Constants.DefaultSerilogLogger;
            return true;
        }
        catch (Exception exception) when (exception is TypeInitializationException or UriFormatException)
        {
            return false;
        }
    }

    private static (ILogger logger, Serilog.ILogger serilogLogger, bool ownsSerilogLogger) GetDefaultLoggers()
    {
        if (!ShouldForceDisableSendLogs.Value)
            return (Framework.Executions.Constants.DefaultLogger, Framework.Executions.Constants.DefaultSerilogLogger,
                false);

        var fallbackSerilogLogger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        return (new SerilogLoggerFactory(fallbackSerilogLogger).CreateLogger("BootstrapFallbackLogger"),
            fallbackSerilogLogger,
            true);
    }

    private static RunOptions GetSafeLoggerOptions(RunOptions options)
    {
        return ShouldForceDisableSendLogs.Value ? options with { SendLogs = false } : options;
    }

    private static ActOptions GetSafeLoggerOptions(ActOptions options)
    {
        return ShouldForceDisableSendLogs.Value ? options with { SendLogs = false } : options;
    }

    private static AssertOptions GetSafeLoggerOptions(AssertOptions options)
    {
        return ShouldForceDisableSendLogs.Value ? options with { SendLogs = false } : options;
    }

    private static TemplateOptions GetSafeLoggerOptions(TemplateOptions options)
    {
        return ShouldForceDisableSendLogs.Value ? options with { SendLogs = false } : options;
    }

    private static ExecuteOptions GetSafeLoggerOptions(ExecuteOptions options)
    {
        return ShouldForceDisableSendLogs.Value ? options with { SendLogs = false } : options;
    }

    private static ILifetimeScope BuildParentContainer()
    {
        var containerBuilder = new ContainerBuilder();
        return containerBuilder.Build();
    }

    private static TRunner CreateDefaultRunner<TRunner>(
        ILogger? logger = null,
        Serilog.ILogger? serilogLogger = null,
        bool? ownsSerilogLogger = null)
        where TRunner : Runner
    {
        (ILogger logger, Serilog.ILogger serilogLogger, bool ownsSerilogLogger) resolvedLoggers;
        if (logger != null && serilogLogger != null && ownsSerilogLogger.HasValue)
        {
            resolvedLoggers = (logger, serilogLogger, ownsSerilogLogger.Value);
        }
        else
        {
            resolvedLoggers = GetDefaultLoggers();
        }

        return CreateRunner<TRunner>(
            DefaultRootScope.BeginLifetimeScope(),
            [],
            resolvedLoggers.logger,
            resolvedLoggers.serilogLogger,
            disposeSerilogLogger: resolvedLoggers.ownsSerilogLogger);
    }

    /// <summary>
    /// Prints the top-level help text for empty command lines and returns a runner that only carries the exit code.
    /// </summary>
    private static TRunner WriteHelpAndCreateBootstrapHandledRunner<TRunner>(Parser cliParser) where TRunner : Runner
    {
        var emptyArgsResult = cliParser.ParseArguments<RunOptions, ActOptions, AssertOptions, TemplateOptions,
            ExecuteOptions, int>([]);
        Console.Out.WriteLine(HelpTextBuilder.BuildHelpText(emptyArgsResult));
        return CreateBootstrapHandledRunner<TRunner>(0);
    }

    /// <summary>
    /// Creates a runner that should stop after bootstrap because the command line was fully handled already.
    /// </summary>
    private static TRunner CreateBootstrapHandledRunner<TRunner>(
        int exitCode,
        ILogger? logger = null,
        Serilog.ILogger? serilogLogger = null,
        bool? ownsSerilogLogger = null)
        where TRunner : Runner
    {
        return (TRunner)CreateDefaultRunner<TRunner>(logger, serilogLogger, ownsSerilogLogger)
            .WithBootstrapHandledExitCode(exitCode);
    }
}
