using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.IO;

namespace QaaS.Runner.Tests.RunnerTests;

[TestFixture]
public class RunnerTests
{
    private class MockRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        ILogger logger, Serilog.ILogger serilogLogger,
        bool emptyResults = false,
        bool serveResults = false)
        : Runner(scope, executionBuilders, logger, serilogLogger, emptyResults, serveResults)
    {
        private readonly ILogger _logger = logger;
        public bool SetupCalled { get; private set; }
        public bool TeardownCalled { get; private set; }
        public bool BuildExecutionsCalled { get; private set; }
        public bool StartExecutionsCalled { get; private set; }
        public int? ExitCode { get; private set; }

        protected override void Setup()
        {
            SetupCalled = true;
            _logger.LogInformation("Setup called");
        }

        protected override void Teardown()
        {
            TeardownCalled = true;
            _logger.LogInformation("Teardown called");
        }

        protected override List<Execution> BuildExecutions()
        {
            BuildExecutionsCalled = true;
            _logger.LogInformation("BuildExecutions called");
            return [];
        }

        protected override int StartExecutions(List<Execution> executions)
        {
            StartExecutionsCalled = true;
            _logger.LogInformation("StartExecutions called");
            return 0;
        }

        protected override void ExitProcess(int exitCode)
        {
            ExitCode = exitCode;
            _logger.LogInformation("ExitProcess called");
        }
    }

    [Test]
    public void
        TestBootstrapNewWithCustomRunner_InvokeRunMethodWithCustomRunnerMethodImplementations_CustomRunnerMethodsAreCalled()
    {
        var runner = Bootstrap.New<MockRunner>(["run", "TestData/test.qaas.yaml", "--no-process-exit"]);
        var mockRunner = runner as MockRunner;

        runner.Run();

        Assert.That(runner, Is.TypeOf<MockRunner>());
        Assert.That(mockRunner, Is.Not.Null);
        Assert.IsTrue(mockRunner!.SetupCalled);
        Assert.IsTrue(mockRunner.TeardownCalled);
        Assert.IsTrue(mockRunner.BuildExecutionsCalled);
        Assert.IsTrue(mockRunner.StartExecutionsCalled);
        Assert.That(mockRunner.ExitCode, Is.Null);
    }

    [Test]
    public void BootstrapNew_WithNullArguments_WritesHelpWithoutInvokingRunnerLifecycle()
    {
        var helpOutput = CaptureStandardOutput(() =>
        {
            var runner = Bootstrap.New<MockRunner>(null);
            var mockRunner = runner as MockRunner;

            var exitCode = runner.RunAndGetExitCode();

            Assert.That(exitCode, Is.Zero);
            Assert.That(mockRunner, Is.Not.Null);
            Assert.That(mockRunner!.SetupCalled, Is.False);
            Assert.That(mockRunner.TeardownCalled, Is.False);
            Assert.That(mockRunner.BuildExecutionsCalled, Is.False);
            Assert.That(mockRunner.StartExecutionsCalled, Is.False);
        });

        Assert.That(helpOutput, Does.Contain("Usage:"));
    }

    [Test]
    public void BootstrapNew_WithEmptyArguments_WritesHelpWithoutInvokingRunnerLifecycle()
    {
        var helpOutput = CaptureStandardOutput(() =>
        {
            var runner = Bootstrap.New<MockRunner>([]);
            var mockRunner = runner as MockRunner;

            var exitCode = runner.RunAndGetExitCode();

            Assert.That(exitCode, Is.Zero);
            Assert.That(mockRunner, Is.Not.Null);
            Assert.That(mockRunner!.SetupCalled, Is.False);
            Assert.That(mockRunner.TeardownCalled, Is.False);
            Assert.That(mockRunner.BuildExecutionsCalled, Is.False);
            Assert.That(mockRunner.StartExecutionsCalled, Is.False);
        });

        Assert.That(helpOutput, Does.Contain("Usage:"));
    }

    [Test]
    public void BootstrapNew_WithHelpFlag_WritesHelpWithoutInvokingRunnerLifecycle()
    {
        var helpOutput = CaptureStandardOutput(() =>
        {
            var runner = Bootstrap.New<MockRunner>(["--help"]);
            var mockRunner = runner as MockRunner;

            var exitCode = runner.RunAndGetExitCode();

            Assert.That(exitCode, Is.Zero);
            Assert.That(mockRunner, Is.Not.Null);
            Assert.That(mockRunner!.SetupCalled, Is.False);
            Assert.That(mockRunner.TeardownCalled, Is.False);
            Assert.That(mockRunner.BuildExecutionsCalled, Is.False);
            Assert.That(mockRunner.StartExecutionsCalled, Is.False);
        });

        Assert.That(helpOutput, Does.Contain("Usage:"));
    }

    [Test]
    public void BootstrapNew_WithVersionFlag_DoesNotInvokeRunnerLifecycle()
    {
        CaptureStandardOutput(() =>
        {
            var runner = Bootstrap.New<MockRunner>(["--version"]);
            var mockRunner = runner as MockRunner;

            var exitCode = runner.RunAndGetExitCode();

            Assert.That(exitCode, Is.Zero);
            Assert.That(mockRunner, Is.Not.Null);
            Assert.That(mockRunner!.SetupCalled, Is.False);
            Assert.That(mockRunner.TeardownCalled, Is.False);
            Assert.That(mockRunner.BuildExecutionsCalled, Is.False);
            Assert.That(mockRunner.StartExecutionsCalled, Is.False);
        });
    }

    [Test]
    public void BootstrapNew_WithInvalidCommand_DoesNotInvokeRunnerLifecycle_AndReturnsFailureExitCode()
    {
        var commandOutput = CaptureStandardOutput(() =>
        {
            var runner = Bootstrap.New<MockRunner>(["invalid-command"]);
            var mockRunner = runner as MockRunner;

            var exitCode = runner.RunAndGetExitCode();

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(mockRunner, Is.Not.Null);
            Assert.That(mockRunner!.SetupCalled, Is.False);
            Assert.That(mockRunner.TeardownCalled, Is.False);
            Assert.That(mockRunner.BuildExecutionsCalled, Is.False);
            Assert.That(mockRunner.StartExecutionsCalled, Is.False);
        });

        Assert.That(commandOutput, Does.Contain("Usage:"));
    }

    [Test]
    public void BootstrapNew_RunAndGetExitCode_AllowsMultipleDefaultRunnersSequentially()
    {
        var firstRunner = Bootstrap.New();
        var secondRunner = Bootstrap.New();

        Assert.Multiple(() =>
        {
            Assert.That(firstRunner.RunAndGetExitCode(), Is.Zero);
            Assert.That(secondRunner.RunAndGetExitCode(), Is.Zero);
        });
    }

    private static string CaptureStandardOutput(Action action)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
