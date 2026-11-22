using System.Text.Json;
using Autofac;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Output;

namespace QaaS.Runner.Tests.RunnerTests;

[TestFixture]
[NonParallelizable]
public class AttemptSummaryTests
{
    private const string AttemptId = "0123456789abcdef0123456789abcdef";
    private const string ProjectKey = "abcdef0123456789abcdef0123456789";
    private string _directory = null!;

    [SetUp]
    public void CreateDirectory() =>
        _directory = Directory.CreateTempSubdirectory("qaas-runner-summary-test-").FullName;

    [TearDown]
    public void DeleteDirectory() => Directory.Delete(_directory, true);

    [Test]
    public void InvalidOrAbsentOptInNeverWritesOrCreatesDirectory()
    {
        Assert.That(AttemptSummary.TryStart(AttemptOperation.Run, null, AttemptId, ProjectKey), Is.Null);
        Assert.That(AttemptSummary.TryStart(AttemptOperation.Run, _directory, "../private", ProjectKey), Is.Null);
        Assert.That(AttemptSummary.TryStart(AttemptOperation.Run, _directory, AttemptId, "a@b.example"), Is.Null);
        Assert.That(AttemptSummary.TryStart(AttemptOperation.Run, "relative", AttemptId, ProjectKey), Is.Null);
        Assert.That(Directory.GetFileSystemEntries(_directory), Is.Empty);
    }

    [Test]
    public void IdenticalAttemptCannotOverwriteEarlierTerminalFile()
    {
        NewAttempt().Publish(AttemptOutcome.AssertionFailure, 1);
        NewAttempt().Publish(AttemptOutcome.Pass, 0);
        var summary = LoadSummary();
        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("outcome").GetString(), Is.EqualTo("assertion_failure"));
            Assert.That(summary.GetProperty("exit_code").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("duration_ms").GetInt64(), Is.GreaterThanOrEqualTo(0));
            Assert.That(summary.GetProperty("completed_at").GetString(), Does.EndWith("Z"));
            Assert.That(summary.EnumerateObject().Select(item => item.Name), Is.EquivalentTo(new[]
            {
                "schema_version", "attempt_id", "project_key", "operation", "outcome",
                "completed_at", "duration_ms", "exit_code"
            }));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void BrokenSinkDoesNotChangeReturnedExitCode()
    {
        var path = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(path, "private");
        using var runner = CreateRunner(new FixtureRunner().ExitWith(1),
            AttemptSummary.TryStart(AttemptOperation.Run, path, AttemptId, ProjectKey)!);
        Assert.That(runner.RunAndGetExitCode(), Is.EqualTo(1));
        Assert.That(File.ReadAllText(path), Is.EqualTo("private"));
    }

    [Test]
    public void OriginalInfrastructureExceptionSurvivesHandoffAndProducesBoundedSummary()
    {
        var failure = new InvalidOperationException("private test data is never emitted");
        using var runner = CreateRunner(new FixtureRunner().Throw(failure), NewAttempt());
        Assert.That(Assert.Throws<InvalidOperationException>(() => runner.RunAndGetExitCode()), Is.SameAs(failure));
        var summary = LoadSummary();
        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("outcome").GetString(), Is.EqualTo("infra_failure"));
            Assert.That(summary.GetProperty("exit_code").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(summary.GetRawText(), Does.Not.Contain("private"));
        });
    }

    [Test]
    public void SuccessfulEmptyExecutionProducesCompletedNotAssertionPass()
    {
        using var runner = CreateRunner(new FixtureRunner().ExitWith(0), NewAttempt());
        Assert.That(runner.RunAndGetExitCode(), Is.Zero);
        Assert.That(LoadSummary().GetProperty("outcome").GetString(), Is.EqualTo("completed"));
    }

    [Test]
    public void ObservedCancellationDoesNotChangeThrownException()
    {
        var exception = new OperationCanceledException();
        using var runner = CreateRunner(new FixtureRunner().Throw(exception), NewAttempt());
        Assert.That(Assert.Throws<OperationCanceledException>(() => runner.RunAndGetExitCode()), Is.SameAs(exception));
        Assert.That(LoadSummary().GetProperty("outcome").GetString(), Is.EqualTo("cancelled"));
    }

    [TestCase(0, AssertionStatus.Passed, "pass")]
    [TestCase(1, AssertionStatus.Failed, "assertion_failure")]
    [TestCase(1, AssertionStatus.Broken, "infra_failure")]
    [TestCase(1, AssertionStatus.Skipped, "unknown")]
    public void ClassifiesTerminalAssertionEvidenceWithoutChangingExitCode(
        int exitCode, AssertionStatus status, string outcome)
    {
        var context = new Context();
        context.ExecutionData.AssertionResults.Add(new AssertionResult { AssertionStatus = status });
        using var runner = CreateRunner(new FixtureRunner().ExitWith(exitCode)
            .Observe(new Execution(ExecutionType.Run, context)), NewAttempt());

        Assert.That(runner.RunAndGetExitCode(), Is.EqualTo(exitCode));
        Assert.That(LoadSummary().GetProperty("outcome").GetString(), Is.EqualTo(outcome));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }

    [Test]
    public void TeardownFailureOverrulesApparentlyPassedAssertionsAndPreservesException()
    {
        var context = new Context();
        context.ExecutionData.AssertionResults.Add(new AssertionResult { AssertionStatus = AssertionStatus.Passed });
        var failure = new InvalidOperationException("private teardown detail");
        using var runner = CreateRunner(new FixtureRunner().ExitWith(0).FailTeardown(failure)
            .Observe(new Execution(ExecutionType.Run, context)), NewAttempt());

        Assert.That(Assert.Throws<InvalidOperationException>(() => runner.RunAndGetExitCode()), Is.SameAs(failure));
        var summary = LoadSummary();
        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("outcome").GetString(), Is.EqualTo("infra_failure"));
            Assert.That(summary.GetProperty("exit_code").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(summary.GetRawText(), Does.Not.Contain("private"));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BootstrapLoadFailureRespectsOptInAndPreservesFailure(bool enabled)
    {
        string[] keys = ["QAAS_ATTEMPT_SUMMARY_DIRECTORY", "QAAS_ATTEMPT_ID", "QAAS_PROJECT_KEY"];
        string?[] values = [enabled ? _directory : null, AttemptId, ProjectKey];
        var saved = keys.Select(Environment.GetEnvironmentVariable).ToArray();
        for (var i = 0; i < keys.Length; i++)
            Environment.SetEnvironmentVariable(keys[i], values[i]);

        try
        {
            Assert.Catch(() => Bootstrap.New(
                ["run", Path.Combine(_directory, "missing.qaas.yaml"), "--no-env", "--disable-elastic-defaults"]));
            if (!enabled)
            {
                Assert.That(Directory.GetFileSystemEntries(_directory), Is.Empty);
                return;
            }
            var summary = LoadSummary();
            Assert.That(summary.GetProperty("outcome").GetString(), Is.EqualTo("infra_failure"));
            Assert.That(summary.GetProperty("exit_code").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        }
        finally
        {
            for (var i = 0; i < keys.Length; i++)
                Environment.SetEnvironmentVariable(keys[i], saved[i]);
        }
    }

    [Test]
    public void RejectsNonlocalWindowsPathFormsAndRestrictsUnixFileMode()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var path in new[] { @"\\server\share", @"\/server/share", @"\\?\C:\test", @"C:relative" })
                Assert.That(AttemptSummary.TryStart(AttemptOperation.Run, path, AttemptId, ProjectKey), Is.Null, path);
            return;
        }

        NewAttempt().Publish(AttemptOutcome.Completed, 0);
        const UnixFileMode privateBits = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var actual = File.GetUnixFileMode(Path.Combine(_directory, AttemptId + ".json"));
        Assert.That(actual & ~privateBits, Is.EqualTo((UnixFileMode)0), "Spool files must not be group/world readable or executable");
    }

    private static FixtureRunner CreateRunner(FixtureRunner runner, AttemptSummary summary)
    {
        runner.WithAttemptSummary(summary);
        return runner;
    }

    private AttemptSummary NewAttempt() =>
        AttemptSummary.TryStart(AttemptOperation.Run, _directory, AttemptId, ProjectKey)!;

    private JsonElement LoadSummary()
    {
        var path = Path.Combine(_directory, AttemptId + ".json");
        Assert.That(new FileInfo(path).Length, Is.LessThanOrEqualTo(1024));
        using var parsed = JsonDocument.Parse(File.ReadAllBytes(path));
        return parsed.RootElement.Clone();
    }

    private sealed class FixtureRunner()
        : Runner(new ContainerBuilder().Build(), [], NullLogger.Instance,
            new Serilog.LoggerConfiguration().CreateLogger())
    {
        private int _exitCode;
        private Exception? _failure;
        private Exception? _teardownFailure;
        private readonly List<Execution> _observed = [];

        public FixtureRunner Observe(Execution execution)
        {
            _observed.Add(execution);
            return this;
        }

        public FixtureRunner FailTeardown(Exception failure)
        {
            _teardownFailure = failure;
            return this;
        }

        public FixtureRunner ExitWith(int value)
        {
            _exitCode = value;
            return this;
        }

        public FixtureRunner Throw(Exception failure)
        {
            _failure = failure;
            return this;
        }

        protected override List<Execution> BuildExecutions() => _observed;

        protected override void Teardown()
        {
            base.Teardown();
            if (_teardownFailure is not null)
                throw _teardownFailure;
        }

        protected override int StartExecutions(List<Execution> executions) =>
            _failure is null ? _exitCode : throw _failure;
    }
}
