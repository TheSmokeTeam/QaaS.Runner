using Allure.Commons;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Assertions;
using QaaS.Runner.Assertions.AssertionObjects;

namespace QaaS.Runner.Logics;

/// <summary>
/// Routes assertion results to configured reporters.
/// </summary>
public class ReportLogic(IList<IReporter> reporters, InternalContext context) : ILogic
{
    /// <summary>
    /// Reports matching <see cref="AssertionResult" /> entries to each configured <see cref="IReporter" />.
    /// </summary>
    /// <param name="executionData">The mutable execution context containing assertion results.</param>
    /// <returns>The same <paramref name="executionData" /> instance after reporting completes.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when a configured reporter does not have a matching assertion result.
    /// </exception>
    public ExecutionData Run(ExecutionData executionData)
    {
        context.Logger.LogInformation("Running {Reports} Logic", "Reports");
        context.Logger.LogInformation("Started writing assertion results to {ResultsDirectory}",
            AllureLifecycle.Instance.ResultsDirectory);

        foreach (var reporter in reporters)
        {
            var matchingResult = executionData.AssertionResults.FirstOrDefault(result =>
                ((AssertionResult)result).Assertion.Name.Equals(reporter.Name));
            
            if (matchingResult is AssertionResult assertionResult)
            {
                // Report only if assertion result status matches one of the statuses to report
                if (assertionResult.Assertion.StatussesToReport.Contains(assertionResult.AssertionStatus))
                    reporter.WriteTestResults(assertionResult);
            }
            else
                throw new ArgumentException("Could not find any matching assertion result to reporter");
        }

        context.Logger.LogInformation("Finished writing assertion results to {ResultsDirectory}",
            AllureLifecycle.Instance.ResultsDirectory);

        return executionData;
    }
}
