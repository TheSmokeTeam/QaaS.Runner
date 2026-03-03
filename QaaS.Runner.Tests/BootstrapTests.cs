namespace QaaS.Runner.Tests;

using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class BootstrapTests
{
    [TestCaseSource(nameof(GetRunnerTestCases))]
    public void TestGetRunner_CallsCorrectLoaderAndReturnsRunner(
        IEnumerable<string> args,
        string expectedRunnerType)
    {
        Runner result;

        // Act
        result = Bootstrap.New(args);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<Runner>());
    }

    private static IEnumerable<TestCaseData> GetRunnerTestCases()
    {
        yield return new TestCaseData(
            new string[] { "run", "TestData/test.qaas.yaml", "-w", "TestData/override.yaml", "--send-logs", "true" },
            "Runner"
        ).SetName("WithRunOptions");

        yield return new TestCaseData(
            new string[] { "act", "TestData/test.qaas.yaml", "-w", "TestData/override.yaml", "--send-logs", "false" },
            "Runner"
        ).SetName("WithActOptions");

        yield return new TestCaseData(
            new string[] { "assert", "TestData/test.qaas.yaml", "-w", "TestData/override.yaml", "--send-logs", "false" },
            "Runner"
        ).SetName("WithAssertOptions");

        yield return new TestCaseData(
            new string[] { "template", "TestData/test.qaas.yaml", "-w", "TestData/override.yaml", "--send-logs", "false" },
            "Runner"
        ).SetName("WithTemplateOptions");

        yield return new TestCaseData(
            new string[] { "execute", "TestData/executable.yaml", "--send-logs", "true" },
            "Runner"
        ).SetName("WithExecuteOptions");

        yield return new TestCaseData(
            new string[] { "--help" },
            "Runner"
        ).SetName("WithHelpFlag");

        yield return new TestCaseData(
            new string[] { "--version" },
            "Runner"
        ).SetName("WithVersionFlag");

        yield return new TestCaseData(
            new[] { "invalid-command" },
            "Runner"
        ).SetName("WithInvalidCommand");
    }
}
