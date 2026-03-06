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

        if (shouldExit)
            ExitProcess(exitCode);
    }

    /// <summary>
    ///     Called before any execution starts, cleans the test results directory
    /// </summary>
    protected virtual void Setup()
    {
        if (EmptyResults)
            CleanResultsDirectory();
    }

    /// <summary>
    /// Called after all executions have finished, writes the tests results to the results directory
    /// </summary>
    protected virtual void Teardown()
    {
        // Disposing logger to enable sending logs to elastic
        if (SerilogLogger is IDisposable disposableLogger)
            disposableLogger.Dispose();
        
        if (ServeResults)
            ServeResultsInAllure();
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
    /// Builds the list of <see cref="Execution" />s from <see cref="ExecutionBuilders" />
    /// </summary>
    /// <returns>List of built <see cref="Execution" />s</returns>
    protected virtual List<Execution> BuildExecutions()
    {
        Logger.LogInformation("Building {ExecutionCount} executions", ExecutionBuilders.Count);
        var globalDict = new Dictionary<string, object?>();
        ExecutionBuilders.ForEach(builder => builder.WithGlobalDict(globalDict));
        
        // passing the same logger reference to every builder
        ExecutionBuilders.ForEach(builder => builder.WithLogger(Logger));
        return ExecutionBuilders.Select(builder => builder.Build()).ToList();
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
        return executions.Select(execution => execution.Start()).Sum();
    }

    protected virtual void DisposeExecutions(IEnumerable<Execution>? executions)
    {
        foreach (var execution in executions ?? Enumerable.Empty<Execution>())
            execution.Dispose();
    }

    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Scope.Dispose();
        GC.SuppressFinalize(this);
    }
}
