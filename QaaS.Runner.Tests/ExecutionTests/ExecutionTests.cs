using System.Collections.Immutable;
using Autofac;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Logics;
using QaaS.Runner.Sessions.Session;
using QaaS.Runner.Storage;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace QaaS.Runner.Tests.ExecutionTests;

[TestFixture]
public class ExecutionTests
{
    [TestCase(ExecutionType.Run, AssertionStatus.Passed, 0)]
    [TestCase(ExecutionType.Run, AssertionStatus.Failed, 1)]
    [TestCase(ExecutionType.Assert, AssertionStatus.Broken, 1)]
    [TestCase(ExecutionType.Act, AssertionStatus.Failed, 0)]
    [TestCase(ExecutionType.Template, AssertionStatus.Passed, 0)]
    public void Start_LogsOnePairPerMeasuredExecution(
        ExecutionType type,
        AssertionStatus status,
        int expectedExit
    )
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var factory = new SerilogLoggerFactory(logger);
        var context = CreateContext(factory.CreateLogger("Runner"));
        var execution = CreateExecution(type, context, [CreateAssertion("assertion", status)]);

        Assert.That(execution.Start(), Is.EqualTo(expectedExit));
        var measured = sink.Events.Where(e => e.Properties.ContainsKey("QaasEvent")).ToArray();
        if (type == ExecutionType.Template)
        {
            Assert.That(measured, Is.Empty);
            return;
        }

        Assert.That(
            measured.Select(e => ((ScalarValue)e.Properties["QaasEvent"]).Value),
            Is.EqualTo(new[] { "RunnerExecutionStarted", "RunnerExecutionFinished" })
        );
        Assert.That(
            measured.All(e =>
                e.Properties["Team"].Equals(new ScalarValue("Smoke"))
                && e.Properties["System"].Equals(new ScalarValue("QaaS"))
                && e.Properties["ExecutionId"].Equals(new ScalarValue(context.ExecutionId))
            ),
            Is.True
        );
        Assert.That(measured[1].Properties["Failed"], Is.EqualTo(new ScalarValue(expectedExit)));
        Assert.That(
            sink.Events.Any(e =>
                e.MessageTemplate.Text.StartsWith("Running {ExecutionType} execution")
            ),
            Is.True
        );
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Start_LogsFailureAndPreservesOriginalException(bool cancelled)
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var factory = new SerilogLoggerFactory(logger);
        var context = CreateContext(factory.CreateLogger("Runner"));
        Exception expected = cancelled ? new OperationCanceledException() : new IOException();
        var storage = new Mock<IStorage>();
        storage.Setup(s => s.Retrieve(context.CaseName)).Throws(expected);
        var execution = CreateExecution(ExecutionType.Assert, context, storages: [storage.Object]);

        Assert.That(Assert.Catch(() => execution.Start()), Is.SameAs(expected));
        var finish = sink.Events.Single(e =>
            e.Properties.TryGetValue("QaasEvent", out var kind)
            && kind.Equals(new ScalarValue("RunnerExecutionFinished"))
        );
        Assert.That(finish.Properties["Failed"], Is.EqualTo(new ScalarValue(1)));
    }

    [Test]
    public void Start_UsesEachCasesOwnMetadata()
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var factory = new SerilogLoggerFactory(logger);
        var sharedLogger = factory.CreateLogger("Runner");
        var first = CreateContext(sharedLogger);
        var second = CreateContext(sharedLogger);
        second.InsertValueIntoGlobalDictionary(
            second.GetMetaDataPath(),
            new MetaDataConfig { Team = "Other team", System = "Other system" }
        );

        CreateExecution(ExecutionType.Run, first).Start();
        CreateExecution(ExecutionType.Act, second).Start();

        var starts = sink.Events.Where(e =>
            e.Properties.TryGetValue("QaasEvent", out var kind)
            && kind.Equals(new ScalarValue("RunnerExecutionStarted"))
        );
        Assert.That(
            starts.Select(e =>
                (
                    ((ScalarValue)e.Properties["Team"]).Value,
                    ((ScalarValue)e.Properties["System"]).Value
                )
            ),
            Is.EqualTo(
                new (object?, object?)[] { ("Smoke", "QaaS"), ("Other team", "Other system") }
            )
        );
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [TestCase(null)]
    [TestCase("invalid metadata")]
    public void Start_LeavesUnavailableOwnerMetadataUntouched(string? invalidMetadata)
    {
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger>();
        logger.Setup(l => l.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug)).Returns(true);
        var context = CreateContext(logger.Object);
        context.InternalGlobalDict.Clear();
        var path = context.GetMetaDataPath();
        if (invalidMetadata != null)
            context.InsertValueIntoGlobalDictionary(path, invalidMetadata);

        Assert.That(CreateExecution(ExecutionType.Act, context).Start(), Is.Zero);
        if (invalidMetadata == null)
            Assert.That(context.InternalGlobalDict, Is.Empty);
        else
            Assert.That(context.GetValueFromGlobalDictionary(path), Is.SameAs(invalidMetadata));
    }

    [Test]
    public void Start_PreservesResultWhenLogLevelCheckThrows()
    {
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger>();
        logger
            .Setup(l => l.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            .Throws<IOException>();
        var context = CreateContext(logger.Object);

        Assert.That(CreateExecution(ExecutionType.Act, context).Start(), Is.Zero);
    }

    [Test]
    public void Start_WithRunTypeAndPassedAssertions_ReturnsZero()
    {
        var context = CreateContext();
        var execution = CreateExecution(ExecutionType.Run, context, [CreateAssertion("a1", AssertionStatus.Passed)]);

        var result = execution.Start();

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void Start_WithRunTypeAndFailedAssertion_ReturnsOne()
    {
        var context = CreateContext();
        var execution = CreateExecution(ExecutionType.Run, context,
            [CreateAssertion("a1", AssertionStatus.Passed), CreateAssertion("a2", AssertionStatus.Failed)]);

        var result = execution.Start();

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void Start_WithAssertTypeAndFailedAssertion_ReturnsOneAndRetrievesFromStorage()
    {
        var context = CreateContext();
        var storage = new Mock<IStorage>();
        storage.Setup(s => s.Retrieve(It.IsAny<string>()))
            .Returns(new List<SessionData> { new() { Name = "retrieved-session" } }.ToImmutableList());

        var execution = CreateExecution(ExecutionType.Assert, context,
            [CreateAssertion("a1", AssertionStatus.Failed)], [storage.Object]);

        var result = execution.Start();

        Assert.That(result, Is.EqualTo(1));
        storage.Verify(s => s.Retrieve(context.CaseName), Times.Once);
    }

    [Test]
    public void Start_WithAssertType_DoesNotRunSessions()
    {
        var context = CreateContext();
        var session = new Mock<ISession>();
        session.Setup(s => s.Run(It.IsAny<ExecutionData>()))
            .Returns(new SessionData { Name = "unexpected-session" });

        var execution = new Execution(ExecutionType.Assert, context)
        {
            DataSourceLogic = new DataSourceLogic([], context),
            SessionLogic = new SessionLogic([session.Object], context),
            AssertionLogic = new AssertionLogic([], context),
            ReportLogic = new ReportLogic([], context),
            StorageLogic = new StorageLogic([], context, ExecutionType.Assert),
            TemplateLogic = new TemplateLogic(context, TextWriter.Null)
        };

        var result = execution.Start();

        Assert.That(result, Is.EqualTo(0));
        Assert.That(context.ExecutionData.SessionDatas, Is.Empty);
        session.Verify(s => s.Run(It.IsAny<ExecutionData>()), Times.Never);
    }

    [Test]
    public void Start_WithActType_ReturnsZeroAndStoresToStorage()
    {
        var context = CreateContext();
        context.ExecutionData.SessionDatas.Add(new SessionData { Name = "session-to-store" });

        var storage = new Mock<IStorage>();
        var execution = CreateExecution(ExecutionType.Act, context, storages: [storage.Object]);

        var result = execution.Start();

        Assert.That(result, Is.EqualTo(0));
        storage.Verify(s => s.Store(It.IsAny<ImmutableList<SessionData?>>(), context.CaseName), Times.Once);
    }

    [Test]
    public void Start_WithRunType_DoesNotUseStorage()
    {
        var context = CreateContext();
        var storage = new Mock<IStorage>();
        var execution = CreateExecution(ExecutionType.Run, context, storages: [storage.Object]);

        var result = execution.Start();

        Assert.That(result, Is.EqualTo(0));
        storage.Verify(s => s.Retrieve(It.IsAny<string>()), Times.Never);
        storage.Verify(s => s.Store(It.IsAny<ImmutableList<SessionData?>>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void Start_WithTemplateType_ReturnsZero()
    {
        var context = CreateContext();
        var execution = CreateExecution(ExecutionType.Template, context);

        var result = execution.Start();

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void Start_WithUnknownType_ThrowsArgumentOutOfRangeException()
    {
        var context = CreateContext();
        var execution = CreateExecution((ExecutionType)999, context);

        Assert.Throws<ArgumentOutOfRangeException>(() => execution.Start());
    }

    [Test]
    public void Dispose_WithOwnedScope_DisposesScope()
    {
        var context = CreateContext();
        var scope = new Mock<ILifetimeScope>();
        var execution = CreateExecution(ExecutionType.Template, context, ownedScope: scope.Object);

        execution.Dispose();

        scope.Verify(disposable => disposable.Dispose(), Times.Once);
    }

    private static Execution CreateExecution(ExecutionType executionType, InternalContext context,
        IList<Assertion>? assertions = null, IList<IStorage>? storages = null, ILifetimeScope? ownedScope = null)
    {
        return new Execution(executionType, context, ownedScope)
        {
            DataSourceLogic = new DataSourceLogic([], context),
            SessionLogic = new SessionLogic([], context),
            AssertionLogic = new AssertionLogic(assertions ?? [], context),
            ReportLogic = new ReportLogic([], context),
            StorageLogic = new StorageLogic(storages ?? [], context, executionType),
            TemplateLogic = new TemplateLogic(context, TextWriter.Null)
        };
    }

    private static Assertion CreateAssertion(string name, AssertionStatus status)
    {
        var assertion = new Mock<Assertion>();
        assertion.Object.Name = name;
        assertion.Object.AssertionName = name;
        assertion.Setup(a => a.Execute(It.IsAny<IImmutableList<SessionData?>>(), It.IsAny<IImmutableList<DataSource>?>()))
            .Returns(new AssertionResult
            {
                Assertion = assertion.Object,
                AssertionStatus = status,
                TestDurationMs = 0,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = []
                }
            });
        return assertion.Object;
    }

    private static InternalContext CreateContext(Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        var context = new InternalContext
        {
            Logger = logger ?? Globals.Logger,
            CaseName = "case-name",
            ExecutionId = "execution-id",
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
