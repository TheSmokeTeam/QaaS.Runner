using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Extensions;
using QaaS.Runner.Infrastructure;

namespace QaaS.Runner.Logics;

/// <summary>
/// Logic triggerer of assertion to run with Execution data
/// </summary>
public class AssertionLogic(IList<Assertion> assertions, InternalContext context) : ILogic
{
    /// <inheritdoc />
    public bool ShouldRun(ExecutionType executionType)
    {
        return executionType is ExecutionType.Assert or ExecutionType.Run;
    }

    /// <summary>
    ///     Executes all assertions in parallel
    /// </summary>
    public ExecutionData Run(ExecutionData executionData)
    {
        context.Logger.LogInformation("Running {LogicType} Logic", "Assertions");
        var metaData = context.GetMetaDataFromContext();

        var assertionResults = new ConcurrentBag<AssertionResult>();

        Parallel.ForEach(assertions, assertion =>
        {
            // Log before execution
            context.Logger.LogInformationWithMetaData("Running assertion {AssertionType} {AssertionName}",
                metaData, assertion.AssertionName, assertion.Name);

            // Execute the assertion
            var result = assertion.Execute(executionData.SessionDatas.ToImmutableList(),
                executionData.DataSources.ToImmutableList());

            // Log debug with exit code after execution
            context.Logger.LogDebugWithMetaData("Assertion {AssertionName} completed with exit code: {ExitCode}",
                metaData, assertion.Name, result.AssertionStatus.ToString());

            assertionResults.Add(result);
        });

        foreach (var assertionResult in assertionResults)
            executionData.AssertionResults.Add(assertionResult);

        return executionData;
    }
}