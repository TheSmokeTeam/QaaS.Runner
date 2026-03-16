using Autofac;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions;
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
    private bool DisposeSerilogLogger { get; set; } = true;

    /// <summary>
    /// Controls whether <see cref="Run" /> terminates the current process after the runner finishes successfully.
    /// </summary>
    public bool ExitProcessOnCompletion { get; set; } = true;

    /// <summary>
    /// The exit code produced by the most recent successful runner execution.
    /// </summary>
    public int? LastExitCode { get; private set; }

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
        var exitCode = RunAndGetExitCode();
        HandleCompletion(exitCode);
    }

    /// <summary>
    /// Runs the runner lifecycle and returns the resulting exit code without terminating the current process.
    /// </summary>
    /// <returns>The aggregated exit code from the runner's executions.</returns>
    public int RunAndGetExitCode()
    {
        List<Execution>? executions = null;
        var exitCode = 0;
        LastExitCode = null;
        Logger.LogInformation(
            "Starting runner with {ExecutionCount} execution builders. EmptyResults={EmptyResults}, ServeResults={ServeResults}, ExitProcessOnCompletion={ExitProcessOnCompletion}",
            ExecutionBuilders.Count, EmptyResults, ServeResults, ExitProcessOnCompletion);
        try
        {
            Setup();
            executions = BuildExecutions();
            exitCode = StartExecutions(executions);
            LastExitCode = exitCode;
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
        return exitCode;
    }

    private void HandleCompletion(int exitCode)
    {
        if (ExitProcessOnCompletion)
        {
            ExitProcess(exitCode);
            return;
        }

        SetProcessExitCode(exitCode);
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
    /// Called after all executions have finished, writes the tests results to the results directory
    /// </summary>
    protected virtual void Teardown()
    {
        Logger.LogDebug("Runner teardown started");
        // Disposing logger to enable sending logs to elastic
        if (DisposeSerilogLogger && SerilogLogger is IDisposable disposableLogger)
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
    /// Sets the current process exit code without terminating the process.
    /// </summary>
    protected virtual void SetProcessExitCode(int exitCode)
    {
        Environment.ExitCode = exitCode;
    }

    /// <summary>
    /// Builds the list of <see cref="Execution" />s from <see cref="ExecutionBuilders" />
    /// </summary>
    /// <returns>List of built <see cref="Execution" />s</returns>
    protected virtual List<Execution> BuildExecutions()
    {
        Logger.LogInformation("Building {ExecutionCount} executions", ExecutionBuilders.Count);
        var globalDict = new Dictionary<string, object?>();
        // Builders share a single global dictionary so metadata and runtime values written by one
        // execution are visible to later executions in the same runner invocation.
        ExecutionBuilders.ForEach(builder => builder.WithGlobalDict(globalDict));

        // passing the same logger reference to every builder
        ExecutionBuilders.ForEach(builder => builder.WithLogger(Logger));
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

    internal Runner WithSerilogLoggerDisposal(bool disposeSerilogLogger)
    {
        DisposeSerilogLogger = disposeSerilogLogger;
        return this;
    }
}
