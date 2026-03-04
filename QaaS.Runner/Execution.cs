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
///     Represents a single execution of QaaS's tests
/// </summary>
public class Execution : BaseExecution
{
    /// <summary>
    /// Execution information and context
    /// </summary>
    /// <param name="type">Execution type</param>
    /// <param name="context">Context</param>
    public Execution(ExecutionType type, Context context)
    {
        Context = context;
        Type = type;
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
        return Type switch
        {
            ExecutionType.Run => Run(),
            ExecutionType.Template => Template(),
            ExecutionType.Act => Act(),
            ExecutionType.Assert => Assert(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private int Act()
    {
        DataSourceLogic.Run(Context.ExecutionData);
        SessionLogic.Run(Context.ExecutionData);
        StorageLogic.Run(Context.ExecutionData);
        return 0;
    }

    private int Assert()
    {
        DataSourceLogic.Run(Context.ExecutionData);
        StorageLogic.Run(Context.ExecutionData);
        AssertionLogic.Run(Context.ExecutionData);
        ReportLogic.Run(Context.ExecutionData);
        return Context.ExecutionData.AssertionResults.All(result =>
            ((AssertionResult)result).AssertionStatus == AssertionStatus.Passed)
            ? 0
            : 1;
    }

    private int Run()
    {
        DataSourceLogic.Run(Context.ExecutionData);
        SessionLogic.Run(Context.ExecutionData);
        AssertionLogic.Run(Context.ExecutionData);
        ReportLogic.Run(Context.ExecutionData);
        return Context.ExecutionData.AssertionResults.All(result =>
            ((AssertionResult)result).AssertionStatus == AssertionStatus.Passed)
            ? 0
            : 1;
    }

    private int Template()
    {
        TemplateLogic.Run(Context.ExecutionData);
        return 0;
    }

    public override void Dispose()
    {
    }
}