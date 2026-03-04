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

    private static readonly ILifetimeScope DefaultScope = BuildParentContainer();
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
        if (args == null)
        {
            var (logger, serilogLogger) = GetDefaultLoggers();
            return CreateRunner<TRunner>(DefaultScope, [], logger, serilogLogger);
        }

        // Build CLI parser and parse supported verbs
        using var cliParser = ParserBuilder.BuildParser();
        var cliParserResult = cliParser.ParseArguments<RunOptions,
            ActOptions, AssertOptions, TemplateOptions, ExecuteOptions, int>(args);

        var runner = cliParserResult
            .WithNotParsed(_ => Console.Out.WriteLine(HelpTextBuilder.BuildHelpText(cliParserResult)))
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
        var (logger, serilogLogger) = GetDefaultLoggers();

        // If all errors are version requests handle the version request case
        if (errorsArray.All(err => err.Tag is ErrorType.VersionRequestedError))
        {
            const string qaasFrameworkAssemblyName = "QaaS.Framework.Executions";
            logger.LogInformation($"\nQaaS Framework Versions:\n" +
                                                   $"{qaasFrameworkAssemblyName} {GetAssemblyVersionFromName(qaasFrameworkAssemblyName)}\n");
            return CreateRunner<TRunner>(DefaultScope, [], logger, serilogLogger);
        }

        // If all errors are help commands requested then the user asked for help and arguments are valid
        if (errorsArray.All(err => err.Tag is ErrorType.HelpRequestedError or ErrorType.HelpVerbRequestedError))
            return CreateRunner<TRunner>(DefaultScope, [], logger, serilogLogger);

        logger.LogCritical("Failed to parse/process the command line arguments");
        return CreateRunner<TRunner>(DefaultScope, [], logger, serilogLogger);
    }

    // Create a custom runner using activator to dynamically get a new instance of any implementation of TRunner
    private static TRunner CreateRunner<TRunner>(ILifetimeScope scope, List<ExecutionBuilder> executionBuilders,
        ILogger logger, Serilog.ILogger serilogLogger, bool emptyResults = false, bool serveResults = false)
        where TRunner : Runner
    {
        return (TRunner)Activator.CreateInstance(
            typeof(TRunner),
            scope,
            executionBuilders,
            logger,
            serilogLogger,
            emptyResults,
            serveResults
        )!;
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

    private static (ILogger logger, Serilog.ILogger serilogLogger) GetDefaultLoggers()
    {
        if (!ShouldForceDisableSendLogs.Value)
            return (Framework.Executions.Constants.DefaultLogger, Framework.Executions.Constants.DefaultSerilogLogger);

        var fallbackSerilogLogger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        return (new SerilogLoggerFactory(fallbackSerilogLogger).CreateLogger("BootstrapFallbackLogger"),
            fallbackSerilogLogger);
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
}
