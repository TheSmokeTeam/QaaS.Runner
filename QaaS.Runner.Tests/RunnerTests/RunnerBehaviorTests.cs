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
using QaaS.Runner.Assertions;
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
    public void Teardown_WithServeResultsEnabled_InvokesServeResultsHook()
    {
        using var scope = BuildScope();
        var runner = new ServeResultsRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object, serveResults: true);

        runner.InvokeTeardown();

        Assert.That(runner.ServedResults, Is.True);
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

        var executionTypeProperty = typeof(Execution).GetProperty("Type", BindingFlags.Instance | BindingFlags.NonPublic);
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
    public void BuildExecutions_WithRegisteredReportPortalLaunchManager_AssignsItToAllBuilders()
    {
        using var scope = BuildScope(registerReportPortalLaunchManager: true);
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1"),
            CreateTemplateExecutionBuilder("case-2")
        };

        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);
        _ = runner.InvokeBuildExecutions();

        var managerField = typeof(ExecutionBuilder)
            .GetField("_reportPortalLaunchManager", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var registeredManager = scope.Resolve<ReportPortalLaunchManager>();

        Assert.That(managerField.GetValue(builders[0]), Is.SameAs(registeredManager));
        Assert.That(managerField.GetValue(builders[1]), Is.SameAs(registeredManager));
    }

    [Test]
    public void BuildExecutions_AssignsSharedReportPortalRunDescriptorToAllBuilders()
    {
        using var scope = BuildScope();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1", team: "Smoke", system: "QaaS"),
            CreateTemplateExecutionBuilder("case-2", team: "Smoke", system: "QaaS")
        };

        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);
        _ = runner.InvokeBuildExecutions();

        var descriptorField = typeof(ExecutionBuilder)
            .GetField("_reportPortalRunDescriptor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var firstDescriptor = descriptorField.GetValue(builders[0]);
        var secondDescriptor = descriptorField.GetValue(builders[1]);

        Assert.That(firstDescriptor, Is.Not.Null);
        Assert.That(secondDescriptor, Is.SameAs(firstDescriptor));
    }

    [Test]
    public void BuildExecutions_WithMixedTeams_ThrowsInvalidOperationException()
    {
        using var scope = BuildScope();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1", team: "Smoke", system: "QaaS"),
            CreateTemplateExecutionBuilder("case-2", team: "AnotherTeam", system: "QaaS")
        };
        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);

        var exception = Assert.Throws<InvalidOperationException>(() => runner.InvokeBuildExecutions());

        Assert.That(exception!.Message, Does.Contain("multiple MetaData.Team values"));
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
    public void Run_WhenBuildExecutionsThrows_StillRunsTeardownAndDispose()
    {
        using var scope = BuildScope();
        var runner = new FailingBuildRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);

        Assert.Throws<InvalidOperationException>(() => runner.Run());

        Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "teardown", "dispose" }));
        Assert.That(runner.Disposed, Is.True);
    }

    private static ILifetimeScope BuildScope(bool registerReportPortalLaunchManager = false)
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<AllureWrapper>().SingleInstance();
        if (registerReportPortalLaunchManager)
            builder.RegisterType<ReportPortalLaunchManager>().SingleInstance();
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

    private static ExecutionBuilder CreateTemplateExecutionBuilder(string caseName, string team = "Smoke",
        string system = "QaaS")
    {
        return new ExecutionBuilder()
            .ExecutionType(ExecutionType.Template)
            .SetExecutionId($"exec-{caseName}")
            .SetCase(caseName)
            .WithMetadata(new MetaDataConfig
            {
                Team = team,
                System = system
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
