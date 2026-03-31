using System.Runtime.ExceptionServices;
using Autofac;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations.CustomExceptions;
using QaaS.Framework.Executions;
using QaaS.Runner.Options;
using QaaS.Runner.WrappedExternals;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace QaaS.Runner;

/// <summary>
/// Represents one configured QaaS runner invocation and owns the full execution lifecycle.
/// </summary>
public class Runner : IRunner, IDisposable
{
    // Tests replace this delegate to verify the base ExitProcess path without terminating the test host.
    internal static Action<int> ProcessExitHandler { get; set; } = Environment.Exit;

    private bool _disposed;
    private ILifetimeScope Scope { get; set; }
    internal ILogger Logger { get; set; }
    private Serilog.ILogger SerilogLogger { get; set; }
    private bool EmptyResults { get; set; }
    private bool ServeResults { get; set; }
    private string ServeResultsFolder { get; set; } = AssertableOptions.DefaultServeResultsFolder;
    private bool DisposeSerilogLogger { get; set; } = true;
    private int? BootstrapHandledExitCode { get; set; }

    /// <summary>
    /// Controls whether <see cref="Run" /> terminates the current process after the runner finishes successfully.
    /// </summary>
    public bool ExitProcessOnCompletion { get; set; } = true;

    /// <summary>
    /// Controls whether the root <c>variables</c> configuration section is projected into the shared runner global dictionary under <c>Variables</c> while executions are built.
    /// </summary>
    public virtual bool LoadVariablesIntoGlobalDict { get; set; } = true;

    /// <summary>
    /// Gets the exit code produced by the most recent successful runner execution.
    /// </summary>
    public int? LastExitCode { get; private set; }

    /// <summary>
    /// Gets or sets the execution builders that will be materialized for this runner invocation.
    /// </summary>
    public List<ExecutionBuilder> ExecutionBuilders { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Runner" /> class.
    /// </summary>
    /// <param name="scope">The Autofac scope that owns runner-level services.</param>
    /// <param name="executionBuilders">The execution builders that should be materialized at runtime.</param>
    /// <param name="logger">The Microsoft logger used for runner lifecycle messages.</param>
    /// <param name="serilogLogger">The Serilog logger used by framework integrations.</param>
    /// <param name="emptyResults">Whether the Allure results directory should be cleaned before execution.</param>
    /// <param name="serveResults">Whether Allure results should be opened after execution completes.</param>
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
    /// Runs the configured lifecycle and applies the resulting exit code policy.
    /// </summary>
    /// <remarks>
    /// Call this when the current process should honor Runner exit behavior. If the caller needs to inspect the exit code without terminating the process, prefer RunAndGetExitCode().
    /// </remarks>
    /// <qaas-docs group="Runtime" subgroup="Runner" />
    public void Run()
    {
        var exitCode = RunAndGetExitCode();
        HandleCompletion(exitCode);
    }

    /// <summary>
    /// Runs the configured lifecycle and returns the resulting exit code to the caller.
    /// </summary>
    /// <remarks>
    /// Call this when the caller wants to control how the resulting exit code is handled instead of letting Runner apply its default process policy.
    /// </remarks>
    /// <qaas-docs group="Runtime" subgroup="Runner" />
    public int RunAndGetExitCode()
    {
        // Help/version/parse-only flows still return a Runner for API compatibility, but should stop here
        // because bootstrap already wrote the CLI output and chose the correct exit code.
        if (BootstrapHandledExitCode.HasValue)
            return CompleteBootstrapHandledRun();

        LastExitCode = null;
        LogRunStart();

        var lifecycleOutcome = CaptureLifecycleOutcome();
        CompleteRun(lifecycleOutcome);

        var exitCode = lifecycleOutcome.GetRequiredExitCode();
        LastExitCode = exitCode;
        Logger.LogInformation("Runner completed. ExitCode={ExitCode}", exitCode);
        return exitCode;
    }

    /// <summary>
    /// Called before any execution starts and prepares runner-level prerequisites such as result cleanup.
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
    /// Called after the execution lifecycle and performs runner-level teardown such as logger disposal and result serving.
    /// </summary>
    protected virtual void Teardown()
    {
        Logger.LogDebug("Runner teardown started");
        if (DisposeSerilogLogger && SerilogLogger is IDisposable disposableLogger)
        {
            Logger.LogDebug("Disposing Serilog logger instance");
            disposableLogger.Dispose();
        }

        if (ServeResults)
        {
            Logger.LogInformation("Serving test results after execution from {ServeResultsFolder}", ServeResultsFolder);
            ServeResultsInAllure();
        }
        else
        {
            Logger.LogDebug("Result serving is disabled for this run");
        }

        Logger.LogDebug("Runner teardown completed");
    }

    /// <summary>
    /// Cleans the Allure results directory through the configured wrapper service.
    /// </summary>
    protected virtual void CleanResultsDirectory()
    {
        Scope.Resolve<AllureWrapper>().CleanTestResultsDirectory();
    }

    /// <summary>
    /// Opens Allure results through the configured wrapper service.
    /// </summary>
    protected virtual void ServeResultsInAllure()
    {
        Scope.Resolve<AllureWrapper>().ServeTestResults(resultsDirectoryName: ServeResultsFolder);
    }

    /// <summary>
    /// Exits the current process with the provided code.
    /// </summary>
    /// <param name="exitCode">The process exit code to emit.</param>
    protected virtual void ExitProcess(int exitCode)
    {
        ProcessExitHandler(exitCode);
    }

    /// <summary>
    /// Sets the current process exit code without terminating the host process.
    /// </summary>
    /// <param name="exitCode">The exit code to store on the process.</param>
    protected virtual void SetProcessExitCode(int exitCode)
    {
        Environment.ExitCode = exitCode;
    }

    /// <summary>
    /// Builds the list of <see cref="Execution" /> instances from <see cref="ExecutionBuilders" />.
    /// </summary>
    /// <returns>The materialized executions for this runner invocation.</returns>
    protected virtual List<Execution> BuildExecutions()
    {
        Logger.LogInformation("Building {ExecutionCount} executions", ExecutionBuilders.Count);
        var globalDict = new Dictionary<string, object?>();

        // Builders share a single global dictionary so metadata and runtime values written by one
        // execution are visible to later executions in the same runner invocation.
        // This is mutable per-run state rather than a container-managed dependency, so pushing it through
        // Autofac would add indirection without improving lifetime management.
        ExecutionBuilders.ForEach(builder => builder.WithGlobalDict(globalDict));
        ExecutionBuilders.ForEach(builder => builder.WithVariablesLoadedIntoGlobalDict(LoadVariablesIntoGlobalDict));

        // The logger is also assigned directly because execution builders are plain mutable configuration objects,
        // not services resolved from the Autofac scope.
        ExecutionBuilders.ForEach(builder => builder.WithLogger(Logger));
        var executions = ExecutionBuilders.Select(builder => builder.Build()).ToList();
        Logger.LogInformation("Built {ExecutionCount} executions successfully", executions.Count);
        return executions;
    }

    /// <summary>
    /// Starts each materialized execution and returns the aggregated exit code.
    /// </summary>
    /// <param name="executions">The executions to run.</param>
    /// <returns>The sum of the individual execution exit codes.</returns>
    protected virtual int StartExecutions(List<Execution> executions)
    {
        Logger.LogInformation("Running {ExecutionCount} executions", executions.Count);
        var exitCode = executions.Select(execution => execution.Start()).Sum();
        Logger.LogInformation("Finished running executions. Aggregated exit code: {ExitCode}", exitCode);
        return exitCode;
    }

    /// <summary>
    /// Disposes the provided executions in deterministic enumeration order.
    /// </summary>
    /// <param name="executions">The executions to dispose.</param>
    protected virtual void DisposeExecutions(IEnumerable<Execution>? executions)
    {
        var executionList = (executions ?? Enumerable.Empty<Execution>()).ToList();
        Logger.LogDebug("Disposing {ExecutionCount} execution instances", executionList.Count);
        foreach (var execution in executionList)
            execution.Dispose();
    }

    /// <summary>
    /// Releases the resources owned by the current Runner instance.
    /// </summary>
    /// <remarks>
    /// Dispose should be called exactly once when the host is no longer needed so scopes, loggers, and other runtime resources are released deterministically.
    /// </remarks>
    /// <qaas-docs group="Runtime" subgroup="Runner" />
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
    /// Marks the runner as a bootstrap-only result so <see cref="Run" /> can return the chosen exit code without
    /// entering the execution lifecycle.
    /// </summary>
    /// <param name="exitCode">The exit code chosen by bootstrap.</param>
    /// <returns>The current runner instance for fluent configuration.</returns>
    internal Runner WithBootstrapHandledExitCode(int exitCode)
    {
        BootstrapHandledExitCode = exitCode;
        return this;
    }

    /// <summary>
    /// Controls whether the runner should dispose the Serilog logger during teardown.
    /// </summary>
    /// <param name="disposeSerilogLogger"><see langword="true" /> to dispose the logger during teardown.</param>
    /// <returns>The current runner instance for fluent configuration.</returns>
    internal Runner WithSerilogLoggerDisposal(bool disposeSerilogLogger)
    {
        DisposeSerilogLogger = disposeSerilogLogger;
        return this;
    }

    /// <summary>
    /// Controls which Allure folder should be served when result serving is enabled.
    /// </summary>
    /// <param name="serveResultsFolder">The folder to serve or open after execution.</param>
    /// <returns>The current runner instance for fluent configuration.</returns>
    internal Runner WithServeResultsFolder(string? serveResultsFolder)
    {
        if (!string.IsNullOrWhiteSpace(serveResultsFolder))
            ServeResultsFolder = serveResultsFolder.Trim();

        return this;
    }

    /// <summary>
    /// Applies the post-run completion policy to the resolved exit code.
    /// </summary>
    /// <param name="exitCode">The exit code produced by the runner lifecycle.</param>
    private void HandleCompletion(int exitCode)
    {
        if (ExitProcessOnCompletion)
        {
            Logger.LogDebug("Completing runner by terminating the current process with exit code {ExitCode}", exitCode);
            ExitProcess(exitCode);
            return;
        }

        Logger.LogDebug("Completing runner by setting the process exit code to {ExitCode}", exitCode);
        SetProcessExitCode(exitCode);
    }

    /// <summary>
    /// Logs the starting configuration for the current runner invocation.
    /// </summary>
    private void LogRunStart()
    {
        Logger.LogInformation(
            "Starting runner with {ExecutionCount} execution builders. EmptyResults={EmptyResults}, ServeResults={ServeResults}, ServeResultsFolder={ServeResultsFolder}, ExitProcessOnCompletion={ExitProcessOnCompletion}",
            ExecutionBuilders.Count, EmptyResults, ServeResults, ServeResultsFolder, ExitProcessOnCompletion);
    }

    /// <summary>
    /// Completes a bootstrap-handled run without entering the normal execution lifecycle.
    /// </summary>
    /// <returns>The precomputed bootstrap exit code.</returns>
    private int CompleteBootstrapHandledRun()
    {
        LastExitCode = BootstrapHandledExitCode!.Value;
        Logger.LogDebug(
            "Skipping runner lifecycle because bootstrap already handled the command-line request. ExitCode={ExitCode}",
            BootstrapHandledExitCode.Value);
        DisposeBootstrapOnlyResources();
        return BootstrapHandledExitCode.Value;
    }

    /// <summary>
    /// Executes the main runner lifecycle and captures a structured outcome instead of relying on tuple conventions.
    /// </summary>
    /// <returns>The structured lifecycle outcome used by cleanup and completion handling.</returns>
    private RunnerLifecycleOutcome CaptureLifecycleOutcome()
    {
        var lifecycleState = new RunnerLifecycleState();

        try
        {
            var lifecycleOutcome = ExecuteLifecycle(lifecycleState);
            Logger.LogInformation("Runner lifecycle finished successfully. ExitCode={ExitCode}",
                lifecycleOutcome.GetRequiredExitCode());
            return lifecycleOutcome;
        }
        catch (RunnerLifecyclePhaseException exception)
        {
            if (ShouldReturnFailureExitCode(exception.Failure.SourceException))
                return RunnerLifecycleOutcome.FailedWithExitCode(lifecycleState.Executions, exception.Phase,
                    exception.Failure, 1);

            return RunnerLifecycleOutcome.Failed(lifecycleState.Executions, exception.Phase, exception.Failure);
        }
    }

    /// <summary>
    /// Runs the setup, build, and execution phases in order and returns the successful lifecycle outcome.
    /// </summary>
    /// <param name="lifecycleState">Mutable state used to retain built executions across phase boundaries.</param>
    /// <returns>The successful lifecycle outcome.</returns>
    private RunnerLifecycleOutcome ExecuteLifecycle(RunnerLifecycleState lifecycleState)
    {
        ExecuteLifecyclePhase(RunnerLifecyclePhase.Setup, Setup);
        lifecycleState.Executions = ExecuteLifecyclePhase(RunnerLifecyclePhase.BuildExecutions, BuildExecutions);

        var exitCode = ExecuteLifecyclePhase(RunnerLifecyclePhase.StartExecutions,
            () => StartExecutions(lifecycleState.Executions!));
        LastExitCode = exitCode;

        return RunnerLifecycleOutcome.Succeeded(lifecycleState.Executions!, exitCode);
    }

    /// <summary>
    /// Executes one lifecycle phase, logging its start and completion, and wraps failures with the phase identity.
    /// </summary>
    /// <typeparam name="T">The value returned by the phase.</typeparam>
    /// <param name="phase">The lifecycle phase being executed.</param>
    /// <param name="phaseAction">The work performed by the phase.</param>
    /// <returns>The value produced by the phase.</returns>
    private T ExecuteLifecyclePhase<T>(RunnerLifecyclePhase phase, Func<T> phaseAction)
    {
        var phaseName = DescribeLifecyclePhase(phase);
        Logger.LogDebug("Runner phase started: {Phase}", phaseName);

        try
        {
            var result = phaseAction();
            Logger.LogDebug("Runner phase completed: {Phase}", phaseName);
            return result;
        }
        catch (Exception exception)
        {
            if (ShouldReturnFailureExitCode(exception))
            {
                Logger.LogDebug("Runner phase ended with configuration failure: {Phase}", phaseName);
                throw new RunnerLifecyclePhaseException(phase, ExceptionDispatchInfo.Capture(exception));
            }

            Logger.LogError(exception, "Runner phase failed: {Phase}", phaseName);
            throw new RunnerLifecyclePhaseException(phase, ExceptionDispatchInfo.Capture(exception));
        }
    }

    /// <summary>
    /// Executes one lifecycle phase that does not return a value.
    /// </summary>
    /// <param name="phase">The lifecycle phase being executed.</param>
    /// <param name="phaseAction">The work performed by the phase.</param>
    private void ExecuteLifecyclePhase(RunnerLifecyclePhase phase, Action phaseAction)
    {
        ExecuteLifecyclePhase<object?>(phase, () =>
        {
            phaseAction();
            return null;
        });
    }

    /// <summary>
    /// Runs cleanup and either returns normally on success or throws the appropriate lifecycle or cleanup failure.
    /// </summary>
    /// <param name="lifecycleOutcome">The lifecycle outcome produced by the main run path.</param>
    private void CompleteRun(RunnerLifecycleOutcome lifecycleOutcome)
    {
        var cleanupFailures = RunCleanupSteps(lifecycleOutcome.Executions);
        if (cleanupFailures.Count == 0)
        {
            if (lifecycleOutcome.ShouldRethrowFailure)
                lifecycleOutcome.RethrowFailure();

            return;
        }

        throw BuildCompletionException(lifecycleOutcome, cleanupFailures);
    }

    /// <summary>
    /// Runs the standard cleanup steps in order and collects any failures without aborting the remaining cleanup.
    /// </summary>
    /// <param name="executions">The executions that should be disposed as part of cleanup.</param>
    /// <returns>The cleanup failures captured during execution.</returns>
    private List<Exception> RunCleanupSteps(List<Execution>? executions)
    {
        Logger.LogDebug("Runner cleanup started");

        var cleanupFailures = new List<Exception>();
        RunCleanupStep("dispose executions", () => DisposeExecutions(executions), cleanupFailures);
        RunCleanupStep("teardown", Teardown, cleanupFailures);
        RunCleanupStep("dispose runner", Dispose, cleanupFailures);

        Logger.LogDebug("Runner cleanup completed. FailureCount={FailureCount}", cleanupFailures.Count);
        return cleanupFailures;
    }

    /// <summary>
    /// Executes one cleanup step and records any failure instead of aborting the remaining cleanup pipeline.
    /// </summary>
    /// <param name="stepName">The descriptive cleanup step name used in logs.</param>
    /// <param name="cleanupStep">The cleanup action to execute.</param>
    /// <param name="failures">The collection that accumulates cleanup failures.</param>
    private void RunCleanupStep(string stepName, Action cleanupStep, ICollection<Exception> failures)
    {
        Logger.LogDebug("Cleanup step started: {StepName}", stepName);

        try
        {
            cleanupStep();
            Logger.LogDebug("Cleanup step completed: {StepName}", stepName);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Cleanup step failed: {StepName}", stepName);
            failures.Add(exception);
        }
    }

    /// <summary>
    /// Builds the exception that should be thrown when cleanup failures exist.
    /// </summary>
    /// <param name="lifecycleOutcome">The lifecycle outcome produced by the main run path.</param>
    /// <param name="cleanupFailures">The cleanup failures captured during completion.</param>
    /// <returns>The exception that best represents the completion failure state.</returns>
    private static Exception BuildCompletionException(RunnerLifecycleOutcome lifecycleOutcome,
        IReadOnlyCollection<Exception> cleanupFailures)
    {
        var failures = new List<Exception>();
        if (lifecycleOutcome.ShouldRethrowFailure)
            failures.Add(lifecycleOutcome.GetFailureException());

        failures.AddRange(cleanupFailures);
        return failures.Count == 1 ? failures[0] : new AggregateException(failures);
    }

    private static bool ShouldReturnFailureExitCode(Exception exception)
    {
        return exception is InvalidConfigurationsException;
    }

    /// <summary>
    /// Disposes bootstrap-only resources when the CLI request was fully handled before normal runner execution.
    /// </summary>
    private void DisposeBootstrapOnlyResources()
    {
        Dispose();
        if (DisposeSerilogLogger && SerilogLogger is IDisposable disposableLogger)
            disposableLogger.Dispose();
    }

    /// <summary>
    /// Converts a lifecycle phase enum value into a stable log-friendly label.
    /// </summary>
    /// <param name="phase">The lifecycle phase to describe.</param>
    /// <returns>The log-friendly phase name.</returns>
    private static string DescribeLifecyclePhase(RunnerLifecyclePhase phase)
    {
        return phase switch
        {
            RunnerLifecyclePhase.Setup => "setup",
            RunnerLifecyclePhase.BuildExecutions => "build executions",
            RunnerLifecyclePhase.StartExecutions => "start executions",
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown runner lifecycle phase.")
        };
    }

    /// <summary>
    /// Tracks which phase of the runner lifecycle is currently executing.
    /// </summary>
    private enum RunnerLifecyclePhase
    {
        /// <summary>
        /// The pre-run setup phase.
        /// </summary>
        Setup,

        /// <summary>
        /// The execution materialization phase.
        /// </summary>
        BuildExecutions,

        /// <summary>
        /// The execution start phase.
        /// </summary>
        StartExecutions
    }

    /// <summary>
    /// Stores mutable lifecycle state that must survive across phase boundaries and failure paths.
    /// </summary>
    private sealed class RunnerLifecycleState
    {
        /// <summary>
        /// Gets or sets the executions built so far in the current lifecycle.
        /// </summary>
        public List<Execution>? Executions { get; set; }
    }

    /// <summary>
    /// Represents the outcome of the main runner lifecycle before cleanup is applied.
    /// </summary>
    private sealed class RunnerLifecycleOutcome
    {
        private RunnerLifecycleOutcome(List<Execution>? executions, int? exitCode, RunnerLifecyclePhase? failedPhase,
            ExceptionDispatchInfo? failure)
        {
            Executions = executions;
            ExitCode = exitCode;
            FailedPhase = failedPhase;
            Failure = failure;
        }

        /// <summary>
        /// Gets the executions that were successfully built before the lifecycle completed or failed.
        /// </summary>
        public List<Execution>? Executions { get; }

        /// <summary>
        /// Gets the aggregated exit code for a successful lifecycle.
        /// </summary>
        public int? ExitCode { get; }

        /// <summary>
        /// Gets the phase that failed when the lifecycle did not complete successfully.
        /// </summary>
        public RunnerLifecyclePhase? FailedPhase { get; }

        /// <summary>
        /// Gets the captured lifecycle failure, if one occurred.
        /// </summary>
        public ExceptionDispatchInfo? Failure { get; }

        /// <summary>
        /// Gets a value indicating whether the lifecycle captured a failure.
        /// </summary>
        public bool HasFailure => Failure != null;

        /// <summary>
        /// Gets a value indicating whether the captured failure should be rethrown after cleanup.
        /// </summary>
        public bool ShouldRethrowFailure => Failure != null && !ExitCode.HasValue;

        /// <summary>
        /// Creates a successful lifecycle outcome.
        /// </summary>
        /// <param name="executions">The executions built during the lifecycle.</param>
        /// <param name="exitCode">The aggregated lifecycle exit code.</param>
        /// <returns>The successful lifecycle outcome.</returns>
        public static RunnerLifecycleOutcome Succeeded(List<Execution> executions, int exitCode)
        {
            return new RunnerLifecycleOutcome(executions, exitCode, null, null);
        }

        /// <summary>
        /// Creates a failed lifecycle outcome.
        /// </summary>
        /// <param name="executions">The executions built before the failure occurred, if any.</param>
        /// <param name="failedPhase">The phase that failed.</param>
        /// <param name="failure">The captured lifecycle failure.</param>
        /// <returns>The failed lifecycle outcome.</returns>
        public static RunnerLifecycleOutcome Failed(List<Execution>? executions, RunnerLifecyclePhase failedPhase,
            ExceptionDispatchInfo failure)
        {
            return new RunnerLifecycleOutcome(executions, null, failedPhase, failure);
        }

        /// <summary>
        /// Creates a lifecycle outcome that should complete with a failure exit code instead of rethrowing.
        /// </summary>
        /// <param name="executions">The executions built before the failure occurred, if any.</param>
        /// <param name="failedPhase">The phase that failed.</param>
        /// <param name="failure">The captured lifecycle failure.</param>
        /// <param name="exitCode">The resolved failure exit code that should be returned to the caller.</param>
        /// <returns>The failed lifecycle outcome with a resolved exit code.</returns>
        public static RunnerLifecycleOutcome FailedWithExitCode(List<Execution>? executions,
            RunnerLifecyclePhase failedPhase, ExceptionDispatchInfo failure, int exitCode)
        {
            return new RunnerLifecycleOutcome(executions, exitCode, failedPhase, failure);
        }

        /// <summary>
        /// Returns the successful lifecycle exit code or throws if the lifecycle did not complete successfully.
        /// </summary>
        /// <returns>The successful lifecycle exit code.</returns>
        public int GetRequiredExitCode()
        {
            return ExitCode ?? throw new InvalidOperationException(
                "The runner lifecycle did not complete successfully, so no exit code is available.");
        }

        /// <summary>
        /// Returns the captured lifecycle exception or throws if the lifecycle completed successfully.
        /// </summary>
        /// <returns>The original lifecycle exception.</returns>
        public Exception GetFailureException()
        {
            return Failure?.SourceException ?? throw new InvalidOperationException(
                "The runner lifecycle completed successfully, so no failure exception is available.");
        }

        /// <summary>
        /// Rethrows the captured lifecycle failure while preserving its original stack trace.
        /// </summary>
        public void RethrowFailure()
        {
            if (Failure == null)
                throw new InvalidOperationException(
                    "The runner lifecycle completed successfully, so there is no failure to rethrow.");

            Failure.Throw();
        }
    }

    /// <summary>
    /// Wraps a lifecycle failure with the phase that produced it.
    /// </summary>
    private sealed class RunnerLifecyclePhaseException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RunnerLifecyclePhaseException" /> class.
        /// </summary>
        /// <param name="phase">The lifecycle phase that failed.</param>
        /// <param name="failure">The captured exception from the failed phase.</param>
        public RunnerLifecyclePhaseException(RunnerLifecyclePhase phase, ExceptionDispatchInfo failure)
            : base($"Runner lifecycle phase '{phase}' failed.", failure.SourceException)
        {
            Phase = phase;
            Failure = failure;
        }

        /// <summary>
        /// Gets the lifecycle phase that failed.
        /// </summary>
        public RunnerLifecyclePhase Phase { get; }

        /// <summary>
        /// Gets the captured failure from the failed lifecycle phase.
        /// </summary>
        public ExceptionDispatchInfo Failure { get; }
    }
}
