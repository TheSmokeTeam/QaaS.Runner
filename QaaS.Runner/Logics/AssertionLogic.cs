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
/// Executes configured assertions against the current execution data and stores their results.
/// </summary>
public class AssertionLogic(IList<Assertion> assertions, InternalContext context) : ILogic
{
    /// <summary>
    /// Executes all assertions in parallel and appends their results to
    /// <see cref="ExecutionData.AssertionResults" />.
    /// </summary>
    /// <param name="executionData">The mutable execution context used as assertion input and output sink.</param>
    /// <returns>The same <paramref name="executionData" /> instance after all assertions complete.</returns>
    public ExecutionData Run(ExecutionData executionData)
    {
        context.Logger.LogInformation("Running {LogicType} Logic", "Assertions");
        var metaData = context.GetMetaDataOrDefault();
        var sessionDataSnapshot = executionData.SessionDatas.ToImmutableList();
        var dataSourcesSnapshot = executionData.DataSources.ToImmutableList();

        var assertionResults = new ConcurrentBag<AssertionResult>();

        Parallel.ForEach(assertions, assertion =>
        {
            // Log before execution
            context.Logger.LogInformationWithMetaData("Running assertion {AssertionType} {AssertionName}",
                metaData, new object?[] { assertion.AssertionName, assertion.Name });

            // Execute the assertion
            var result = assertion.Execute(sessionDataSnapshot, dataSourcesSnapshot);

            // Log debug with exit code after execution
            context.Logger.LogDebugWithMetaData("Assertion {AssertionName} completed with exit code: {ExitCode}",
                metaData, new object?[] { assertion.Name, result.AssertionStatus.ToString() });

            assertionResults.Add(result);
        });

        foreach (var assertionResult in assertionResults)
            executionData.AssertionResults.Add(assertionResult);

        return executionData;
    }
}
