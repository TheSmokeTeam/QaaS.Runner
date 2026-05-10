using System.Reflection;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Configurations.CustomExceptions;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Assertions;
using QaaS.Runner.WrappedExternals;
using Allure.Commons;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

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

    private sealed class VariablesDisabledByOverrideRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        public override bool LoadVariablesIntoGlobalDict { get; set; } = false;

        public List<Execution> InvokeBuildExecutions() => base.BuildExecutions();
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

    private sealed class InvalidConfigurationBuildRunner(
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
            throw new InvalidConfigurationsException("invalid configuration");
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
    public void BuildExecutions_LoadsVariablesSectionIntoSharedGlobalDictionaryByDefault()
    {
        using var scope = BuildScope();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["variables:rabbitmq:host"] = "localhost",
                ["variables:rabbitmq:port"] = "5672"
            })
            .Build();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1", configuration),
            CreateTemplateExecutionBuilder("case-2", configuration)
        };

        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);
        runner.InvokeBuildExecutions();

        var globalDictField = typeof(ExecutionBuilder).GetField("_globalDict", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sharedGlobalDict = (Dictionary<string, object?>)globalDictField.GetValue(builders[0])!;
        var variables = (Dictionary<string, object?>)sharedGlobalDict["Variables"]!;
        var rabbitMq = (Dictionary<string, object?>)variables["rabbitmq"]!;

        Assert.Multiple(() =>
        {
            Assert.That(sharedGlobalDict, Contains.Key("Variables"));
            Assert.That(rabbitMq["host"], Is.EqualTo("localhost"));
            Assert.That(rabbitMq["port"], Is.EqualTo("5672"));
        });
    }

    [Test]
    public void BuildExecutions_LoadsVariableListsIntoSharedGlobalDictionaryWithoutIndexedKeys()
    {
        using var scope = BuildScope();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["variables:rabbitmq:hosts:0"] = "primary",
                ["variables:rabbitmq:hosts:1"] = "secondary"
            })
            .Build();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1", configuration)
        };

        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);
        runner.InvokeBuildExecutions();

        var globalDictField = typeof(ExecutionBuilder).GetField("_globalDict", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sharedGlobalDict = (Dictionary<string, object?>)globalDictField.GetValue(builders[0])!;
        var variables = (Dictionary<string, object?>)sharedGlobalDict["Variables"]!;
        var rabbitMq = (Dictionary<string, object?>)variables["rabbitmq"]!;
        var hosts = rabbitMq["hosts"] as List<object?>;

        Assert.Multiple(() =>
        {
            Assert.That(hosts, Is.Not.Null);
            Assert.That(hosts, Is.EqualTo(new object?[] { "primary", "secondary" }));
            Assert.That(rabbitMq.ContainsKey("0"), Is.False);
            Assert.That(rabbitMq.ContainsKey("1"), Is.False);
        });
    }

    [Test]
    public void BuildExecutions_WhenVariablesLoadingIsDisabled_DoesNotPopulateVariablesGlobalPath()
    {
        using var scope = BuildScope();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["variables:rabbitmq:host"] = "localhost"
            })
            .Build();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1", configuration)
        };

        var runner = new VariablesDisabledByOverrideRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);
        runner.InvokeBuildExecutions();

        var globalDictField = typeof(ExecutionBuilder).GetField("_globalDict", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sharedGlobalDict = (Dictionary<string, object?>)globalDictField.GetValue(builders[0])!;

        Assert.That(sharedGlobalDict, Does.Not.ContainKey("Variables"));
    }

    [Test]
    public void BuildExecutions_AssignsSharedReportPortalLaunchManagerWhenRegistered()
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
    public void BuildExecutions_WithMixedTeams_AssignsDifferentReportPortalDescriptorsPerTeam()
    {
        using var scope = BuildScope();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1", team: "Smoke", system: "QaaS"),
            CreateTemplateExecutionBuilder("case-2", team: "AnotherTeam", system: "QaaS")
        };
        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);

        _ = runner.InvokeBuildExecutions();

        var descriptorField = typeof(ExecutionBuilder)
            .GetField("_reportPortalRunDescriptor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var firstDescriptor = (ReportPortalRunDescriptor?)descriptorField.GetValue(builders[0]);
        var secondDescriptor = (ReportPortalRunDescriptor?)descriptorField.GetValue(builders[1]);

        Assert.That(firstDescriptor, Is.Not.Null);
        Assert.That(secondDescriptor, Is.Not.Null);
        Assert.That(secondDescriptor, Is.Not.SameAs(firstDescriptor));
        Assert.That(firstDescriptor!.TeamName, Is.EqualTo("Smoke"));
        Assert.That(secondDescriptor!.TeamName, Is.EqualTo("AnotherTeam"));
        Assert.That(firstDescriptor.SystemName, Is.EqualTo("QaaS"));
        Assert.That(secondDescriptor.SystemName, Is.EqualTo("QaaS"));
    }

    [Test]
    public void RunAndGetExitCode_WhenBuildExecutionsThrowsInvalidConfigurationsException_ReturnsFailureExitCode()
    {
        using var scope = BuildScope();
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger>();
        var runner = new InvalidConfigurationBuildRunner(scope, [], logger.Object, new Mock<Serilog.ILogger>().Object);

        var exitCode = runner.RunAndGetExitCode();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(runner.Calls, Is.EqualTo(new[] { "setup", "build", "teardown", "dispose" }));
            Assert.That(runner.Disposed, Is.True);
            Assert.That(runner.LastExitCode, Is.EqualTo(1));
        });

        logger.Verify(log => log.Log(
                It.Is<LogLevel>(level => level == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
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
    public void BuildExecutions_WithMixedSystems_AssignsDifferentReportPortalDescriptorsPerSystem()
    {
        using var scope = BuildScope();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1", team: "Smoke", system: "QaaS"),
            CreateTemplateExecutionBuilder("case-2", team: "Smoke", system: "Smooth")
        };
        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);

        _ = runner.InvokeBuildExecutions();

        var descriptorField = typeof(ExecutionBuilder)
            .GetField("_reportPortalRunDescriptor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var firstDescriptor = (ReportPortalRunDescriptor?)descriptorField.GetValue(builders[0]);
        var secondDescriptor = (ReportPortalRunDescriptor?)descriptorField.GetValue(builders[1]);

        Assert.That(firstDescriptor, Is.Not.Null);
        Assert.That(secondDescriptor, Is.Not.Null);
        Assert.That(secondDescriptor, Is.Not.SameAs(firstDescriptor));
        Assert.That(firstDescriptor!.TeamName, Is.EqualTo("Smoke"));
        Assert.That(secondDescriptor!.TeamName, Is.EqualTo("Smoke"));
        Assert.That(firstDescriptor.SystemName, Is.EqualTo("QaaS"));
        Assert.That(secondDescriptor.SystemName, Is.EqualTo("Smooth"));
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

    private static ExecutionBuilder CreateTemplateExecutionBuilder(
        string caseName,
        IConfiguration? rootConfiguration = null,
        string team = "Smoke",
        string system = "QaaS")
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            CaseName = caseName,
            ExecutionId = $"exec-{caseName}",
            RootConfiguration = rootConfiguration ?? new ConfigurationBuilder().Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig
        {
            Team = team,
            System = system
        });

        return new ExecutionBuilder(context, ExecutionType.Template, null, null, null, null)
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
