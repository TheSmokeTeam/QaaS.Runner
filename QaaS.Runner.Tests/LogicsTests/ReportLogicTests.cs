using System.Linq;
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
    [TestCase(1)]
    [TestCase(5)]
    public void TestRun_WithReportersAndAssertionResults_WritesResultsToReporters(int reportersCount)
    {
        var mockReporters = new List<Mock<IReporter>>();
        var assertionResults = new List<AssertionResult>();

        for (int i = 0; i < reportersCount; i++)
        {
            var mockReporter = new Mock<IReporter>();
            mockReporter.SetupGet(r => r.Name).Returns($"Reporter{i + 1}");
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

        var resultedExecutionData = reportLogic.Run(executionData);

        Assert.That(resultedExecutionData, Is.Not.Null);
        Assert.That(resultedExecutionData, Is.SameAs(executionData));

        foreach (var mockReporter in mockReporters)
        {
            foreach (var assertionResult in assertionResults)
            {
                mockReporter.Verify(r => r.WriteTestResults(assertionResult), Times.Once());
            }
        }
    }

    [Test]
    public void TestRun_WithReporterAndNoAssertionResults_DoesNotThrow()
    {
        var mockReporter = new Mock<IReporter>();
        mockReporter.SetupGet(r => r.Name).Returns("Allure");

        var mockReporters = new List<IReporter> { mockReporter.Object };
        var reportLogic = new ReportLogic(mockReporters, Globals.GetContextWithMetadata());
        var executionData = new ExecutionData();

        Assert.DoesNotThrow(() => reportLogic.Run(executionData));
        mockReporter.Verify(currentReporter => currentReporter.WriteTestResults(It.IsAny<AssertionResult>()), Times.Never);
    }

    [Test]
    public void TestRun_WithMultipleReportersTargetingSameAssertion_WritesToEachReporter()
    {
        var firstReporter = new Mock<IReporter>();
        firstReporter.SetupGet(r => r.Name).Returns("ReporterOne");

        var secondReporter = new Mock<IReporter>();
        secondReporter.SetupGet(r => r.Name).Returns("ReporterTwo");

        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "AssertionA",
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
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };

        var reportLogic = new ReportLogic([firstReporter.Object, secondReporter.Object], Globals.GetContextWithMetadata());
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(assertionResult);

        var result = reportLogic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        firstReporter.Verify(reporter => reporter.WriteTestResults(assertionResult), Times.Once);
        secondReporter.Verify(reporter => reporter.WriteTestResults(assertionResult), Times.Once);
    }

    [Test]
    public void TestRun_WhenAssertionStatusIsNotConfiguredForReporting_DoesNotWriteResults()
    {
        var reporter = new Mock<IReporter>();
        reporter.SetupGet(r => r.Name).Returns("Reporter");
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "AssertionA",
                StatussesToReport = [AssertionStatus.Failed],
                AssertionName = null,
                AssertionHook = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(assertionResult);
        var logic = new ReportLogic([reporter.Object], Globals.GetContextWithMetadata());

        var result = logic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        reporter.Verify(currentReporter => currentReporter.WriteTestResults(It.IsAny<AssertionResult>()), Times.Never);
    }
}
