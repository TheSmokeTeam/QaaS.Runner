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
    /// Creates a new Runner instance from the supplied bootstrap inputs.
    /// </summary>
    /// <remarks>
    /// This is the primary code-first entry point for bootstrapping the product from command-line style arguments so library startup and CLI startup stay aligned.
    /// </remarks>
    /// <qaas-docs group="Getting Started" subgroup="Bootstrap" />
    public static Runner New(IEnumerable<string>? args = null)
    {
        return GetRunner<Runner>(args);
    }

    /// <summary>
    /// Creates a new Runner instance from the supplied bootstrap inputs.
    /// </summary>
    /// <remarks>
    /// This is the primary code-first entry point for bootstrapping the product from command-line style arguments so library startup and CLI startup stay aligned.
    /// </remarks>
    /// <qaas-docs group="Getting Started" subgroup="Bootstrap" />
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
        var commandLineArgs = NormalizeArguments(args ?? []);
        using var cliParser = ParserBuilder.BuildParser();

        if (commandLineArgs.Length == 0)
        {
            var emptyArgsResult = ParseSupportedArguments(cliParser, []);
            return WriteHelpAndCreateBootstrapHandledRunner<TRunner>(emptyArgsResult);
        }

        var cliParserResult = ParseSupportedArguments(cliParser, commandLineArgs);

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
                errors => HandleParseError<TRunner>(cliParserResult, errors));
    }

    /// <summary>
    /// Normalizes bootstrap arguments so embedded hosts can omit the explicit `run` verb when the intent is obvious.
    /// </summary>
    internal static string[] NormalizeArguments(
        IEnumerable<string> args,
        string? appBaseDirectory = null,
        Func<string, bool>? fileExists = null)
    {
        var arguments = args.ToArray();
        _ = appBaseDirectory;
        _ = fileExists;
        if (arguments.Length == 0)
            return arguments;

        if (!ShouldAssumeRunMode(arguments))
            return arguments;

        return ["run", .. arguments];
    }

    /// <summary>
    /// Handles the parser's "not parsed" channel, which CommandLineParser uses for help requests, version requests,
    /// and actual parse failures alike.
    /// Bootstrap still returns a <see cref="Runner" /> instance in all of those cases so callers can keep a simple
    /// `Bootstrap.New(...).Run()` API. To make that safe, parse-only flows return a runner with a precomputed
    /// bootstrap exit code; <see cref="Runner.RunAndGetExitCode" /> sees that sentinel and exits before setup,
    /// building, execution, or teardown begin.
    /// </summary>
    private static TRunner HandleParseError<TRunner>(ParserResult<object> cliParserResult,
        IEnumerable<Error> errors) where TRunner : Runner
    {
        var parseErrors = errors.ToArray();
        var (logger, serilogLogger, ownsSerilogLogger) = GetDefaultLoggers();

        if (IsVersionOnlyRequest(parseErrors))
        {
            const string qaasFrameworkAssemblyName = "QaaS.Framework.Executions";
            logger.LogInformation($"\nQaaS Framework Versions:\n" +
                                                   $"{qaasFrameworkAssemblyName} {GetAssemblyVersionFromName(qaasFrameworkAssemblyName)}\n");
            return CreateBootstrapHandledRunner<TRunner>(0, logger, serilogLogger, ownsSerilogLogger);
        }

        if (IsHelpOnlyRequest(parseErrors))
        {
            return WriteHelpAndCreateBootstrapHandledRunner<TRunner>(cliParserResult, 0, logger, serilogLogger,
                ownsSerilogLogger);
        }

        WriteHelpText(cliParserResult);
        logger.LogCritical("Failed to parse/process the command line arguments");
        return CreateBootstrapHandledRunner<TRunner>(1, logger, serilogLogger,
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

    private static bool ShouldAssumeRunMode(IReadOnlyList<string> arguments)
    {
        var firstArgument = arguments[0];
        if (IsExecutionModeAlias(firstArgument) ||
            IsHelpOption(firstArgument) ||
            IsVersionOption(firstArgument) ||
            IsOption(firstArgument))
        {
            return false;
        }

        return LooksLikeConfigurationPath(firstArgument);
    }

    private static bool IsExecutionModeAlias(string argument)
    {
        return string.Equals(argument, "run", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(argument, "act", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(argument, "assert", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(argument, "template", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(argument, "execute", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHelpOption(string argument)
    {
        return string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionOption(string argument)
    {
        return string.Equals(argument, "--version", StringComparison.OrdinalIgnoreCase);
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

    private static bool IsOption(string argument)
    {
        return argument.StartsWith("-", StringComparison.Ordinal);
    }

    internal static TOptions GetSafeLoggerOptions<TOptions>(TOptions options, bool forceDisableSendLogs,
        Func<TOptions, TOptions> disableSendLogs)
    {
        return forceDisableSendLogs ? disableSendLogs(options) : options;
    }

    private static RunOptions GetSafeLoggerOptions(RunOptions options) =>
        GetSafeLoggerOptions(options, ShouldForceDisableSendLogs.Value,
            currentOptions => currentOptions with { SendLogs = false });

    private static ActOptions GetSafeLoggerOptions(ActOptions options) =>
        GetSafeLoggerOptions(options, ShouldForceDisableSendLogs.Value,
            currentOptions => currentOptions with { SendLogs = false });

    private static AssertOptions GetSafeLoggerOptions(AssertOptions options) =>
        GetSafeLoggerOptions(options, ShouldForceDisableSendLogs.Value,
            currentOptions => currentOptions with { SendLogs = false });

    private static TemplateOptions GetSafeLoggerOptions(TemplateOptions options) =>
        GetSafeLoggerOptions(options, ShouldForceDisableSendLogs.Value,
            currentOptions => currentOptions with { SendLogs = false });

    private static ExecuteOptions GetSafeLoggerOptions(ExecuteOptions options) =>
        GetSafeLoggerOptions(options, ShouldForceDisableSendLogs.Value,
            currentOptions => currentOptions with { SendLogs = false });

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

    internal static bool CanUseFrameworkDefaultLoggers(Func<ILogger> loggerAccessor,
        Func<Serilog.ILogger> serilogLoggerAccessor)
    {
        try
        {
            _ = loggerAccessor();
            _ = serilogLoggerAccessor();
            return true;
        }
        catch (Exception exception) when (exception is TypeInitializationException or UriFormatException)
        {
            return false;
        }
    }

    private static bool CanUseFrameworkDefaultLoggers()
    {
        return CanUseFrameworkDefaultLoggers(
            () => Framework.Executions.ExecutionLogging.DefaultLogger,
            () => Framework.Executions.ExecutionLogging.DefaultSerilogLogger);
    }

    internal static (ILogger logger, Serilog.ILogger serilogLogger, bool ownsSerilogLogger) GetDefaultLoggers(
        bool forceDisableSendLogs)
    {
        if (!forceDisableSendLogs)
            return (Framework.Executions.ExecutionLogging.DefaultLogger, Framework.Executions.ExecutionLogging.DefaultSerilogLogger,
                false);

        var fallbackSerilogLogger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        return (new SerilogLoggerFactory(fallbackSerilogLogger).CreateLogger("BootstrapFallbackLogger"),
            fallbackSerilogLogger,
            true);
    }

    private static (ILogger logger, Serilog.ILogger serilogLogger, bool ownsSerilogLogger) GetDefaultLoggers()
    {
        return GetDefaultLoggers(ShouldForceDisableSendLogs.Value);
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

    private static void WriteHelpText(ParserResult<object> cliParserResult)
    {
        Console.Out.WriteLine(BuildHelpTextWithGuidance(cliParserResult));
    }

    private static string BuildHelpTextWithGuidance(ParserResult<object> cliParserResult)
    {
        return string.Join(
                   Environment.NewLine,
                   [
                       HelpTextBuilder.BuildHelpText(cliParserResult).ToString().TrimEnd(),
                       string.Empty,
                       "No-args guidance:",
                       "  Empty arguments only work for code-only hosts that choose a no-args path in Program.cs.",
                       "  If a YAML file is part of the scenario, pass it explicitly: dotnet run -- run <config-file>."
                   ]) +
               Environment.NewLine;
    }

    private static TRunner WriteHelpAndCreateBootstrapHandledRunner<TRunner>(
        ParserResult<object> cliParserResult,
        int exitCode = 0,
        ILogger? logger = null,
        Serilog.ILogger? serilogLogger = null,
        bool? ownsSerilogLogger = null)
        where TRunner : Runner
    {
        WriteHelpText(cliParserResult);
        return CreateBootstrapHandledRunner<TRunner>(exitCode, logger, serilogLogger, ownsSerilogLogger);
    }

    /// <summary>
    /// Creates a minimal runner with no execution builders whose only job is to carry the bootstrap decision.
    /// It is still a real runner instance so disposal is uniform, but its preset bootstrap exit code makes
    /// <see cref="Runner.RunAndGetExitCode" /> short-circuit before any execution lifecycle work begins.
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
