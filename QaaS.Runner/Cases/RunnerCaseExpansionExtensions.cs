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
    /// Extracts a single execution builder (by position) to serve as a base for case expansion.
    /// Only the selected builder is removed from the Runner; any other builders created by
    /// <c>Bootstrap.New</c> are left in place so they can keep running alongside the generated cases.
    /// </summary>
    /// <param name="runner">The runner instance.</param>
    /// <param name="index">
    /// The index of the execution builder to use as the base. Defaults to the first builder.
    /// </param>
    /// <param name="setupBase">Optional configuration block to run over the extracted base builder.</param>
    /// <returns>The extracted execution builder, or a new instance if the list was empty.</returns>
    public static ExecutionBuilder ExtractBaseBuilder(
        this Runner runner,
        int index = 0,
        Action<ExecutionBuilder>? setupBase = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        runner.ExecutionBuilders ??= new List<ExecutionBuilder>();

        ExecutionBuilder baseBuilder;
        if (runner.ExecutionBuilders.Count == 0)
        {
            baseBuilder = new ExecutionBuilder();
        }
        else
        {
            if (index < 0 || index >= runner.ExecutionBuilders.Count)
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    $"The runner has {runner.ExecutionBuilders.Count} execution builder(s); cannot extract index {index}.");

            baseBuilder = runner.ExecutionBuilders[index];
            runner.ExecutionBuilders.RemoveAt(index);
        }

        setupBase?.Invoke(baseBuilder);
        return baseBuilder;
    }

    /// <summary>
    /// Extracts the first execution builder to serve as a base and applies the supplied configuration block.
    /// </summary>
    /// <param name="runner">The runner instance.</param>
    /// <param name="setupBase">Configuration block to run over the extracted base builder.</param>
    /// <returns>The extracted execution builder, or a new instance if the list was empty.</returns>
    public static ExecutionBuilder ExtractBaseBuilder(this Runner runner, Action<ExecutionBuilder> setupBase) =>
        runner.ExtractBaseBuilder(0, setupBase);

    /// <summary>
    /// Clones the provided base builder for each test case, applies the case's configuration,
    /// and stacks the resulting builders onto the Runner's existing execution builders.
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

        if (cases == null || cases.Length == 0)
        {
            // If there are no cases, the base builder itself is added back onto the list to run.
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
