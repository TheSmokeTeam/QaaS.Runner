using Autofac;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Logics;
using AssertionLogic = QaaS.Runner.Logics.AssertionLogic;
using DataSourceLogic = QaaS.Runner.Logics.DataSourceLogic;
using ReportLogic = QaaS.Runner.Logics.ReportLogic;
using SessionLogic = QaaS.Runner.Logics.SessionLogic;
using TemplateLogic = QaaS.Runner.Logics.TemplateLogic;

namespace QaaS.Runner;

/// <summary>
/// Represents a single QaaS execution and orchestrates the logic pipeline associated with its
/// <see cref="ExecutionType" />.
/// </summary>
/// <remarks>
/// Each supported <see cref="ExecutionType" /> maps to an
/// <see cref="ExecutionPlan" /> that declares:
/// - which logic steps run, in order
/// - how the final exit code is computed
/// </remarks>
public class Execution : BaseExecution
{
    private static readonly IReadOnlyDictionary<ExecutionType, ExecutionPlan> ExecutionPlans =
        new Dictionary<ExecutionType, ExecutionPlan>
        {
            [ExecutionType.Run] = new(
                [
                    execution => execution.DataSourceLogic,
                    execution => execution.SessionLogic,
                    execution => execution.AssertionLogic,
                    execution => execution.ReportLogic
                ],
                checkAssertions: true
            ),
            [ExecutionType.Act] = new([
                execution => execution.DataSourceLogic,
                execution => execution.SessionLogic,
                execution => execution.StorageLogic,
            ]),
            [ExecutionType.Assert] = new(
                [
                    execution => execution.DataSourceLogic,
                    execution => execution.StorageLogic,
                    execution => execution.AssertionLogic,
                    execution => execution.ReportLogic
                ],
                checkAssertions: true
            ),
            [ExecutionType.Template] = new([execution => execution.TemplateLogic]),
        };

    private readonly ILifetimeScope? _ownedScope;

    /// <summary>
    /// Execution information and context
    /// </summary>
    /// <param name="type">Execution type</param>
    /// <param name="context">Context</param>
    public Execution(ExecutionType type, Context context) : this(type, context, null)
    {
    }

    /// <summary>
    /// Execution information and context
    /// </summary>
    /// <param name="type">Execution type</param>
    /// <param name="context">Context</param>
    /// <param name="ownedScope">Autofac scope created for this execution.</param>
    public Execution(ExecutionType type, Context context, ILifetimeScope? ownedScope = null)
    {
        Context = context;
        Type = type;
        _ownedScope = ownedScope;
    }

    internal DataSourceLogic DataSourceLogic { get; init; } = null!;
    internal StorageLogic StorageLogic { get; init; } = null!;
    internal SessionLogic SessionLogic { get; init; } = null!;
    internal AssertionLogic AssertionLogic { get; init; } = null!;
    internal ReportLogic ReportLogic { get; init; } = null!;
    internal TemplateLogic TemplateLogic { get; init; } = null!;
    internal bool IsBatch { get; init; }

    /// <inheritdoc />
    public override int Start()
    {
        Context.Logger.LogInformation(
            "Running {ExecutionType} execution with executionId {ExecutionId} and case name {CaseName}", Type,
            Context.ExecutionId, Context.CaseName);
        var plan = ResolvePlan(Type);
        var attempt = ExecutionUsage.Start(this, Context, Type);
        if (attempt == null)
            return plan.Execute(this);

        try
        {
            return plan.Execute(this, attempt);
        }
        catch (Exception exception)
        {
            attempt.Outcome = ExecutionUsage.IsCancellation(exception)
                ? "cancelled"
                : "runner_error";
            throw;
        }
        finally
        {
            attempt.Finish();
        }
    }

    public override void Dispose()
    {
        _ownedScope?.Dispose();
    }

    private static ExecutionPlan ResolvePlan(ExecutionType executionType)
    {
        return ExecutionPlans.TryGetValue(executionType, out var executionPlan)
            ? executionPlan
            : throw new ArgumentOutOfRangeException(nameof(executionType), executionType,
                "Unsupported execution type.");
    }

    private int ResolveAssertionExitCode(ExecutionUsage? attempt)
    {
        foreach (var result in Context.ExecutionData.AssertionResults)
        {
            var status = ((AssertionResult)result).AssertionStatus;
            if (status == AssertionStatus.Passed)
                continue;
            if (attempt != null)
                attempt.Outcome =
                    attempt.HasBrokenAssertion || status == AssertionStatus.Broken
                        ? "broken_assertion"
                        : "assertion_failure";
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// Encapsulates the ordered logic pipeline and exit-code policy for one execution type.
    /// </summary>
    /// <param name="logicSelectors">
    /// Selectors that resolve the concrete logic instances from an <see cref="Execution" /> and run
    /// them in declaration order.
    /// </param>
    /// <param name="checkAssertions">
    /// Whether non-passing assertion results set a nonzero process exit code.
    /// </param>
    private sealed class ExecutionPlan(
        IReadOnlyList<Func<Execution, QaaS.Framework.Executions.Logics.ILogic>> logicSelectors,
        bool checkAssertions = false
    )
    {
        public int Execute(Execution execution, ExecutionUsage? attempt = null)
        {
            foreach (var logicSelector in logicSelectors)
            {
                var logic = logicSelector(execution);
                if (attempt != null && logic is AssertionLogic assertions)
                    assertions.Run(execution.Context.ExecutionData, attempt);
                else
                    logic.Run(execution.Context.ExecutionData);
            }

            return checkAssertions ? execution.ResolveAssertionExitCode(attempt) : 0;
        }
    }
}
