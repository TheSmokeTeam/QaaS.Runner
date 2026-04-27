using System.Linq;
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
    public void TestRun_WithSingleReporterType_WritesMatchingAssertionResults(int assertionCount)
    {
        var reporter = new RecordingReporter();
        var assertionResults = new List<AssertionResult>();

        for (int i = 0; i < assertionCount; i++)
        {
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

        var reportLogic = new ReportLogic([reporter], Globals.GetContextWithMetadata());
        var executionData = new ExecutionData();

        foreach (var result in assertionResults)
        {
            executionData.AssertionResults.Add(result);
        }

        var resultedExecutionData = reportLogic.Run(executionData);

        Assert.That(resultedExecutionData, Is.Not.Null);
        Assert.That(resultedExecutionData, Is.SameAs(executionData));
        Assert.That(reporter.Results, Is.EqualTo(assertionResults));
    }

    [Test]
    public void TestRun_WithReporterAndNoAssertionResults_DoesNotThrow()
    {
        var reporter = new RecordingReporter();
        var reportLogic = new ReportLogic([reporter], Globals.GetContextWithMetadata());
        var executionData = new ExecutionData();

        Assert.DoesNotThrow(() => reportLogic.Run(executionData));
        Assert.That(reporter.Results, Is.Empty);
    }

    [Test]
    public void TestRun_WithMultipleReporterTypes_WritesAllMatchingAssertionsToEachReporter()
    {
        var firstReporter = new RecordingReporter();
        var secondReporter = new AlternateRecordingReporter();
        var firstAssertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "AssertionOne",
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
        var secondAssertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "AssertionTwo",
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

        var reportLogic = new ReportLogic([firstReporter, secondReporter], Globals.GetContextWithMetadata());
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(firstAssertionResult);
        executionData.AssertionResults.Add(secondAssertionResult);

        var result = reportLogic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        Assert.Multiple(() =>
        {
            Assert.That(firstReporter.Results, Is.EqualTo(new[] { firstAssertionResult, secondAssertionResult }));
            Assert.That(secondReporter.Results, Is.EqualTo(new[] { firstAssertionResult, secondAssertionResult }));
        });
    }

    [Test]
    public void TestRun_WhenAssertionStatusIsNotConfiguredForReporting_DoesNotWriteResults()
    {
        var reporter = new RecordingReporter();
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
        var logic = new ReportLogic([reporter], Globals.GetContextWithMetadata());

        var result = logic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        Assert.That(reporter.Results, Is.Empty);
    }

    private class RecordingReporter : IReporter
    {
        public virtual ReporterKind Kind => ReporterKind.Allure;
        public string Name { get; set; } = nameof(RecordingReporter);
        public string AssertionName { get; set; } = string.Empty;
        public bool SaveSessionData { get; set; }
        public bool SaveAttachments { get; set; }
        public bool SaveLogs { get; set; }
        public bool SaveTemplate { get; set; }
        public bool DisplayTrace { get; set; }
        public long EpochTestSuiteStartTime { get; set; }
        public List<AssertionResult> Results { get; } = [];

        public void WriteTestResults(AssertionResult assertionResult)
        {
            Results.Add(assertionResult);
        }
    }

    private sealed class AlternateRecordingReporter : RecordingReporter
    {
        public override ReporterKind Kind => ReporterKind.ReportPortal;
    }
}
