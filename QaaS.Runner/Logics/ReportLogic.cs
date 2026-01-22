using Allure.Commons;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Assertions;
using QaaS.Runner.Assertions.AssertionObjects;

namespace QaaS.Runner.Logics;

/// <summary>
/// Logic class for reporters to report results
/// </summary>
public class ReportLogic(IList<IReporter> reporters, InternalContext context) : ILogic
{
    /// <inheritdoc />
    public bool ShouldRun(ExecutionType executionType)
    {
        return executionType is ExecutionType.Run or ExecutionType.Assert;
    }


    /// <summary>
    ///     Reports <see cref="AssertionResult" />s to the provided <see cref="IReporter" />s
    /// </summary>
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