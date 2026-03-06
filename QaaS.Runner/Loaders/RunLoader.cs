using System.IO.Abstractions;
using Autofac;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations;
using QaaS.Framework.Executions.Loaders;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Artifactory;
using QaaS.Runner.Options;
using QaaS.Runner.WrappedExternals;
using BaseOptions = QaaS.Runner.Options.BaseOptions;

namespace QaaS.Runner.Loaders;

/// <summary>
///     Builds and loads a runner of type TRunner with ExecutionBuilders based on provided options and configuration.
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

    /// <summary>
    /// Initializes new lifetime scope to load new Run with.
    /// </summary>
    public RunLoader(TOptions options, string? executionId = null) : base(options,
        executionId)
    {
        _runScope = InitializeScope();
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
        contextBuilder.SetConfigurationFile(Options.ConfigurationFile!);
        contextBuilder.SetCurrentRunningSessions(
            new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>()));
        foreach (var overwriteFile in Options.OverwriteFiles)
            contextBuilder.WithOverwriteFile(overwriteFile);
        contextBuilder.SetCase(relativeCaseFilePath);
        foreach (var overwriteArgument in Options.OverwriteArguments)
            contextBuilder.WithOverwriteArgument(overwriteArgument);
        foreach (var referenceConfig in Options.GetParsedPushReferences())
            contextBuilder.WithReferenceResolution(referenceConfig);
        if (Options.ResolveCasesLast) contextBuilder.ResolveCaseLast();
        if (!Options.DontResolveWithEnvironmentVariables) contextBuilder.WithEnvironmentVariableResolution();
        return contextBuilder.BuildInternal();
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
        return _fileSystem.Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, casesDirectoryPath),
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

            // If casesNamesToRun is 0 all cases would run.
            if (Options.CasesNamesToRun.Count <= 0) return contexts;

            var notFoundGivenCases =
                Options.CasesNamesToRun.Except(contexts.Select(context => context.CaseName!)).ToList();
            if (notFoundGivenCases.Count > 0)
                throw new InvalidOperationException(
                    "Found none existing cases names given by test-cases-to-run flag: " +
                    string.Join(",", notFoundGivenCases) + ".\nAll existing cases names: " +
                    string.Join(", ", contexts.Select(context => context.CaseName!)));

            contexts = contexts.Where(context => Options.CasesNamesToRun.Contains(context.CaseName!)).ToList();
        }

        return contexts;
    }

    private ILifetimeScope InitializeScope()
    {
        return new ContainerBuilder().Build().BeginLifetimeScope(scope =>
        {
            scope.RegisterInstance(new AllureWrapper()).SingleInstance();
            scope.RegisterInstance(new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>()))
                .As<IInternalRunningSessions>();
            // Must not be single instance so it builds a new configuration builder for every context
            scope.RegisterType<ConfigurationBuilder>().As<IConfigurationBuilder>();
        });
    }

    private ExecutionBuilder LoadContextToExecutionBuilder(InternalContext context)
    {
        var runBuilder = new ExecutionBuilder(context, Options.GetExecutionType(), Options.SessionNamesToRun,
            Options.SessionCategoriesToRun, Options.AssertionNamesToRun, Options.AssertionCategoriesToRun);
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
        var executionBuilders = GetLoadedExecutionBuilders().ToList();

        // Use Activator to create an instance of the configured implementation of TRunner
        return (TRunner)Activator.CreateInstance(
            typeof(TRunner), _runScope, executionBuilders, Logger, SerilogLogger,
            Options is AssertableOptions assertableOptions && assertableOptions.EmptyAllureDirectory,
            Options is AssertableOptions assertableOptions2 && assertableOptions2.AutoServeTestResults
        )!;
    }
}
