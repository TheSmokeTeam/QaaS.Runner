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
    public void TestRun_WithNoReporters_DoesNotStartOrFinishReportPortalLaunch()
    {
        var manager = new RecordingReportPortalLaunchManager();
        var reportLogic = CreateReportLogic([], manager);
        var executionData = new ExecutionData();

        var result = reportLogic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        Assert.Multiple(() =>
        {
            Assert.That(manager.StartCount, Is.EqualTo(0));
            Assert.That(manager.FinishCount, Is.EqualTo(0));
        });
    }

    [TestCase(1)]
    [TestCase(5)]
    public void TestRun_WithSingleReporterTarget_WritesMatchingAssertionResults(int assertionCount)
    {
        var reporter = new RecordingReporter();
        var manager = new RecordingReportPortalLaunchManager();
        var assertionResults = new List<AssertionResult>();

        for (int i = 0; i < assertionCount; i++)
        {
            var assertion = new Assertion
            {
                Name = $"Assertion{i + 1}",
                StatusesToReport =
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

        var reportLogic = CreateReportLogic([reporter], manager);
        var executionData = new ExecutionData();

        foreach (var result in assertionResults)
        {
            executionData.AssertionResults.Add(result);
        }

        var resultedExecutionData = reportLogic.Run(executionData);

        Assert.That(resultedExecutionData, Is.Not.Null);
        Assert.That(resultedExecutionData, Is.SameAs(executionData));
        Assert.That(reporter.Results, Is.EqualTo(assertionResults));
        Assert.Multiple(() =>
        {
            Assert.That(manager.StartCount, Is.EqualTo(0));
            Assert.That(manager.FinishCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void TestRun_WithReporterAndNoAssertionResults_DoesNotThrow()
    {
        var reporter = new RecordingReporter();
        var manager = new RecordingReportPortalLaunchManager();
        var reportLogic = CreateReportLogic([reporter], manager);
        var executionData = new ExecutionData();

        Assert.DoesNotThrow(() => reportLogic.Run(executionData));
        Assert.That(reporter.Results, Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(manager.StartCount, Is.EqualTo(0));
            Assert.That(manager.FinishCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void TestRun_WithDifferentReporterTargets_WritesOnlyMatchingAssertions()
    {
        var events = new List<string>();
        var manager = new RecordingReportPortalLaunchManager(events);
        var firstReporter = new RecordingReporter(events) { Target = ReporterTarget.Allure };
        var secondReporter = new AlternateRecordingReporter(events) { Target = ReporterTarget.ReportPortal };
        var firstAssertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "AssertionOne",
                StatusesToReport =
                [
                    AssertionStatus.Broken,
                    AssertionStatus.Failed,
                    AssertionStatus.Passed,
                    AssertionStatus.Skipped,
                    AssertionStatus.Unknown
                ],
                ReporterTargets = [ReporterTarget.Allure],
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
                StatusesToReport =
                [
                    AssertionStatus.Broken,
                    AssertionStatus.Failed,
                    AssertionStatus.Passed,
                    AssertionStatus.Skipped,
                    AssertionStatus.Unknown
                ],
                ReporterTargets = [ReporterTarget.ReportPortal],
                AssertionName = null,
                AssertionHook = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };

        var reportLogic = CreateReportLogic([firstReporter, secondReporter], manager);
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(firstAssertionResult);
        executionData.AssertionResults.Add(secondAssertionResult);

        var result = reportLogic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        Assert.Multiple(() =>
        {
            Assert.That(firstReporter.Results, Is.EqualTo(new[] { firstAssertionResult }));
            Assert.That(secondReporter.Results, Is.EqualTo(new[] { secondAssertionResult }));
            Assert.That(manager.StartCount, Is.EqualTo(1));
            Assert.That(manager.FinishCount, Is.EqualTo(1));
            Assert.That(manager.StartContext, Is.Not.Null);
            Assert.That(manager.StartTimeUtc, Is.EqualTo(TestSuiteStartTimeUtc));
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
        var secondReporter = new AlternateRecordingReporter { Target = ReporterTarget.ReportPortal };
        var manager = new RecordingReportPortalLaunchManager();
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "AssertionOne",
                StatusesToReport =
                [
                    AssertionStatus.Broken,
                    AssertionStatus.Failed,
                    AssertionStatus.Passed,
                    AssertionStatus.Skipped,
                    AssertionStatus.Unknown
                ],
                ReporterTargets = [ReporterTarget.Allure, ReporterTarget.ReportPortal],
                AssertionName = null,
                AssertionHook = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };

        var reportLogic = CreateReportLogic([firstReporter, secondReporter], manager);
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(assertionResult);

        var result = reportLogic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        Assert.Multiple(() =>
        {
            Assert.That(firstReporter.Results, Is.EqualTo(new[] { assertionResult }));
            Assert.That(secondReporter.Results, Is.EqualTo(new[] { assertionResult }));
            Assert.That(manager.StartCount, Is.EqualTo(1));
            Assert.That(manager.FinishCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestRun_WhenAssertionStatusIsNotConfiguredForReporting_DoesNotWriteResults()
    {
        var reporter = new RecordingReporter();
        var manager = new RecordingReportPortalLaunchManager();
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "AssertionA",
                StatusesToReport = [AssertionStatus.Failed],
                AssertionName = null,
                AssertionHook = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(assertionResult);
        var logic = CreateReportLogic([reporter], manager);

        var result = logic.Run(executionData);

        Assert.That(result, Is.SameAs(executionData));
        Assert.That(reporter.Results, Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(manager.StartCount, Is.EqualTo(0));
            Assert.That(manager.FinishCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void TestRun_WhenReportPortalReporterThrows_FinishesReportPortalLaunch()
    {
        var manager = new RecordingReportPortalLaunchManager();
        var reporter = new ThrowingReporter { Target = ReporterTarget.ReportPortal };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "AssertionA",
                StatusesToReport = [AssertionStatus.Passed],
                ReporterTargets = [ReporterTarget.ReportPortal],
                AssertionName = null,
                AssertionHook = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };
        var executionData = new ExecutionData();
        executionData.AssertionResults.Add(assertionResult);
        var logic = CreateReportLogic([reporter], manager);

        Assert.Throws<InvalidOperationException>(() => logic.Run(executionData));
        Assert.Multiple(() =>
        {
            Assert.That(manager.StartCount, Is.EqualTo(1));
            Assert.That(manager.FinishCount, Is.EqualTo(1));
        });
    }

    private static ReportLogic CreateReportLogic(
        IList<IReporter> reporters,
        RecordingReportPortalLaunchManager? manager = null)
    {
        return new ReportLogic(
            reporters,
            Globals.GetContextWithMetadata(),
            manager ?? new RecordingReportPortalLaunchManager(),
            TestSuiteStartTimeUtc);
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

    private sealed class AlternateRecordingReporter : RecordingReporter
    {
        public AlternateRecordingReporter(IList<string>? events = null) : base(events)
        {
        }
    }

    private sealed class ThrowingReporter : RecordingReporter
    {
        public override void WriteTestResults(AssertionResult assertionResult)
        {
            throw new InvalidOperationException("report failed");
        }
    }

    private sealed class RecordingReportPortalLaunchManager : IReportPortalLaunchManager
    {
        private readonly IList<string>? _events;

        public RecordingReportPortalLaunchManager(IList<string>? events = null)
        {
            _events = events;
        }

        public bool IsStarted { get; private set; }
        public string LaunchUuid => throw new NotSupportedException();
        public IReportPortalClient Client => throw new NotSupportedException();
        public int StartCount { get; private set; }
        public int FinishCount { get; private set; }
        public Context? StartContext { get; private set; }
        public DateTime? StartTimeUtc { get; private set; }

        public void Start(Context context, DateTime testSuiteStartTimeUtc)
        {
            StartCount++;
            StartContext = context;
            StartTimeUtc = testSuiteStartTimeUtc;
            IsStarted = true;
            _events?.Add("start");
        }

        public void Finish()
        {
            FinishCount++;
            IsStarted = false;
            _events?.Add("finish");
        }
    }
}
