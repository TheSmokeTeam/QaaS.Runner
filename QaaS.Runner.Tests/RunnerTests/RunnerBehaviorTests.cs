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
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters.ReportPortal;
using QaaS.Runner.Logics;

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

    private sealed class CommandAwareGateRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger,
        List<Execution> executions) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        protected override void Setup()
        {
        }

        protected override List<Execution> BuildExecutions() => executions;

        protected override void Teardown()
        {
        }
    }

    private sealed class RecordingExecution(
        ExecutionType type,
        InternalContext context,
        string name,
        IList<string> calls) : Execution(type, context)
    {
        public override int Start()
        {
            calls.Add($"start-{name}");
            return 0;
        }
    }

    private sealed class PublishOrderRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger) : Runner(scope, executionBuilders, logger, serilogLogger)
    {
        public List<string> Calls { get; } = [];
        public string? TerminalOutputPathAtPublish { get; private set; }

        protected override void Setup() => Calls.Add("setup");

        protected override List<Execution> BuildExecutions()
        {
            Calls.Add("build");
            return [CreateRecordingExecution(ExecutionType.Run, "run", [], reportPortalEnabled: true)];
        }

        protected override int StartExecutions(List<Execution> executions)
        {
            Calls.Add("start");
            return base.StartExecutions(executions);
        }

        protected override void PublishReportPortalResults(IEnumerable<Execution>? executions)
        {
            TerminalOutputPathAtPublish = ReportPortalPublisher.TerminalOutputPath;
            Calls.Add("publish-reportportal");
        }

        protected override void DisposeExecutions(IEnumerable<Execution>? executions)
        {
            Calls.Add("dispose-executions");
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

    private sealed class RecordingReportPortalPublisher(
        Exception? validationException = null,
        IList<string>? executionCalls = null)
        : ReportPortalPublisher(Globals.Logger)
    {
        private readonly Exception? _validationException = validationException;
        public List<string> Calls { get; } = [];
        public int ValidatedReporterCount { get; private set; }
        public (string Path, string Text)? CapturedTerminalOutput { get; private set; }

        public override Task ValidateAsync(IEnumerable<ReportPortalReporter> reporters,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("validate");
            executionCalls?.Add("validate");
            ValidatedReporterCount = reporters.Count();
            if (_validationException is not null)
                throw _validationException;

            return Task.CompletedTask;
        }

        public override Task PublishAsync(IEnumerable<ReportPortalReporter> reporters,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("publish");
            if (TerminalOutputPath is not null)
                CapturedTerminalOutput = (TerminalOutputPath, File.ReadAllText(TerminalOutputPath));
            return Task.CompletedTask;
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
    public void BuildExecutions_DoesNotAssignReportPortalRuntimeStateToBuilders()
    {
        using var scope = BuildScope();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-1", reportPortalEnabled: true),
            CreateTemplateExecutionBuilder("case-2", reportPortalEnabled: true)
        };

        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);
        _ = runner.InvokeBuildExecutions();

        var reportPortalRuntimeFields = typeof(ExecutionBuilder)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field =>
                field.Name.Contains("reportPortal", StringComparison.OrdinalIgnoreCase) &&
                !field.FieldType.IsAssignableTo(typeof(Delegate)))
            .Select(field => field.Name)
            .ToArray();

        Assert.That(reportPortalRuntimeFields, Is.Empty);
    }

    [Test]
    [NonParallelizable]
    public void RunAndGetExitCode_WhenReportPortalValidationFails_ReturnsFailureExitCodeBeforeStart()
    {
        using var scope = BuildScope();
        var calls = new List<string>();
        var publisher = new RecordingReportPortalPublisher(
            new InvalidConfigurationsException("ReportPortal validation failed"), calls);
        var execution = CreateRecordingExecution(ExecutionType.Run, "run", calls, reportPortalEnabled: true);
        var runner = new CommandAwareGateRunner(scope, [], Globals.Logger,
            new Mock<Serilog.ILogger>().Object, [execution])
        {
            ReportPortalPublisher = publisher
        };

        var exitCode = runner.RunAndGetExitCode();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(new[] { "validate" }));
            Assert.That(publisher.Calls, Is.EqualTo(new[] { "validate", "publish" }));
            Assert.That(runner.LastExitCode, Is.EqualTo(1));
        });
    }

    [Test]
    public void RunAndGetExitCode_WithMixedCommands_ValidatesImmediatelyBeforeFirstReportingExecution()
    {
        using var scope = BuildScope();
        var calls = new List<string>();
        var executions = new List<Execution>
        {
            CreateRecordingExecution(ExecutionType.Template, "template", calls),
            CreateRecordingExecution(ExecutionType.Act, "act", calls),
            CreateRecordingExecution(ExecutionType.Run, "run", calls, reportPortalEnabled: true),
            CreateRecordingExecution(ExecutionType.Assert, "assert", calls, reportPortalEnabled: true)
        };
        var publisher = new RecordingReportPortalPublisher(executionCalls: calls);
        var runner = new CommandAwareGateRunner(scope, [], Globals.Logger,
            new Mock<Serilog.ILogger>().Object, executions)
        {
            ReportPortalPublisher = publisher
        };

        var exitCode = runner.RunAndGetExitCode();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(publisher.ValidatedReporterCount, Is.EqualTo(2));
            Assert.That(calls, Is.EqualTo(new[]
            {
                "start-template",
                "start-act",
                "validate",
                "start-run",
                "start-assert"
            }));
        });
    }

    [Test]
    public void RunAndGetExitCode_WhenMixedCommandValidationFails_StopsAtFirstReportingExecution()
    {
        using var scope = BuildScope();
        var calls = new List<string>();
        var executions = new List<Execution>
        {
            CreateRecordingExecution(ExecutionType.Template, "template", calls),
            CreateRecordingExecution(ExecutionType.Act, "act", calls),
            CreateRecordingExecution(ExecutionType.Run, "run", calls, reportPortalEnabled: true),
            CreateRecordingExecution(ExecutionType.Template, "later-template", calls)
        };
        var publisher = new RecordingReportPortalPublisher(
            new InvalidConfigurationsException("ReportPortal validation failed"), calls);
        var runner = new CommandAwareGateRunner(scope, [], Globals.Logger,
            new Mock<Serilog.ILogger>().Object, executions)
        {
            ReportPortalPublisher = publisher
        };

        var exitCode = runner.RunAndGetExitCode();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(new[]
            {
                "start-template",
                "start-act",
                "validate"
            }));
        });
    }

    [Test]
    [NonParallelizable]
    public void RunAndGetExitCode_PublishesReportPortalResultsBeforeDisposingExecutions()
    {
        using var scope = BuildScope();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var runner = new PublishOrderRunner(scope,
            [CreateExecutionBuilder(ExecutionType.Run, "case", reportPortalEnabled: true)], Globals.Logger,
            new Mock<Serilog.ILogger>().Object)
        {
            ReportPortalPublisher = new RecordingReportPortalPublisher()
        };

        var exitCode = runner.RunAndGetExitCode();

        Assert.That(exitCode, Is.Zero);
        Assert.That(runner.TerminalOutputPathAtPublish, Is.Not.Null);
        Assert.That(File.Exists(runner.TerminalOutputPathAtPublish), Is.False);
        Assert.That(Console.Out, Is.SameAs(originalOut));
        Assert.That(Console.Error, Is.SameAs(originalError));
        Assert.That(runner.Calls,
            Is.EqualTo(new[]
            {
                "setup",
                "build",
                "start",
                "publish-reportportal",
                "dispose-executions",
                "teardown",
                "dispose"
            }));
    }

    [TestCase(ExecutionType.Template)]
    [TestCase(ExecutionType.Act)]
    [NonParallelizable]
    public void RunAndGetExitCode_WithNonReportingCommand_DoesNotUseReportPortal(ExecutionType executionType)
    {
        using var scope = BuildScope();
        var calls = new List<string>();
        var publisher = new RecordingReportPortalPublisher();
        var execution = CreateRecordingExecution(executionType, executionType.ToString(), calls);
        var runner = new CommandAwareGateRunner(scope,
            [CreateExecutionBuilder(executionType, "case", reportPortalEnabled: true)], Globals.Logger,
            new Mock<Serilog.ILogger>().Object, [execution])
        {
            ReportPortalPublisher = publisher
        };

        var exitCode = runner.RunAndGetExitCode();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(calls, Is.EqualTo(new[] { $"start-{executionType}" }));
            Assert.That(publisher.Calls, Is.Empty);
            Assert.That(publisher.TerminalOutputPath, Is.Null);
        });
    }

    [Test]
    public void RunAndGetExitCode_WithDisabledReportPortal_DoesNotValidateOrPublish()
    {
        using var scope = BuildScope();
        var calls = new List<string>();
        var publisher = new RecordingReportPortalPublisher();
        var execution = CreateRecordingExecution(ExecutionType.Run, "run", calls);
        var runner = new CommandAwareGateRunner(scope, [CreateExecutionBuilder(ExecutionType.Run, "case")],
            Globals.Logger, new Mock<Serilog.ILogger>().Object, [execution])
        {
            ReportPortalPublisher = publisher
        };

        var exitCode = runner.RunAndGetExitCode();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(calls, Is.EqualTo(new[] { "start-run" }));
            Assert.That(publisher.Calls, Is.Empty);
        });
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

    private static ILifetimeScope BuildScope()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<AllureWrapper>().SingleInstance();
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
        string system = "QaaS",
        bool reportPortalEnabled = false)
    {
        return CreateExecutionBuilder(ExecutionType.Template, caseName, rootConfiguration, team, system,
            reportPortalEnabled);
    }

    private static ExecutionBuilder CreateExecutionBuilder(
        ExecutionType executionType,
        string caseName,
        IConfiguration? rootConfiguration = null,
        string team = "Smoke",
        string system = "QaaS",
        bool reportPortalEnabled = false)
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

        var builder = new ExecutionBuilder(context, executionType, null, null, null, null)
            .SetExecutionId($"exec-{caseName}")
            .SetCase(caseName)
            .WithMetadata(new MetaDataConfig
            {
                Team = team,
                System = system
            });

        if (reportPortalEnabled)
            builder.Reporters!.ConfigureReportPortal(new ReportPortalConfig { Enabled = true });

        return builder;
    }

    private static Execution CreateRecordingExecution(
        ExecutionType executionType,
        string name,
        IList<string> calls,
        bool reportPortalEnabled = false)
    {
        var context = CreateContext();
        var reporters = reportPortalEnabled
            ? new List<IReporter>
            {
                new ReportPortalReporter
                {
                    Context = context,
                    Config = new ReportPortalConfig
                    {
                        Enabled = true,
                        Endpoint = "https://reportportal.example/api/",
                        ApiKey = "api-key",
                        Project = "project"
                    }
                }
            }
            : [];

        return new RecordingExecution(executionType, context, name, calls)
        {
            ReportLogic = new ReportLogic(reporters, context)
        };
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
