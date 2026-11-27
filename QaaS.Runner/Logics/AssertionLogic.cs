using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Extensions;
using QaaS.Runner.Infrastructure;

namespace QaaS.Runner.Logics;

/// <summary>
/// Executes configured assertions against the current execution data and stores their results.
/// </summary>
public class AssertionLogic(IList<Assertion> assertions, InternalContext context) : ILogic
{
    internal bool IsConfigured => assertions.Count != 0;

    /// <summary>
    /// Executes all assertions in parallel and appends their results to
    /// <see cref="ExecutionData.AssertionResults" />.
    /// </summary>
    /// <param name="executionData">The mutable execution context used as assertion input and output sink.</param>
    /// <returns>The same <paramref name="executionData" /> instance after all assertions complete.</returns>
    public ExecutionData Run(ExecutionData executionData) => Run(executionData, null);

    internal ExecutionData Run(ExecutionData executionData, ExecutionUsage? attempt)
    {
        context.Logger.LogInformation("Running {LogicType} Logic", "Assertions");
        var metaData = context.GetMetaDataOrDefault();
        var sessionDataSnapshot = executionData.SessionDatas.ToImmutableList();
        var dataSourcesSnapshot = executionData.DataSources.ToImmutableList();

        var assertionResults = new ConcurrentBag<AssertionResult>();

        // A parallel failure can interrupt the batch before its results are published.
        if (attempt != null)
            attempt.AssertionsExecuted = null;
        Parallel.ForEach(assertions, assertion =>
        {
            // Log before execution
            context.Logger.LogInformationWithMetaData("Running assertion {AssertionType} {AssertionName}",
                metaData, new object?[] { assertion.AssertionName, assertion.Name });

            // Execute the assertion
            var result = assertion.Execute(sessionDataSnapshot, dataSourcesSnapshot);

            context.Logger.LogDebugWithMetaData(
                "Assertion completed. DiagnosticCode={DiagnosticCode} AssertionName={AssertionName} " +
                "AssertionType={AssertionType} AssertionStatus={AssertionStatus}",
                metaData, new object?[] { "QAAS_ASSERTION_COMPLETED", assertion.Name,
                    assertion.AssertionName, result.AssertionStatus.ToString() });

            assertionResults.Add(result);
        });

        var previousCount = executionData.AssertionResults.Count;
        foreach (var assertionResult in assertionResults)
        {
            executionData.AssertionResults.Add(assertionResult);
            if (attempt != null && assertionResult.AssertionStatus == AssertionStatus.Broken)
                attempt.HasBrokenAssertion = true;
        }
        if (attempt != null)
            attempt.AssertionsExecuted = executionData.AssertionResults.Count - previousCount;

        return executionData;
    }
}
