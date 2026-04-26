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
    public ExecutionData Run(ExecutionData executionData)
    {
        var reporterTypes = FormatReporterTypes(reporters);
        context.Logger.LogInformation("Running {Reports} Logic", "Reports");
        context.Logger.LogInformation(
            "Started writing assertion results using {ReporterCount} reporters. ReporterTypes={ReporterTypes}",
            reporters.Count, reporterTypes);

        var assertionResults = executionData.AssertionResults
            .OfType<AssertionResult>()
            .ToList();

        foreach (var reporter in reporters)
        {
            context.Logger.LogDebug(
                "Reporter type {ReporterType} matched {AssertionCount} assertion results",
                reporter.GetType().Name,
                assertionResults.Count);

            foreach (var assertionResult in assertionResults)
            {
                context.Logger.LogDebug(
                    "Routing assertion {AssertionName} with status {AssertionStatus} to reporter type {ReporterType}",
                    assertionResult.Assertion.Name, assertionResult.AssertionStatus, reporter.GetType().Name);
                if (assertionResult.Assertion.StatussesToReport.Contains(assertionResult.AssertionStatus))
                {
                    reporter.WriteTestResults(assertionResult);
                }
                else
                {
                    context.Logger.LogDebug(
                        "Skipping reporter type {ReporterType} for assertion {AssertionName} because status {AssertionStatus} is not configured for reporting",
                        reporter.GetType().Name, assertionResult.Assertion.Name, assertionResult.AssertionStatus);
                }
            }
        }

        context.Logger.LogInformation(
            "Finished writing assertion results using {ReporterCount} reporters. ReporterTypes={ReporterTypes}",
            reporters.Count, reporterTypes);

        return executionData;
    }

    private static string FormatReporterTypes(IEnumerable<IReporter> reporters)
    {
        var reporterTypes = reporters
            .Select(reporter => reporter.GetType().Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reporterType => reporterType, StringComparer.Ordinal)
            .ToArray();

        return reporterTypes.Length == 0 ? "None" : string.Join(", ", reporterTypes);
    }
}
