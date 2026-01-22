using System.Reflection;
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
        : Runner(scope, executionBuilders, logger, serilogLogger, emptyResults,
            serveResults)
    {
        private readonly ILogger _logger = logger;
        public bool SetupCalled { get; private set; }
        public bool TeardownCalled { get; private set; }
        public bool BuildExecutionsCalled { get; private set; }
        public bool StartExecutionsCalled { get; private set; }

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
    }

    [Test]
    public void
        TestBootstrapNewWithCustomRunner_InvokeRunMethodWithCustomRunnerMethodImplementations_CustomRunnerMethodsAreCalled()
    {
        // Arrange + Act
        var runner = Bootstrap.New<MockRunner>();

        var mockRunner = runner as MockRunner;

        // Replace the Run method using reflection to avoid Environment.Exit
        var setupMethod = typeof(Runner).GetMethod("Setup", BindingFlags.NonPublic | BindingFlags.Instance);
        var buildExecutionsMethod =
            typeof(Runner).GetMethod("BuildExecutions", BindingFlags.NonPublic | BindingFlags.Instance);
        var startExecutionsMethod =
            typeof(Runner).GetMethod("StartExecutions", BindingFlags.NonPublic | BindingFlags.Instance);
        var teardownMethod = typeof(Runner).GetMethod("Teardown", BindingFlags.NonPublic | BindingFlags.Instance);

        // Create a new method that calls the protected methods without Environment.Exit
        var runDelegate = new Action(() =>
        {
            setupMethod!.Invoke(runner, null);
            var executions = buildExecutionsMethod!.Invoke(runner, null);
            startExecutionsMethod!.Invoke(runner, [executions]);
            teardownMethod!.Invoke(runner, null);
        });

        // Execute the custom run logic
        runDelegate.Invoke();

        // Assert 
        // Check that the returned runner is of the correct type
        Assert.That(runner, Is.TypeOf<MockRunner>());

        // Verify the overridden methods were called
        Assert.IsTrue(mockRunner!.SetupCalled);
        Assert.IsTrue(mockRunner.TeardownCalled);
        Assert.IsTrue(mockRunner.BuildExecutionsCalled);
        Assert.IsTrue(mockRunner.StartExecutionsCalled);
    }
}