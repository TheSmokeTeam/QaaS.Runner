using Moq;
using Moq.Protected;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Logics;

namespace QaaS.Runner.Tests.LogicsTests;

public class ReportLogicTests
{
    [TestCase(ExecutionType.Assert, true)]
    [TestCase(ExecutionType.Run, true)]
    [TestCase(ExecutionType.Template, false)]
    [TestCase(ExecutionType.Act, false)]
    public void TestShouldRun_WithExecutionType_ReturnsExpectedBoolean(ExecutionType executionType, bool expected)
    {
        // Arrange
        var mockReporters = new List<IReporter>();
        var context = new InternalContext();
        var reportLogic = new ReportLogic(mockReporters, context);

        // Act & Assert
        Assert.That(reportLogic.ShouldRun(executionType), Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(5)]
    public void TestRun_WithReportersAndAssertionResults_WritesResultsToReporters(int reportersCount)
    {
        // Arrange
        var mockReporters = new List<Mock<IReporter>>();
        var assertionResults = new List<AssertionResult>();

        for (int i = 0; i < reportersCount; i++)
        {
            var mockReporter = new Mock<IReporter>();
            mockReporter.SetupGet(r => r.Name).Returns($"Assertion{i + 1}");
            mockReporters.Add(mockReporter);
            var assertion = new Assertion
            {
                Name = $"Assertion{i + 1}",
                StatussesToReport =
                [
                    AssertionStatus.Broken,
                    AssertionStatus.Failed,
                    AssertionStatus.Passed,
                    AssertionStatus.Skipped,
                    AssertionStatus.Unknown
                ],
                AssertionName = null,
                AssertionHook = null
            };
            var assertionResult = new AssertionResult
            {
                Assertion = assertion,
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 0,
                Flaky = null,
            };
            assertionResults.Add(assertionResult);
        }

        var reportLogic = new ReportLogic(mockReporters.Select(mock => mock.Object).ToList(),
            Globals.GetContextWithMetadata());
        var executionData = new ExecutionData();

        foreach (var result in assertionResults)
        {
            executionData.AssertionResults.Add(result);
        }

        // Act
        var resultedExecutionData = reportLogic.Run(executionData);

        // Assert
        Assert.That(resultedExecutionData, Is.Not.Null);
        Assert.That(resultedExecutionData, Is.SameAs(executionData));

        for (int i = 0; i < reportersCount; i++)
        {
            var mockReporter = mockReporters[i];
            var mockAssertionResult = assertionResults[i];
            mockReporter.Verify(r => r.WriteTestResults(mockAssertionResult), Times.Once());
        }
    }

    [Test]
    public void TestRun_WithMismatchedReporterAndAssertionResult_ThrowsArgumentException()
    {
        // Arrange
        var mockReporter = new Mock<IReporter>();
        mockReporter.Setup(r => r.Name).Returns("NonExistentReporter");

        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "DifferentReporter",
                AssertionName = null,
                AssertionHook = null,
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };

        var mockReporters = new List<IReporter> { mockReporter.Object };
        var reportLogic = new ReportLogic(mockReporters, Globals.GetContextWithMetadata());
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(assertionResult);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => reportLogic.Run(executionData));
    }
}