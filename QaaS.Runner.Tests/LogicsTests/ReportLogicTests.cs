using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Logics;

namespace QaaS.Runner.Tests.LogicsTests;

public class ReportLogicTests
{
    private static readonly DateTime TestSuiteStartTimeUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void TestRun_WithNoReporters_DoesNotStartLifecycleReporters()
    {
        var reportLogic = CreateReportLogic([]);
        var executionData = new ExecutionData();

        var result = reportLogic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
    }

    [TestCase(1)]
    [TestCase(5)]
    public void TestRun_WithSingleReporterTarget_WritesMatchingAssertionResults(int assertionCount)
    {
        var reporter = new RecordingReporter();
        var assertionResults = new List<AssertionResult>();

        for (int i = 0; i < assertionCount; i++)
            assertionResults.Add(CreateAssertionResult($"Assertion{i + 1}", ReporterTarget.Allure));

        var reportLogic = CreateReportLogic([reporter]);
        var executionData = new ExecutionData();

        foreach (var result in assertionResults)
            executionData.AssertionResults.Add(result);

        var resultedExecutionData = reportLogic.Run(executionData);

        Assert.That(resultedExecutionData, Is.SameAs(executionData));
        Assert.That(reporter.Results, Is.EqualTo(assertionResults));
    }

    [Test]
    public void TestRun_WithReporterAndNoAssertionResults_DoesNotThrow()
    {
        var reporter = new RecordingReporter();
        var reportLogic = CreateReportLogic([reporter]);
        var executionData = new ExecutionData();

        Assert.DoesNotThrow(() => reportLogic.Run(executionData));
        Assert.That(reporter.Results, Is.Empty);
    }

    [Test]
    public void TestRun_WithDifferentReporterTargets_WritesOnlyMatchingAssertions()
    {
        var events = new List<string>();
        var firstReporter = new RecordingReporter(events) { Target = ReporterTarget.Allure };
        var secondReporter = new RecordingLifecycleReporter(events) { Target = ReporterTarget.ReportPortal };
        var firstAssertionResult = CreateAssertionResult("AssertionOne", ReporterTarget.Allure);
        var secondAssertionResult = CreateAssertionResult("AssertionTwo", ReporterTarget.ReportPortal);

        var reportLogic = CreateReportLogic([firstReporter, secondReporter]);
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(firstAssertionResult);
        executionData.AssertionResults.Add(secondAssertionResult);

        var result = reportLogic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        Assert.Multiple(() =>
        {
            Assert.That(firstReporter.Results, Is.EqualTo(new[] { firstAssertionResult }));
            Assert.That(secondReporter.Results, Is.EqualTo(new[] { secondAssertionResult }));
            Assert.That(secondReporter.StartCount, Is.EqualTo(1));
            Assert.That(secondReporter.FinishCount, Is.EqualTo(1));
            Assert.That(secondReporter.StartContext, Is.Not.Null);
            Assert.That(secondReporter.StartTimeUtc, Is.EqualTo(TestSuiteStartTimeUtc));
            Assert.That(events, Is.EqualTo(new[]
            {
                "start",
                "write:Allure:AssertionOne",
                "write:ReportPortal:AssertionTwo",
                "finish"
            }));
        });
    }

    [Test]
    public void TestRun_WithMultipleReporterTargetsOnSameAssertion_WritesToAllMatchingReporters()
    {
        var firstReporter = new RecordingReporter { Target = ReporterTarget.Allure };
        var secondReporter = new RecordingLifecycleReporter { Target = ReporterTarget.ReportPortal };
        var assertionResult = CreateAssertionResult("AssertionOne", ReporterTarget.Allure, ReporterTarget.ReportPortal);

        var reportLogic = CreateReportLogic([firstReporter, secondReporter]);
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(assertionResult);

        var result = reportLogic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        Assert.Multiple(() =>
        {
            Assert.That(firstReporter.Results, Is.EqualTo(new[] { assertionResult }));
            Assert.That(secondReporter.Results, Is.EqualTo(new[] { assertionResult }));
            Assert.That(secondReporter.StartCount, Is.EqualTo(1));
            Assert.That(secondReporter.FinishCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestRun_WhenAssertionStatusIsNotConfiguredForReporting_DoesNotWriteResults()
    {
        var reporter = new RecordingReporter();
        var assertionResult = CreateAssertionResult("AssertionA", ReporterTarget.Allure);
        assertionResult.Assertion.StatusesToReport = [AssertionStatus.Failed];
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(assertionResult);
        var logic = CreateReportLogic([reporter]);

        var result = logic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        Assert.That(reporter.Results, Is.Empty);
    }

    [Test]
    public void TestRun_WhenLifecycleReporterThrows_FinishesStartedLifecycleReporter()
    {
        var reporter = new ThrowingLifecycleReporter { Target = ReporterTarget.ReportPortal };
        var assertionResult = CreateAssertionResult("AssertionA", ReporterTarget.ReportPortal);
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(assertionResult);
        var logic = CreateReportLogic([reporter]);

        Assert.Throws<InvalidOperationException>(() => logic.Run(executionData));
        Assert.Multiple(() =>
        {
            Assert.That(reporter.StartCount, Is.EqualTo(1));
            Assert.That(reporter.FinishCount, Is.EqualTo(1));
        });
    }

    private static ReportLogic CreateReportLogic(IList<IReporter> reporters)
    {
        return new ReportLogic(
            reporters,
            Globals.GetContextWithMetadata(),
            TestSuiteStartTimeUtc);
    }

    private static AssertionResult CreateAssertionResult(string name, params ReporterTarget[] reporterTargets)
    {
        return new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = name,
                StatusesToReport =
                [
                    AssertionStatus.Broken,
                    AssertionStatus.Failed,
                    AssertionStatus.Passed,
                    AssertionStatus.Skipped,
                    AssertionStatus.Unknown
                ],
                ReporterTargets = reporterTargets,
                AssertionName = null,
                AssertionHook = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };
    }

    private class RecordingReporter : IReporter
    {
        private readonly IList<string>? _events;

        public RecordingReporter(IList<string>? events = null)
        {
            _events = events;
        }

        public ReporterTarget Target { get; set; } = ReporterTarget.Allure;
        public string Name { get; set; } = nameof(RecordingReporter);
        public string AssertionName { get; set; } = string.Empty;
        public bool SaveSessionData { get; set; }
        public bool SaveAttachments { get; set; }
        public bool SaveLogs { get; set; }
        public bool DisplayTrace { get; set; }
        public DateTime TestSuiteStartTimeUtc { get; set; }
        public List<AssertionResult> Results { get; } = [];

        public virtual void WriteTestResults(AssertionResult assertionResult)
        {
            _events?.Add($"write:{Target}:{assertionResult.Assertion.Name}");
            Results.Add(assertionResult);
        }
    }

    private class RecordingLifecycleReporter : RecordingReporter, ILifecycleReporter
    {
        private readonly IList<string>? _events;

        public RecordingLifecycleReporter(IList<string>? events = null) : base(events)
        {
            _events = events;
        }

        public int StartCount { get; private set; }
        public int FinishCount { get; private set; }
        public Context? StartContext { get; private set; }
        public DateTime? StartTimeUtc { get; private set; }

        public void StartReport(Context context, DateTime testSuiteStartTimeUtc)
        {
            StartCount++;
            StartContext = context;
            StartTimeUtc = testSuiteStartTimeUtc;
            _events?.Add("start");
        }

        public void FinishReport()
        {
            FinishCount++;
            _events?.Add("finish");
        }
    }

    private sealed class ThrowingLifecycleReporter : RecordingLifecycleReporter
    {
        public override void WriteTestResults(AssertionResult assertionResult)
        {
            throw new InvalidOperationException("report failed");
        }
    }
}
