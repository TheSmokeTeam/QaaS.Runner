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
/// The class uses a small strategy/pipeline model instead of embedding execution-type branching
/// directly inside <see cref="Start" />. Each supported <see cref="ExecutionType" /> maps to an
/// <see cref="ExecutionPlan" /> that declares:
/// - which logic steps run, in order
/// - how the final exit code is computed
///
/// This keeps orchestration open to extension while preserving the exact runtime behavior expected
/// by the current tests and callers.
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
                execution => execution.ResolveAssertionExitCode()),
            [ExecutionType.Act] = new(
                [
                    execution => execution.DataSourceLogic,
                    execution => execution.SessionLogic,
                    execution => execution.StorageLogic
                ],
                _ => 0),
            [ExecutionType.Assert] = new(
                [
                    execution => execution.DataSourceLogic,
                    execution => execution.StorageLogic,
                    execution => execution.AssertionLogic,
                    execution => execution.ReportLogic
                ],
                execution => execution.ResolveAssertionExitCode()),
            [ExecutionType.Template] = new(
                [
                    execution => execution.TemplateLogic
                ],
                _ => 0)
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

    /// <inheritdoc />
    public override int Start()
    {
        Context.Logger.LogInformation(
            "Running {ExecutionType} execution with executionId {ExecutionId} and case name {CaseName}", Type,
            Context.ExecutionId, Context.CaseName);
        return ResolvePlan(Type).Execute(this);
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

    private int ResolveAssertionExitCode()
    {
        return Context.ExecutionData.AssertionResults.All(result =>
            ((AssertionResult)result).AssertionStatus == AssertionStatus.Passed)
            ? 0
            : 1;
    }

    /// <summary>
    /// Encapsulates the ordered logic pipeline and exit-code policy for one execution type.
    /// </summary>
    /// <param name="logicSelectors">
    /// Selectors that resolve the concrete logic instances from an <see cref="Execution" /> and run
    /// them in declaration order.
    /// </param>
    /// <param name="exitCodeResolver">
    /// Strategy used to compute the final process exit code after the pipeline completes.
    /// </param>
    private sealed class ExecutionPlan(
        IReadOnlyList<Func<Execution, QaaS.Framework.Executions.Logics.ILogic>> logicSelectors,
        Func<Execution, int> exitCodeResolver)
    {
        public int Execute(Execution execution)
        {
            foreach (var logicSelector in logicSelectors)
                logicSelector(execution).Run(execution.Context.ExecutionData);

            return exitCodeResolver(execution);
        }
    }
}
