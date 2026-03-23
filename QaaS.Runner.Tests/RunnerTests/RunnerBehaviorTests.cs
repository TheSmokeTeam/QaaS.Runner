using System.Reflection;
using Autofac;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.WrappedExternals;
using Allure.Commons;

namespace QaaS.Runner.Tests.RunnerTests;

[TestFixture]
public class RunnerBehaviorTests
{
    private sealed class ExposedRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger,
        bool emptyResults = false,
        bool serveResults = false) : Runner(scope, executionBuilders, logger, serilogLogger, emptyResults, serveResults)
    {
        public void InvokeSetup() => base.Setup();
        public void InvokeTeardown() => base.Teardown();
        public List<Execution> InvokeBuildExecutions() => base.BuildExecutions();
        public int InvokeStartExecutions(List<Execution> executions) => base.StartExecutions(executions);
    }

    private sealed class BaseExitRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        public void InvokeBaseExitProcess(int exitCode) => base.ExitProcess(exitCode);
    }

    private sealed class RunLifecycleRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        public List<string> Calls { get; } = [];
        public int? ExitCode { get; private set; }

        protected override void Setup() => Calls.Add("setup");

        protected override List<Execution> BuildExecutions()
        {
            Calls.Add("build");
            return [];
        }

        protected override int StartExecutions(List<Execution> executions)
        {
            Calls.Add("start");
            return 7;
        }

        protected override void Teardown() => Calls.Add("teardown");

        protected override void ExitProcess(int exitCode)
        {
            Calls.Add("exit");
            ExitCode = exitCode;
        }
    }

    private sealed class NonExitingRunLifecycleRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        public List<string> Calls { get; } = [];
        public int? ExitCode { get; private set; }
        public int? ProcessExitCode { get; private set; }

        protected override void Setup() => Calls.Add("setup");

        protected override List<Execution> BuildExecutions()
        {
            Calls.Add("build");
            return [];
        }

        protected override int StartExecutions(List<Execution> executions)
        {
            Calls.Add("start");
            return 5;
        }

        protected override void Teardown() => Calls.Add("teardown");

        protected override void ExitProcess(int exitCode)
        {
            Calls.Add("exit");
            ExitCode = exitCode;
        }

        protected override void SetProcessExitCode(int exitCode)
        {
            Calls.Add("set-exit-code");
            ProcessExitCode = exitCode;
        }
    }

    private sealed class ServeResultsRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger,
        bool serveResults) : Runner(scope, executionBuilders, logger, serilogLogger, serveResults: serveResults)
    {
        public bool ServedResults { get; private set; }

        public void InvokeTeardown() => base.Teardown();

        protected override void ServeResultsInAllure()
        {
            ServedResults = true;
        }
    }

    private sealed class AllureSpyWrapper : AllureWrapper
    {
        public bool CleanCalled { get; private set; }
        public bool ServeCalled { get; private set; }

        public override void CleanTestResultsDirectory()
        {
            CleanCalled = true;
        }

        public override void ServeTestResults(string allureRunnablePath = DefaultAllureRunnablePath)
        {
            ServeCalled = true;
        }
    }

    private sealed class FailingBuildRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }

        protected override void Setup() => Calls.Add("setup");

        protected override List<Execution> BuildExecutions()
        {
            Calls.Add("build");
            throw new InvalidOperationException("boom");
        }

        protected override void Teardown() => Calls.Add("teardown");

        public override void Dispose()
        {
            Disposed = true;
            Calls.Add("dispose");
            base.Dispose();
        }
    }

    private sealed class BootstrapHandledRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger,
        bool emptyResults = false,
        bool serveResults = false) : Runner(scope, executionBuilders, logger, serilogLogger, emptyResults, serveResults)
    {
        public List<string> Calls { get; } = [];

        protected override void Setup() => Calls.Add("setup");

        protected override List<Execution> BuildExecutions()
        {
            Calls.Add("build");
            return [];
        }

        protected override int StartExecutions(List<Execution> executions)
        {
            Calls.Add("start");
            return 0;
        }

        protected override void Teardown() => Calls.Add("teardown");

        public override void Dispose()
        {
            Calls.Add("dispose");
            base.Dispose();
        }
    }

    private sealed class PrebuiltExecutionRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger,
        List<Execution> executions) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        private readonly List<Execution> _executions = executions;

        protected override List<Execution> BuildExecutions()
        {
            return _executions;
        }

        protected override int StartExecutions(List<Execution> executions)
        {
            return 0;
        }
    }

    private sealed class CleanupFailureRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger,
        Exception? lifecycleException = null,
        Exception? disposeExecutionsException = null,
        Exception? teardownException = null,
        Exception? disposeException = null) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        private readonly Exception? _lifecycleException = lifecycleException;
        private readonly Exception? _disposeExecutionsException = disposeExecutionsException;
        private readonly Exception? _teardownException = teardownException;
        private readonly Exception? _disposeException = disposeException;

        public List<string> Calls { get; } = [];

        protected override void Setup() => Calls.Add("setup");

        protected override List<Execution> BuildExecutions()
        {
            Calls.Add("build");
            if (_lifecycleException != null)
            {
                throw _lifecycleException;
            }

            return [];
        }

        protected override int StartExecutions(List<Execution> executions)
        {
            Calls.Add("start");
            return 0;
        }

        protected override void DisposeExecutions(IEnumerable<Execution>? executions)
        {
            Calls.Add("dispose-executions");
            if (_disposeExecutionsException != null)
            {
                throw _disposeExecutionsException;
            }

            base.DisposeExecutions(executions);
        }

        protected override void Teardown()
        {
            Calls.Add("teardown");
            if (_teardownException != null)
            {
                throw _teardownException;
            }
        }

        public override void Dispose()
        {
            Calls.Add("dispose");
            if (_disposeException != null)
            {
                throw _disposeException;
            }

            base.Dispose();
        }
    }

    private sealed class FailingStartRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger,
        List<Execution> executions,
        Exception startFailure) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        private readonly List<Execution> _executions = executions;
        private readonly Exception _startFailure = startFailure;

        public List<string> Calls { get; } = [];

        protected override void Setup() => Calls.Add("setup");

        protected override List<Execution> BuildExecutions()
        {
            Calls.Add("build");
            return _executions;
        }

        protected override int StartExecutions(List<Execution> executions)
        {
            Calls.Add("start");
            throw _startFailure;
        }

        protected override void DisposeExecutions(IEnumerable<Execution>? executions)
        {
            Calls.Add("dispose-executions");
            base.DisposeExecutions(executions);
        }

        protected override void Teardown()
        {
            Calls.Add("teardown");
        }

        public override void Dispose()
        {
            Calls.Add("dispose");
            base.Dispose();
        }
    }

    [Test]
    public void Setup_WithEmptyResultsEnabled_CleansAllureResultsDirectory()
    {
        var markerFile = CreateAllureMarkerFile();
        using var scope = BuildScope();
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object, emptyResults: true);

        runner.InvokeSetup();

        Assert.That(File.Exists(markerFile), Is.False);
    }

    [Test]
    public void Setup_WithEmptyResultsDisabled_DoesNotCleanAllureResultsDirectory()
    {
        var markerFile = CreateAllureMarkerFile();
        using var scope = BuildScope();
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object, emptyResults: false);

        runner.InvokeSetup();

        Assert.That(File.Exists(markerFile), Is.True);
        File.Delete(markerFile);
    }

    [Test]
    public void Teardown_DisposesSerilogLoggerWhenDisposable()
    {
        using var scope = BuildScope();
        var serilogLogger = new Mock<Serilog.ILogger>();
        var disposableLogger = serilogLogger.As<IDisposable>();
        var runner = new ExposedRunner(scope, [], Globals.Logger, serilogLogger.Object);

        runner.InvokeTeardown();

        disposableLogger.Verify(logger => logger.Dispose(), Times.Once);
    }

    [Test]
    public void Teardown_WithNonDisposableLogger_DoesNotThrow()
    {
        using var scope = BuildScope();
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);

        Assert.DoesNotThrow(() => runner.InvokeTeardown());
    }

    [Test]
    public void Teardown_WithLoggerDisposalDisabled_DoesNotDisposeSerilogLogger()
    {
        using var scope = BuildScope();
        var serilogLogger = new Mock<Serilog.ILogger>();
        var disposableLogger = serilogLogger.As<IDisposable>();
        var runner = new ExposedRunner(scope, [], Globals.Logger, serilogLogger.Object);
        runner.WithSerilogLoggerDisposal(false);

        runner.InvokeTeardown();

        disposableLogger.Verify(logger => logger.Dispose(), Times.Never);
    }

    [Test]
    public void Teardown_WithServeResultsEnabled_InvokesServeResultsHook()
    {
        using var scope = BuildScope();
        var runner = new ServeResultsRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object, serveResults: true);

        runner.InvokeTeardown();

        Assert.That(runner.ServedResults, Is.True);
    }

    [Test]
    public void Teardown_BaseImplementation_UsesAllureWrapperFromScopeWhenServingResults()
    {
        var allureWrapper = new AllureSpyWrapper();
        using var scope = BuildScope(allureWrapper);
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object,
            serveResults: true);

        runner.InvokeTeardown();

        Assert.That(allureWrapper.ServeCalled, Is.True);
    }

    [Test]
    public void Teardown_WithServeResultsDisabled_DoesNotInvokeServeResultsHook()
    {
        using var scope = BuildScope();
        var runner = new ServeResultsRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object, serveResults: false);

        runner.InvokeTeardown();

        Assert.That(runner.ServedResults, Is.False);
    }

    [Test]
    public void StartExecutions_ReturnsSumOfExecutionExitCodes()
    {
        using var scope = BuildScope();
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);
        var context = CreateContext();

        var firstExecution = new Mock<Execution>(ExecutionType.Run, context);
        firstExecution.Setup(execution => execution.Start()).Returns(1);

        var secondExecution = new Mock<Execution>(ExecutionType.Run, context);
        secondExecution.Setup(execution => execution.Start()).Returns(2);

        var result = runner.InvokeStartExecutions([firstExecution.Object, secondExecution.Object]);

        Assert.That(result, Is.EqualTo(3));
    }

    [Test]
    public void BuildExecutions_WithTemplateBuilders_BuildsExpectedNumberOfExecutions()
    {
        using var scope = BuildScope();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-a"),
            CreateTemplateExecutionBuilder("case-b")
        };

        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);

        var executions = runner.InvokeBuildExecutions();

        Assert.That(executions, Has.Count.EqualTo(2));

        var executionTypeProperty = typeof(Execution).GetProperty("Type", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(executions.Select(execution => executionTypeProperty!.GetValue(execution)),
            Is.All.EqualTo(ExecutionType.Template));
    }

    [Test]
    public void BuildExecutions_UsesSharedGlobalDictionaryAndConfiguredLoggerOnAllBuilders()
    {
        using var scope = BuildScope();
        var logger = Globals.Logger;
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1"),
            CreateTemplateExecutionBuilder("case-2")
        };

        var runner = new ExposedRunner(scope, builders, logger, new Mock<Serilog.ILogger>().Object);
        runner.InvokeBuildExecutions();

        var globalDictField = typeof(ExecutionBuilder).GetField("_globalDict", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var configuredLoggerField = typeof(ExecutionBuilder).GetField("_configuredLogger", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var firstGlobalDict = globalDictField.GetValue(builders[0]);
        var secondGlobalDict = globalDictField.GetValue(builders[1]);
        var firstLogger = configuredLoggerField.GetValue(builders[0]);
        var secondLogger = configuredLoggerField.GetValue(builders[1]);

        Assert.That(firstGlobalDict, Is.SameAs(secondGlobalDict));
        Assert.That(firstLogger, Is.SameAs(logger));
        Assert.That(secondLogger, Is.SameAs(logger));
    }

    [Test]
    public void StartExecutions_WithNoExecutions_ReturnsZero()
    {
        using var scope = BuildScope();
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);

        var result = runner.InvokeStartExecutions([]);

        Assert.That(result, Is.Zero);
    }

    [Test]
    public void Run_InvokesLifecycleInOrder_AndPassesExitCode()
    {
        using var scope = BuildScope();
        var runner = new RunLifecycleRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);

        runner.Run();

        Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "start", "teardown", "exit" }));
        Assert.That(runner.ExitCode, Is.EqualTo(7));
    }

    [Test]
    public void Run_WhenProcessExitIsDisabled_SetsProcessExitCodeWithoutCallingExit()
    {
        using var scope = BuildScope();
        var runner = new NonExitingRunLifecycleRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object)
        {
            ExitProcessOnCompletion = false
        };

        runner.Run();

        Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "start", "teardown", "set-exit-code" }));
        Assert.That(runner.ProcessExitCode, Is.EqualTo(5));
        Assert.That(runner.ExitCode, Is.Null);
        Assert.That(runner.LastExitCode, Is.EqualTo(5));
    }

    [Test]
    public void RunAndGetExitCode_ReturnsExitCodeWithoutCallingProcessExitHooks()
    {
        using var scope = BuildScope();
        var runner = new NonExitingRunLifecycleRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);

        var exitCode = runner.RunAndGetExitCode();

        Assert.That(exitCode, Is.EqualTo(5));
        Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "start", "teardown" }));
        Assert.That(runner.ProcessExitCode, Is.Null);
        Assert.That(runner.ExitCode, Is.Null);
        Assert.That(runner.LastExitCode, Is.EqualTo(5));
    }

    [Test]
    public void RunAndGetExitCode_WhenBootstrapAlreadyHandled_SkipsLifecycleAndDisposesResources()
    {
        using var scope = BuildScope();
        var serilogLogger = new Mock<Serilog.ILogger>();
        var disposableLogger = serilogLogger.As<IDisposable>();
        var runner = Bootstrap.CreateRunner<BootstrapHandledRunner>(scope, [], Globals.Logger, serilogLogger.Object);
        runner.WithBootstrapHandledExitCode(12);

        var exitCode = runner.RunAndGetExitCode();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(12));
            Assert.That(runner.Calls, Is.EqualTo(new[] { "dispose" }));
            Assert.That(runner.LastExitCode, Is.EqualTo(12));
        });
        disposableLogger.Verify(logger => logger.Dispose(), Times.Once);
    }

    [Test]
    public void RunAndGetExitCode_WhenBootstrapAlreadyHandled_RespectsDisabledSerilogDisposal()
    {
        using var scope = BuildScope();
        var serilogLogger = new Mock<Serilog.ILogger>();
        var disposableLogger = serilogLogger.As<IDisposable>();
        var runner = Bootstrap.CreateRunner<BootstrapHandledRunner>(scope, [], Globals.Logger, serilogLogger.Object);
        runner.WithSerilogLoggerDisposal(false)
            .WithBootstrapHandledExitCode(2);

        var exitCode = runner.RunAndGetExitCode();

        Assert.That(exitCode, Is.EqualTo(2));
        disposableLogger.Verify(logger => logger.Dispose(), Times.Never);
    }

    [Test]
    public void ExitProcess_UsesProcessExitHandler()
    {
        using var scope = BuildScope();
        var runner = new BaseExitRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);
        var originalExitHandler = Runner.ProcessExitHandler;
        int? capturedExitCode = null;

        try
        {
            Runner.ProcessExitHandler = exitCode => capturedExitCode = exitCode;

            runner.InvokeBaseExitProcess(9);

            Assert.That(capturedExitCode, Is.EqualTo(9));
        }
        finally
        {
            Runner.ProcessExitHandler = originalExitHandler;
        }
    }

    [Test]
    public void Run_WhenBuildExecutionsThrows_StillRunsTeardownAndDispose()
    {
        using var scope = BuildScope();
        var runner = new FailingBuildRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);

        Assert.Throws<InvalidOperationException>(() => runner.Run());

        Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "teardown", "dispose" }));
        Assert.That(runner.Disposed, Is.True);
        Assert.That(runner.LastExitCode, Is.Null);
    }

    [Test]
    public void RunAndGetExitCode_WhenBuildExecutionsThrows_RethrowsLifecycleFailure()
    {
        using var scope = BuildScope();
        var runner = new FailingBuildRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);

        Assert.Throws<InvalidOperationException>(() => runner.RunAndGetExitCode());
    }

    [Test]
    public void RunAndGetExitCode_DisposesBuiltExecutionsAfterSuccessfulRun()
    {
        using var scope = BuildScope();
        var context = CreateContext();
        var firstExecution = new Mock<Execution>(ExecutionType.Run, context);
        var secondExecution = new Mock<Execution>(ExecutionType.Run, context);
        var runner = new PrebuiltExecutionRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object,
            [firstExecution.Object, secondExecution.Object]);

        var exitCode = runner.RunAndGetExitCode();

        Assert.That(exitCode, Is.Zero);
        firstExecution.Verify(execution => execution.Dispose(), Times.Once);
        secondExecution.Verify(execution => execution.Dispose(), Times.Once);
    }

    [Test]
    public void RunAndGetExitCode_WhenStartExecutionsThrows_DisposesBuiltExecutionsAndRethrowsFailure()
    {
        using var scope = BuildScope();
        var context = CreateContext();
        var execution = new Mock<Execution>(ExecutionType.Run, context);
        var startFailure = new InvalidOperationException("start failed");
        var runner = new FailingStartRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object,
            [execution.Object], startFailure);

        var exception = Assert.Throws<InvalidOperationException>(() => runner.RunAndGetExitCode());

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.SameAs(startFailure));
            Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "start", "dispose-executions", "teardown", "dispose" }));
            Assert.That(runner.LastExitCode, Is.Null);
        });
        execution.Verify(currentExecution => currentExecution.Dispose(), Times.Once);
    }

    [Test]
    public void RunAndGetExitCode_WhenCleanupFailsAfterSuccessfulLifecycle_ThrowsCleanupFailure()
    {
        using var scope = BuildScope();
        var teardownFailure = new InvalidOperationException("teardown failed");
        var runner = new CleanupFailureRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object,
            teardownException: teardownFailure);

        var exception = Assert.Throws<InvalidOperationException>(() => runner.RunAndGetExitCode());

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.SameAs(teardownFailure));
            Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "start", "dispose-executions", "teardown", "dispose" }));
            Assert.That(runner.LastExitCode, Is.EqualTo(0));
        });
    }

    [Test]
    public void RunAndGetExitCode_WhenLifecycleAndCleanupFail_ThrowsAggregateException()
    {
        using var scope = BuildScope();
        var lifecycleFailure = new InvalidOperationException("build failed");
        var teardownFailure = new InvalidOperationException("teardown failed");
        var disposeFailure = new InvalidOperationException("dispose failed");
        var runner = new CleanupFailureRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object,
            lifecycleException: lifecycleFailure,
            teardownException: teardownFailure,
            disposeException: disposeFailure);

        var exception = Assert.Throws<AggregateException>(() => runner.RunAndGetExitCode());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.InnerExceptions, Is.EqualTo(new[] { lifecycleFailure, teardownFailure, disposeFailure }));
            Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "dispose-executions", "teardown", "dispose" }));
            Assert.That(runner.LastExitCode, Is.Null);
        });
    }

    [Test]
    public void RunAndGetExitCode_WhenMultipleCleanupStepsFail_AggregatesCleanupFailures()
    {
        using var scope = BuildScope();
        var disposeExecutionsFailure = new InvalidOperationException("dispose-executions failed");
        var teardownFailure = new InvalidOperationException("teardown failed");
        var runner = new CleanupFailureRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object,
            disposeExecutionsException: disposeExecutionsFailure,
            teardownException: teardownFailure);

        var exception = Assert.Throws<AggregateException>(() => runner.RunAndGetExitCode());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.InnerExceptions, Is.EqualTo(new[] { disposeExecutionsFailure, teardownFailure }));
            Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "start", "dispose-executions", "teardown", "dispose" }));
            Assert.That(runner.LastExitCode, Is.EqualTo(0));
        });
    }

    [Test]
    public void Dispose_WhenCalledTwice_DisposesScopeOnlyOnce()
    {
        using var scope = BuildScope();
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);

        Assert.DoesNotThrow(() =>
        {
            runner.Dispose();
            runner.Dispose();
        });
    }

    private static ILifetimeScope BuildScope(AllureWrapper? allureWrapper = null)
    {
        var builder = new ContainerBuilder();
        if (allureWrapper == null)
        {
            builder.RegisterType<AllureWrapper>().SingleInstance();
        }
        else
        {
            builder.RegisterInstance(allureWrapper).As<AllureWrapper>().SingleInstance();
        }

        return builder.Build().BeginLifetimeScope();
    }

    private static string CreateAllureMarkerFile()
    {
        var resultsDirectory = AllureLifecycle.Instance.ResultsDirectory;
        Directory.CreateDirectory(resultsDirectory);
        var markerFile = Path.Combine(resultsDirectory, $"marker-{Guid.NewGuid():N}.txt");
        File.WriteAllText(markerFile, "marker");
        return markerFile;
    }

    private static ExecutionBuilder CreateTemplateExecutionBuilder(string caseName)
    {
        return new ExecutionBuilder()
            .ExecutionType(ExecutionType.Template)
            .SetExecutionId($"exec-{caseName}")
            .SetCase(caseName)
            .WithMetadata(new MetaDataConfig
            {
                Team = "Smoke",
                System = "QaaS"
            });
    }

    private static InternalContext CreateContext()
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            CaseName = "case",
            ExecutionId = "id",
            RootConfiguration = new ConfigurationBuilder().Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig
        {
            Team = "Smoke",
            System = "QaaS"
        });
        return context;
    }
}
