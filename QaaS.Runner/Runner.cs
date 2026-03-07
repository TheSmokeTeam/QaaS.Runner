using Autofac;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions;
using QaaS.Runner.Assertions;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.WrappedExternals;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace QaaS.Runner;

/// <summary>
/// Runner object representing a single QaaS.Runner run
/// </summary>
public class Runner : IRunner, IDisposable
{
    private bool _disposed;
    private ILifetimeScope Scope { get; set; }
    internal ILogger Logger { get; set; }
    private Serilog.ILogger SerilogLogger { get; set; }
    private bool EmptyResults { get; set; }
    private bool ServeResults { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Runner" /> class
    /// </summary>
    public List<ExecutionBuilder> ExecutionBuilders { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Runner"/> class
    /// </summary>
    public Runner(ILifetimeScope scope, List<ExecutionBuilder> executionBuilders, ILogger logger,
        Serilog.ILogger serilogLogger,
        bool emptyResults = false, bool serveResults = false)
    {
        ExecutionBuilders = executionBuilders;
        Logger = logger;
        SerilogLogger = serilogLogger;
        Scope = scope;
        EmptyResults = emptyResults;
        ServeResults = serveResults;
    }

    /// <summary>
    /// Runs the test suite in the following order:
    /// 1) Calls the Setup method that setups the environment (runs all the configured steps before starting the tests,
    /// for example clearing test results folder)
    /// 2) Builds all the <see cref="Execution" />s
    /// 3) Starts all of the <see cref="Execution" />s
    /// 4) Calls the Teardown method that teardowns the environment (runs all the configured steps after finishing the
    /// tests,
    /// for example writing the test results to the results folder)
    /// </summary>
    public void Run()
    {
        List<Execution>? executions = null;
        var exitCode = 0;
        var shouldExit = false;
        Logger.LogInformation(
            "Starting runner with {ExecutionCount} execution builders. EmptyResults={EmptyResults}, ServeResults={ServeResults}",
            ExecutionBuilders.Count, EmptyResults, ServeResults);
        try
        {
            Setup();
            executions = BuildExecutions();
            exitCode = StartExecutions(executions);
            shouldExit = true;
        }
        finally
        {
            try
            {
                DisposeExecutions(executions);
            }
            finally
            {
                try
                {
                    Teardown();
                }
                finally
                {
                    Dispose();
                }
            }
        }

        Logger.LogInformation("Runner completed. ExitCode={ExitCode}", exitCode);
        if (shouldExit)
            ExitProcess(exitCode);
    }

    /// <summary>
    ///     Called before any execution starts, cleans the test results directory
    /// </summary>
    protected virtual void Setup()
    {
        Logger.LogDebug("Runner setup started");
        if (EmptyResults)
        {
            Logger.LogInformation("Cleaning results directory before execution");
            CleanResultsDirectory();
        }
        else
        {
            Logger.LogDebug("Results directory cleanup is disabled for this run");
        }

        Logger.LogDebug("Runner setup completed");
    }

    /// <summary>
    /// Called after all executions have finished. Finalizes the shared ReportPortal launch first,
    /// then disposes logging resources, and finally serves Allure results when requested.
    /// </summary>
    protected virtual void Teardown()
    {
        Logger.LogDebug("Runner teardown started");
        if (Scope.IsRegistered<ReportPortalLaunchManager>())
        {
            var reportPortalLaunchManager = Scope.Resolve<ReportPortalLaunchManager>();
            Logger.LogDebug("Finishing ReportPortal launch before teardown completes");
            reportPortalLaunchManager.FinishLaunchAsync(Logger).GetAwaiter().GetResult();
        }

        // Disposing logger to enable sending logs to elastic
        if (SerilogLogger is IDisposable disposableLogger)
        {
            Logger.LogDebug("Disposing Serilog logger instance");
            disposableLogger.Dispose();
        }

        if (ServeResults)
        {
            Logger.LogInformation("Serving test results after execution");
            ServeResultsInAllure();
        }
        else
        {
            Logger.LogDebug("Result serving is disabled for this run");
        }

        Logger.LogDebug("Runner teardown completed");
    }

    /// <summary>
    /// Cleans the allure results directory.
    /// </summary>
    protected virtual void CleanResultsDirectory()
    {
        Scope.Resolve<AllureWrapper>().CleanTestResultsDirectory();
    }

    /// <summary>
    /// Serves allure results.
    /// </summary>
    protected virtual void ServeResultsInAllure()
    {
        Scope.Resolve<AllureWrapper>().ServeTestResults();
    }

    /// <summary>
    /// Exits the process with the provided code.
    /// </summary>
    protected virtual void ExitProcess(int exitCode)
    {
        Environment.Exit(exitCode);
    }

    /// <summary>
    /// Builds the list of <see cref="Execution" />s from <see cref="ExecutionBuilders" />.
    /// During this step the runner injects shared runtime services such as the global dictionary,
    /// the configured logger, and the runner-scoped ReportPortal launch manager.
    /// </summary>
    /// <returns>List of built <see cref="Execution" />s</returns>
    protected virtual List<Execution> BuildExecutions()
    {
        Logger.LogInformation("Building {ExecutionCount} executions", ExecutionBuilders.Count);
        var globalDict = new Dictionary<string, object?>();
        var reportPortalLaunchManager = Scope.IsRegistered<ReportPortalLaunchManager>()
            ? Scope.Resolve<ReportPortalLaunchManager>()
            : null;
        var reportPortalRunDescriptor = BuildReportPortalRunDescriptor();
        // Builders share a single global dictionary so metadata and runtime values written by one
        // execution are visible to later executions in the same runner invocation.
        ExecutionBuilders.ForEach(builder => builder.WithGlobalDict(globalDict));

        // passing the same logger reference to every builder
        ExecutionBuilders.ForEach(builder => builder.WithLogger(Logger));
        if (reportPortalLaunchManager is not null)
            ExecutionBuilders.ForEach(builder => builder.WithReportPortalLaunchManager(reportPortalLaunchManager));
        if (reportPortalRunDescriptor is not null)
            ExecutionBuilders.ForEach(builder => builder.WithReportPortalRunDescriptor(reportPortalRunDescriptor));
        var executions = ExecutionBuilders.Select(builder => builder.Build()).ToList();
        Logger.LogInformation("Built {ExecutionCount} executions successfully", executions.Count);
        return executions;
    }

    /// <summary>
    /// Starts each of the <see cref="Execution" />s that are passed
    /// Each <see cref="Execution" /> starts all the actions that are configured (Storages, Sessions, Assertions etc...)
    /// </summary>
    /// <param name="executions"><see cref="Execution" />s to Start</param>
    /// <returns>Exit code of the executions results</returns>
    protected virtual int StartExecutions(List<Execution> executions)
    {
        Logger.LogInformation("Running {ExecutionCount} executions", executions.Count);
        var exitCode = executions.Select(execution => execution.Start()).Sum();
        Logger.LogInformation("Finished running executions. Aggregated exit code: {ExitCode}", exitCode);
        return exitCode;
    }

    protected virtual void DisposeExecutions(IEnumerable<Execution>? executions)
    {
        var executionList = (executions ?? Enumerable.Empty<Execution>()).ToList();
        Logger.LogDebug("Disposing {ExecutionCount} execution instances", executionList.Count);
        foreach (var execution in executionList)
            execution.Dispose();
    }

    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Logger.LogDebug("Disposing runner scope");
        Scope.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Builds the runner-scoped ReportPortal launch descriptor used by every execution builder in this invocation.
    /// The descriptor enforces one project/team per run and provides stable launch naming defaults.
    /// </summary>
    private ReportPortalRunDescriptor? BuildReportPortalRunDescriptor()
    {
        var startedAtLocal = DateTimeOffset.Now;
        var builderSettings = ExecutionBuilders
            .Select(builder => new
            {
                Builder = builder,
                Settings = ReportPortalConfig.Resolve(builder.ReportPortal, BuildSingleBuilderRunDescriptor(builder, startedAtLocal))
            })
            .Where(item => item.Settings.Enabled)
            .ToList();

        if (builderSettings.Count == 0)
        {
            Logger.LogDebug("ReportPortal is disabled for all execution builders in this runner invocation.");
            return null;
        }

        var distinctTeams = builderSettings
            .Select(item => item.Builder.MetaData?.Team?.Trim())
            .Where(team => !string.IsNullOrWhiteSpace(team))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (distinctTeams.Count > 1)
        {
            throw new InvalidOperationException(
                "A single runner invocation cannot mix multiple MetaData.Team values when ReportPortal reporting is enabled. " +
                $"Resolved teams: {string.Join(", ", distinctTeams)}");
        }

        var distinctProjects = builderSettings.Select(item => item.Settings.Project)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (distinctProjects.Count > 1)
        {
            throw new InvalidOperationException(
                "A single runner invocation cannot publish to multiple ReportPortal projects. " +
                $"Resolved projects: {string.Join(", ", distinctProjects)}");
        }

        var distinctSystems = builderSettings
            .Select(item => item.Builder.MetaData?.System?.Trim())
            .Where(system => !string.IsNullOrWhiteSpace(system))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var systemName = distinctSystems.Count switch
        {
            0 => "Unknown System",
            1 => distinctSystems[0]!,
            _ => "Mixed Systems"
        };

        var sessionNames = builderSettings
            .SelectMany(item => item.Builder.ReadSessions())
            .Select(session => session.Name)
            .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
            .Select(sessionName => sessionName!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sessionName => sessionName, StringComparer.Ordinal)
            .ToArray();
        var executionModes = builderSettings.Select(item => item.Builder.ReadExecutionType().ToString().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var executionMode = executionModes.Count == 1 ? executionModes[0] : "mixed";
        var descriptor = new ReportPortalRunDescriptor(
            distinctTeams.SingleOrDefault(),
            systemName,
            sessionNames,
            executionMode,
            startedAtLocal);

        Logger.LogDebug(
            "Built ReportPortal run descriptor for project {ProjectName}. Team={TeamName}, System={SystemName}, Sessions=[{SessionNames}], ExecutionMode={ExecutionMode}",
            distinctProjects[0], descriptor.TeamName ?? "<none>", descriptor.SystemName, string.Join(", ", descriptor.SessionNames),
            descriptor.ExecutionMode);
        return descriptor;
    }

    private static ReportPortalRunDescriptor BuildSingleBuilderRunDescriptor(ExecutionBuilder builder,
        DateTimeOffset startedAtLocal)
    {
        var sessionNames = builder.ReadSessions()
            .Select(session => session.Name)
            .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
            .Select(sessionName => sessionName!)
            .ToArray();

        return new ReportPortalRunDescriptor(
            builder.MetaData?.Team,
            builder.MetaData?.System,
            sessionNames,
            builder.ReadExecutionType().ToString().ToLowerInvariant(),
            startedAtLocal);
    }
}
