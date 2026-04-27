using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.CustomExceptions;
using QaaS.Framework.Executions.Loaders;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Artifactory;
using QaaS.Runner.Options;
using BaseOptions = QaaS.Runner.Options.BaseOptions;

namespace QaaS.Runner.Loaders;

/// <summary>
/// Loads a single run-like command (`run`, `act`, `assert`, or `template`) into execution builders.
/// Unlike <see cref="ExecuteLoader{TRunner}" />, this loader does not orchestrate nested commands; it translates one
/// configuration entry point plus optional case expansion into one runner instance.
/// </summary>
/// <typeparam name="TRunner">The runner type (must inherit from Runner).</typeparam>
/// <typeparam name="TOptions">The options type (must inherit from BaseOptions).</typeparam>
public class RunLoader<TRunner, TOptions> : BaseLoader<TOptions, TRunner>
    where TRunner : Runner where TOptions : BaseOptions
{
    private const uint HttpClientTimeoutSeconds = 100;
    private readonly IFileSystem _fileSystem = new FileSystem();
    private readonly IJfrogArtifactoryHelper _jfrogArtifactoryHelper = new JfrogArtifactoryHelper();
    private readonly ILifetimeScope _runScope;
    private readonly Lazy<IReadOnlyList<IExecutionBuilderConfigurator>> _executionBuilderConfigurators;
    private bool _missingConfigurationFileWarningLogged;

    /// <summary>
    /// Creates the runner scope used later for setup/teardown services such as Allure cleanup/serving.
    /// Context construction in this loader is done directly and does not rely on Autofac registrations.
    /// </summary>
    public RunLoader(TOptions options, string? executionId = null) : base(options,
        executionId)
    {
        _runScope = Bootstrap.CreateRunnerScope();
        _executionBuilderConfigurators = new Lazy<IReadOnlyList<IExecutionBuilderConfigurator>>(
            DiscoverExecutionBuilderConfigurators);
    }

    /// <summary>
    /// Builds Context from scope and an optional caseFilePath.
    /// </summary>
    /// <returns>The built Context</returns>
    protected virtual InternalContext BuildContext(string? executionId, string? relativeCaseFilePath = null,
        IContextBuilder? contextBuilder = null)
    {
        contextBuilder ??= new ContextBuilder(new ConfigurationBuilder(),
            Constants.SupportedReferenceLists, Constants.SupportedUniqueIdsPathRegexes);
        contextBuilder.SetLogger(Logger);
        contextBuilder.SetExecutionId(executionId);
        var shouldLoadConfigurationFile = ShouldLoadConfigurationFile();
        if (shouldLoadConfigurationFile)
            contextBuilder.SetConfigurationFile(Options.ConfigurationFile!);
        contextBuilder.SetCurrentRunningSessions(
            new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>()));
        foreach (var overwriteFile in Options.OverwriteFiles)
            contextBuilder.WithOverwriteFile(overwriteFile);
        foreach (var overwriteFolder in Options.OverwriteFolders)
            contextBuilder.WithOverwriteFolder(overwriteFolder);
        contextBuilder.SetCase(relativeCaseFilePath);
        foreach (var overwriteArgument in Options.OverwriteArguments)
            contextBuilder.WithOverwriteArgument(overwriteArgument);
        foreach (var referenceConfig in Options.GetParsedPushReferences())
            contextBuilder.WithReferenceResolution(referenceConfig);
        if (Options.ResolveCasesLast) contextBuilder.ResolveCaseLast();
        if (!Options.DontResolveWithEnvironmentVariables) contextBuilder.WithEnvironmentVariableResolution();

        try
        {
            return contextBuilder.BuildInternal();
        }
        catch (Exception exception) when (shouldLoadConfigurationFile &&
                                         RunnerYamlConfigurationExceptionFactory.ShouldWrap(exception))
        {
            throw RunnerYamlConfigurationExceptionFactory.CreateLocalFileLoadException(
                Options.ConfigurationFile!,
                exception);
        }
    }

    private List<InternalContext> GetContextsWithJfrogArtifactoryCases(string? executionId, string casesDirectoryPath)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(HttpClientTimeoutSeconds);
        return _jfrogArtifactoryHelper.GetUrlsToAllFilesInArtifactoryFolderAsync(casesDirectoryPath, httpClient)
            .GetAwaiter()
            .GetResult()
            .OrderBy(f => f)
            .Select(casePath => BuildContext(executionId, casePath))
            .ToList();
    }

    private List<InternalContext> GetContextsWithFileSystemCases(string? executionId, string casesDirectoryPath)
    {
        var resolvedCasesDirectoryPath = ResolveLocalPath(casesDirectoryPath);
        if (!_fileSystem.Directory.Exists(resolvedCasesDirectoryPath))
            throw new DirectoryNotFoundException(RunnerDiagnosticMessageFormatter.Format(
                "Cases root directory was not found.",
                [
                    $"Configured cases root directory: {casesDirectoryPath}",
                    $"Resolved local path: {resolvedCasesDirectoryPath}"
                ],
                null,
                null,
                [
                    "Set a valid cases directory path or remove the cases-root setting and retry."
                ]));

        return _fileSystem.Directory.GetFiles(resolvedCasesDirectoryPath,
                "*", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .Select(caseFile =>
                BuildContext(executionId, Path.GetRelativePath(Environment.CurrentDirectory, caseFile)))
            .ToList();
    }

    private IEnumerable<InternalContext> GetLoadedContexts()
    {
        var contexts = new List<InternalContext>();
        if (Options.CasesRootDirectory == null)
        {
            contexts.Add(BuildContext(ExecutionId));
        }
        else
        {
            // Load contexts in different ways depending on if the Cases are http based or file based
            contexts = PathUtils.IsPathHttpUrl(Options.CasesRootDirectory)
                ? GetContextsWithJfrogArtifactoryCases(ExecutionId, Options.CasesRootDirectory)
                : GetContextsWithFileSystemCases(ExecutionId, Options.CasesRootDirectory);
            contexts = FilterIgnoredCases(contexts).ToList();

            // If casesNamesToRun is 0 all cases would run.
            if (Options.CasesNamesToRun.Count <= 0) return contexts;

            var notFoundGivenCases =
                Options.CasesNamesToRun.Except(contexts.Select(context => context.CaseName!)).ToList();
            if (notFoundGivenCases.Count > 0)
                throw new InvalidOperationException(RunnerDiagnosticMessageFormatter.Format(
                    "The test-cases-to-run filter contains case names that were not discovered.",
                    [
                        $"Cases root directory: {Options.CasesRootDirectory}",
                        $"Requested case names not found: {RunnerDiagnosticMessageFormatter.SummarizeValues(notFoundGivenCases)}",
                        $"Discovered case names: {RunnerDiagnosticMessageFormatter.SummarizeValues(contexts.Select(context => context.CaseName))}"
                    ],
                    null,
                    null,
                    [
                        "Update the test-cases-to-run values or the cases directory contents and retry."
                    ]));

            contexts = contexts.Where(context => Options.CasesNamesToRun.Contains(context.CaseName!)).ToList();
        }

        return contexts;
    }

    private IEnumerable<InternalContext> FilterIgnoredCases(IEnumerable<InternalContext> contexts)
    {
        var ignoredCaseNames = new HashSet<string>(Options.CasesNamesToIgnore ?? [], StringComparer.Ordinal);
        var ignoredCasePatterns = (Options.CasesNamePatternsToIgnore ?? [])
            .Select(pattern => new Regex(pattern, RegexOptions.Compiled))
            .ToArray();

        return contexts.Where(context =>
        {
            if (context.CaseName == null)
            {
                return true;
            }

            if (ignoredCaseNames.Contains(context.CaseName))
            {
                return false;
            }

            return ignoredCasePatterns.All(pattern => !pattern.IsMatch(context.CaseName));
        });
    }

    private ExecutionBuilder LoadContextToExecutionBuilder(InternalContext context)
    {
        var runBuilder = new ExecutionBuilder(context, Options.GetExecutionType(), Options.SessionNamesToRun,
            Options.SessionCategoriesToRun, Options.AssertionNamesToRun, Options.AssertionCategoriesToRun);

        foreach (var configurator in _executionBuilderConfigurators.Value)
        {
            Logger.LogDebug(
                "Applying runner execution configurator {ConfiguratorType}",
                configurator.GetType().FullName);
            configurator.Configure(runBuilder);
        }

        return runBuilder;
    }

    private IEnumerable<ExecutionBuilder> GetLoadedExecutionBuilders()
    {
        return GetLoadedContexts().Select(LoadContextToExecutionBuilder);
    }

    /// <summary>
    ///     Creates a runner of type TRunner
    /// </summary>
    public override TRunner GetLoadedRunner()
    {
        var reporterMode = GetReporterModeOrThrow();
        ValidateReporterSpecificOptions(reporterMode);
        var executionBuilders = GetLoadedExecutionBuilders().ToList();
        var serveResultsFolder = Options is AssertableOptions assertableOptions && assertableOptions.AutoServeTestResults
            ? assertableOptions.GetServeResultsFolderOrDefault()
            : null;

        var runner = Bootstrap.CreateRunner<TRunner>(
            _runScope,
            executionBuilders,
            Logger,
            SerilogLogger,
            Options is AssertableOptions emptyResultsOptions && emptyResultsOptions.EmptyAllureDirectory,
            serveResultsFolder is not null);
        runner.WithServeResultsFolder(serveResultsFolder);
        runner.WithReporterMode(reporterMode);
        runner.ExitProcessOnCompletion = !Options.NoProcessExit;
        return runner;
    }

    private ReporterMode GetReporterModeOrThrow()
    {
        if (Options is not AssertableOptions assertableOptions)
            return ReporterMode.Both;

        try
        {
            return assertableOptions.GetReporterModeOrDefault();
        }
        catch (ArgumentException exception)
        {
            throw new InvalidConfigurationsException(exception.Message, exception);
        }
    }

    private void ValidateReporterSpecificOptions(ReporterMode reporterMode)
    {
        if (Options is not AssertableOptions assertableOptions || reporterMode != ReporterMode.ReportPortal)
            return;

        if (assertableOptions.AutoServeTestResults)
            throw new InvalidConfigurationsException(
                "The '--serve-results' flag is only valid when Allure reporting is enabled.");

        if (assertableOptions.EmptyAllureDirectory)
            throw new InvalidConfigurationsException(
                "The '--empty-results-directory' flag is only valid when Allure reporting is enabled.");
    }

    protected virtual IReadOnlyList<IExecutionBuilderConfigurator> DiscoverExecutionBuilderConfigurators()
    {
        return ExecutionBuilderConfiguratorLoader.Load(Logger);
    }

    private bool ShouldLoadConfigurationFile()
    {
        if (string.IsNullOrWhiteSpace(Options.ConfigurationFile))
            return false;

        if (PathUtils.IsPathHttpUrl(Options.ConfigurationFile))
            return true;

        var resolvedConfigurationFilePath = ResolveLocalPath(Options.ConfigurationFile);

        try
        {
            using var _ = _fileSystem.File.Open(resolvedConfigurationFilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            if (_executionBuilderConfigurators.Value.Count == 0)
                throw new CouldNotFindConfigurationException(
                    RunnerDiagnosticMessageFormatter.Format(
                        "Configuration file was not found and QaaS cannot continue.",
                        [
                            $"Configured path: {Options.ConfigurationFile}",
                            $"Resolved local path: {resolvedConfigurationFilePath}",
                            "Discovered code configurators: 0"
                        ],
                        null,
                        null,
                        [
                            "Provide a valid .qaas.yaml file.",
                            "Or register at least one IExecutionBuilderConfigurator so QaaS can build the execution in code."
                        ]),
                    exception);

            if (!_missingConfigurationFileWarningLogged)
            {
                Logger.LogWarning(
                    "Configuration file {ConfigurationFile} was not found at {ResolvedConfigurationFilePath}. Continuing because {ConfiguratorCount} code configurator(s) were discovered and can build the execution in code.",
                    Options.ConfigurationFile,
                    resolvedConfigurationFilePath,
                    _executionBuilderConfigurators.Value.Count);
                _missingConfigurationFileWarningLogged = true;
            }

            return false;
        }
    }

    private string ResolveLocalPath(string configuredPath)
    {
        return _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(Environment.CurrentDirectory, configuredPath));
    }
}
