using System.Reflection;
using Autofac;
using CommandLine;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions.CommandLineBuilders;
using QaaS.Runner.Loaders;
using QaaS.Runner.Modules;
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
        var commandLineArgs = args?.ToArray() ?? [];
        using var cliParser = ParserBuilder.BuildParser();
        var cliParserResult = ParseSupportedArguments(cliParser, commandLineArgs);

        // An empty command line is treated as an explicit "show me the top-level usage" request.
        // The parser still reports it as "no verb selected", but bootstrap upgrades that case to a
        // successful help flow instead of an error so callers can safely do Bootstrap.New(null).Run().
        if (commandLineArgs.Length == 0)
        {
            return HandleNotParsedResult<TRunner>(cliParserResult, GetParseErrors(cliParserResult),
                treatNoVerbAsHelpOnly: true);
        }

        // Help, version, and real parse failures all surface as NotParsed results. We convert them into a
        // bootstrap-handled runner here so the public API stays "always return a Runner" while still ensuring
        // the caller will not accidentally start an empty execution lifecycle after CLI handling is complete.
        if (cliParserResult is NotParsed<object> notParsedResult)
        {
            return HandleNotParsedResult<TRunner>(cliParserResult, notParsedResult.Errors,
                treatNoVerbAsHelpOnly: false);
        }

        return cliParserResult
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
                errors => throw new InvalidOperationException(
                    $"Parser returned unexpected not-parsed result: {string.Join(", ", errors.Select(error => error.Tag))}"));
    }

    /// <summary>
    /// Converts a not-parsed command line result into a bootstrap-handled runner.
    /// CommandLineParser models help/version requests and actual parse failures with the same <see cref="Error" />
    /// channel, but <see cref="Bootstrap.New(IEnumerable{string})" /> still promises to return a <see cref="Runner" />
    /// instance in every case. This method therefore performs two jobs:
    /// 1) emit the user-facing CLI output once (help text or version text)
    /// 2) return a runner that already knows its final exit code and will skip the execution lifecycle later
    ///    when <see cref="Runner.RunAndGetExitCode" /> sees <c>BootstrapHandledExitCode</c>
    /// </summary>
    private static TRunner HandleNotParsedResult<TRunner>(ParserResult<object> cliParserResult,
        IEnumerable<Error> errors, bool treatNoVerbAsHelpOnly) where TRunner : Runner
    {
        var errorsArray = errors.ToArray();
        var (logger, serilogLogger, ownsSerilogLogger) = GetDefaultLoggers();

        // CommandLineParser reports "--version" as a parse error, but from the runner API perspective it is a fully
        // handled success path: print the versions, then return a sentinel runner that does nothing else.
        if (IsVersionOnlyRequest(errorsArray))
        {
            const string qaasFrameworkAssemblyName = "QaaS.Framework.Executions";
            logger.LogInformation($"\nQaaS Framework Versions:\n" +
                                                   $"{qaasFrameworkAssemblyName} {GetAssemblyVersionFromName(qaasFrameworkAssemblyName)}\n");
            return CreateBootstrapHandledRunner<TRunner>(0, logger, serilogLogger, ownsSerilogLogger);
        }

        var shouldTreatAsHelpOnly = treatNoVerbAsHelpOnly || IsHelpOnlyRequest(errorsArray);
        WriteHelpText(cliParserResult);

        if (!shouldTreatAsHelpOnly)
        {
            logger.LogCritical("Failed to parse/process the command line arguments");
        }

        return CreateBootstrapHandledRunner<TRunner>(shouldTreatAsHelpOnly ? 0 : 1, logger, serilogLogger,
            ownsSerilogLogger);
    }

    private static bool IsVersionOnlyRequest(IEnumerable<Error> errors)
    {
        return errors.All(error => error.Tag is ErrorType.VersionRequestedError);
    }

    private static bool IsHelpOnlyRequest(IEnumerable<Error> errors)
    {
        return errors.All(error => error.Tag is ErrorType.HelpRequestedError or ErrorType.HelpVerbRequestedError);
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

    // Create a custom runner using activator to dynamically get a new instance of any implementation of TRunner
    internal static TRunner CreateRunner<TRunner>(ILifetimeScope scope, List<ExecutionBuilder> executionBuilders,
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

    /// <summary>
    /// Creates the minimal lifetime scope a runner needs at runtime.
    /// Both <see cref="RunLoader{TRunner,TOptions}" /> and <see cref="ExecuteLoader{TRunner}" /> build configuration
    /// directly and only rely on Autofac later for runner lifecycle helpers such as Allure cleanup and serving.
    /// Keeping that scope minimal avoids the older duplicated loader-specific scope initialization code.
    /// </summary>
    internal static ILifetimeScope CreateRunnerScope()
    {
        var containerBuilder = new ContainerBuilder();
        containerBuilder.RegisterModule<AllureWrapperModule>();
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
            CreateRunnerScope(),
            [],
            resolvedLoggers.logger,
            resolvedLoggers.serilogLogger,
            disposeSerilogLogger: resolvedLoggers.ownsSerilogLogger);
    }

    private static ParserResult<object> ParseSupportedArguments(Parser cliParser, IEnumerable<string> args)
    {
        return cliParser.ParseArguments<RunOptions, ActOptions, AssertOptions, TemplateOptions, ExecuteOptions, int>(
            args);
    }

    private static IEnumerable<Error> GetParseErrors(ParserResult<object> cliParserResult)
    {
        return cliParserResult is NotParsed<object> notParsedResult ? notParsedResult.Errors : [];
    }

    private static void WriteHelpText(ParserResult<object> cliParserResult)
    {
        Console.Out.WriteLine(HelpTextBuilder.BuildHelpText(cliParserResult));
    }

    /// <summary>
    /// Creates a minimal runner with no execution builders whose only job is to carry the final bootstrap decision.
    /// The runner still needs real logger and scope instances so <see cref="Runner.RunAndGetExitCode" /> can dispose
    /// resources uniformly, but the bootstrap exit code makes that runner skip setup/build/start/teardown entirely.
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
