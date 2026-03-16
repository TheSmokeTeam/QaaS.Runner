using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

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
        var runner = Bootstrap.New<MockRunner>();
        var mockRunner = runner as MockRunner;

        runner.Run();

        Assert.That(runner, Is.TypeOf<MockRunner>());
        Assert.That(mockRunner, Is.Not.Null);
        Assert.IsTrue(mockRunner!.SetupCalled);
        Assert.IsTrue(mockRunner.TeardownCalled);
        Assert.IsTrue(mockRunner.BuildExecutionsCalled);
        Assert.IsTrue(mockRunner.StartExecutionsCalled);
        Assert.That(mockRunner.ExitCode, Is.EqualTo(0));
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
}
