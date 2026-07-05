using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.Reporters;

namespace QaaS.Runner.Logics;

/// <summary>
/// Routes assertion results to configured reporters.
/// </summary>
public class ReportLogic : ILogic
{
    internal IList<IReporter> Reporters { get; }
    private readonly InternalContext _context;

    public ReportLogic(IList<IReporter> reporters, InternalContext context)
    {
        Reporters = reporters;
        _context = context;
    }

    /// <summary>
    /// Reports each status-eligible <see cref="AssertionResult" /> to every configured <see cref="IReporter" />.
    /// </summary>
    /// <param name="executionData">The mutable execution context containing assertion results.</param>
    /// <returns>The same <paramref name="executionData" /> instance after reporting completes.</returns>
    public ExecutionData Run(ExecutionData executionData)
    {
        var reporterTypes = FormatReporterTypes(Reporters);
        _context.Logger.LogInformation("Running {Reports} Logic", "Reports");
        _context.Logger.LogInformation(
            "Started writing assertion results using {ReporterCount} reporters. ReporterTypes={ReporterTypes}",
            Reporters.Count,
            reporterTypes
        );

        var assertionResults = executionData.AssertionResults.OfType<AssertionResult>().ToList();

        foreach (var reporter in Reporters)
        {
            _context.Logger.LogDebug(
                "Reporter type {ReporterType} is evaluating {AssertionCount} assertion results",
                reporter.GetType().Name,
                assertionResults.Count
            );

            foreach (var assertionResult in assertionResults)
            {
                if (
                    assertionResult.Assertion.StatusesToReport.Contains(
                        assertionResult.AssertionStatus
                    )
                )
                {
                    _context.Logger.LogDebug(
                        "Routing assertion {AssertionName} with status {AssertionStatus} to reporter type {ReporterType}",
                        assertionResult.Assertion.Name,
                        assertionResult.AssertionStatus,
                        reporter.GetType().Name
                    );
                    reporter.WriteTestResults(assertionResult);
                }
                else
                {
                    _context.Logger.LogDebug(
                        "Skipping reporter type {ReporterType} for assertion {AssertionName} because status {AssertionStatus} is not configured for reporting",
                        reporter.GetType().Name,
                        assertionResult.Assertion.Name,
                        assertionResult.AssertionStatus
                    );
                }
            }
        }

        _context.Logger.LogInformation(
            "Finished writing assertion results using {ReporterCount} reporters. ReporterTypes={ReporterTypes}",
            Reporters.Count,
            reporterTypes
        );

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
