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
            mockReporter.SetupGet(r => r.AssertionName).Returns($"Assertion{i + 1}");
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
        mockReporter.SetupGet(r => r.Name).Returns("Allure");
        mockReporter.SetupGet(r => r.AssertionName).Returns("NonExistentAssertion");

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

    [Test]
    public void TestRun_WithMultipleReportersTargetingSameAssertion_WritesToEachReporter()
    {
        var firstReporter = new Mock<IReporter>();
        firstReporter.SetupGet(r => r.Name).Returns("ReporterOne");
        firstReporter.SetupGet(r => r.AssertionName).Returns("AssertionA");

        var secondReporter = new Mock<IReporter>();
        secondReporter.SetupGet(r => r.Name).Returns("ReporterTwo");
        secondReporter.SetupGet(r => r.AssertionName).Returns("AssertionA");

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
}
