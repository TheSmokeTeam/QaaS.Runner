using Autofac;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions;
using QaaS.Framework.SDK;
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
        var reportPortalRunDescriptors = BuildReportPortalRunDescriptors();
        // Builders share a single global dictionary so metadata and runtime values written by one
        // execution are visible to later executions in the same runner invocation.
        ExecutionBuilders.ForEach(builder => builder.WithGlobalDict(globalDict));

        // passing the same logger reference to every builder
        ExecutionBuilders.ForEach(builder => builder.WithLogger(Logger));
        if (reportPortalLaunchManager is not null)
            ExecutionBuilders.ForEach(builder => builder.WithReportPortalLaunchManager(reportPortalLaunchManager));
        foreach (var runDescriptorPair in reportPortalRunDescriptors)
            runDescriptorPair.Key.WithReportPortalRunDescriptor(runDescriptorPair.Value);
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
    /// Builds grouped ReportPortal launch descriptors so every execution builder targeting the same team project and
    /// system reuses one shared launch name/description contract.
    /// </summary>
    private Dictionary<ExecutionBuilder, ReportPortalRunDescriptor> BuildReportPortalRunDescriptors()
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
            return new Dictionary<ExecutionBuilder, ReportPortalRunDescriptor>();
        }

        var descriptors = new Dictionary<ExecutionBuilder, ReportPortalRunDescriptor>();
        foreach (var builderGroup in builderSettings.GroupBy(item => new
                 {
                     Team = item.Settings.Team?.Trim().ToLowerInvariant() ?? string.Empty,
                     System = item.Settings.System.Trim().ToLowerInvariant()
                 }))
        {
            var sessionNames = builderGroup
                .SelectMany(item => item.Builder.ReadSessions())
                .Select(session => session.Name)
                .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
                .Select(sessionName => sessionName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(sessionName => sessionName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var executionModes = builderGroup
                .Select(item => item.Builder.ReadExecutionType().ToString().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var executionMode = executionModes.Count == 1 ? executionModes[0] : "mixed";
            var teamName = builderGroup
                .Select(item => item.Settings.Team)
                .FirstOrDefault(team => !string.IsNullOrWhiteSpace(team));
            var systemName = builderGroup
                .Select(item => item.Settings.System)
                .FirstOrDefault(system => !string.IsNullOrWhiteSpace(system)) ?? "Unknown System";
            var descriptor = new ReportPortalRunDescriptor(
                teamName,
                systemName,
                sessionNames,
                executionMode,
                startedAtLocal,
                BuildLaunchAttributes(builderGroup.Select(item => item.Builder)));

            foreach (var item in builderGroup)
                descriptors[item.Builder] = descriptor;

            Logger.LogDebug(
                "Built ReportPortal run descriptor for team {TeamName}, system {SystemName}, sessions [{SessionNames}], execution mode {ExecutionMode}, builder count {BuilderCount}.",
                descriptor.TeamName ?? "<none>",
                descriptor.SystemName,
                string.Join(", ", descriptor.SessionNames),
                descriptor.ExecutionMode,
                builderGroup.Count());
        }

        return descriptors;
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
            startedAtLocal,
            BuildLaunchAttributes([builder]));
    }

    private static IReadOnlyDictionary<string, string> BuildLaunchAttributes(IEnumerable<ExecutionBuilder> builders)
    {
        var builderList = builders.ToList();
        var attributes = builderList
            .SelectMany(builder => BaseReporter.ExtractMetadataAttributes(builder.MetaData ?? new MetaDataConfig()))
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key) &&
                                !string.IsNullOrWhiteSpace(attribute.Value))
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(", ", group.Select(attribute => attribute.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        attributes["executionMode"] = string.Join(", ", builderList
            .Select(builder => builder.ReadExecutionType().ToString().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal));
        attributes["builderCount"] = builderList.Count.ToString();
        attributes["sessionCount"] = builderList
            .SelectMany(builder => builder.ReadSessions())
            .Select(session => session.Name)
            .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ToString();

        var caseNames = builderList
            .Select(builder => builder.ReadCase())
            .Where(caseName => !string.IsNullOrWhiteSpace(caseName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(caseName => caseName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (caseNames.Length > 0)
            attributes["caseName"] = string.Join(", ", caseNames);

        var executionIds = builderList
            .Select(builder => builder.ReadExecutionId())
            .Where(executionId => !string.IsNullOrWhiteSpace(executionId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(executionId => executionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (executionIds.Length > 0)
            attributes["executionId"] = string.Join(", ", executionIds);

        return attributes;
    }
}
