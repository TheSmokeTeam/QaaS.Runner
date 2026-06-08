using System;
using System.Collections.Generic;
using System.Linq;

namespace QaaS.Runner.Cases;

/// <summary>
/// Provides extension methods for extending the <see cref="Runner"/> with case-expansion support.
/// </summary>
public static class RunnerCaseExpansionExtensions
{
    /// <summary>
    /// Extracts the first execution builder (usually loaded from YAML via Bootstrap.New) to serve as a base.
    /// It removes it from the Runner so it can be safely branched into multiple cases.
    /// </summary>
    /// <param name="runner">The runner instance.</param>
    /// <param name="setupBase">Optional configuration block to run over the extracted base builder.</param>
    /// <returns>The extracted execution builder, or a new instance if none was present.</returns>
    public static ExecutionBuilder ExtractBaseBuilder(this Runner runner, Action<ExecutionBuilder>? setupBase = null)
    {
        runner.ExecutionBuilders ??= new List<ExecutionBuilder>();

        ExecutionBuilder baseBuilder;
        if (runner.ExecutionBuilders.Count > 0)
        {
            baseBuilder = runner.ExecutionBuilders[0];
            runner.ExecutionBuilders.RemoveAt(0);
        }
        else
        {
            baseBuilder = new ExecutionBuilder();
        }

        setupBase?.Invoke(baseBuilder);
        return baseBuilder;
    }

    /// <summary>
    /// Clones the provided base builder for each test case, applies the case's configuration, 
    /// and attaches the resulting builders back to the Runner.
    /// </summary>
    /// <param name="runner">The runner instance.</param>
    /// <param name="baseBuilder">The base execution builder to clone from.</param>
    /// <param name="cases">The cases to expand.</param>
    /// <returns>The runner instance for fluent chaining.</returns>
    public static Runner AddTestCases(
        this Runner runner,
        ExecutionBuilder baseBuilder,
        params ITestCase[] cases)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(baseBuilder);

        runner.ExecutionBuilders ??= new List<ExecutionBuilder>();
        runner.ExecutionBuilders.Clear();

        if (cases == null || cases.Length == 0)
        {
            // If there are no cases, we assume the base builder itself is the only case we want to run.
            runner.ExecutionBuilders.Add(baseBuilder);
            return runner;
        }

        foreach (var testCase in cases)
        {
            ArgumentNullException.ThrowIfNull(testCase);
            var caseBuilder = baseBuilder.Clone();
            caseBuilder.SetCase(testCase.Name);
            testCase.SetupExecutionBuilder(caseBuilder);
            runner.ExecutionBuilders.Add(caseBuilder);
        }

        return runner;
    }
}
