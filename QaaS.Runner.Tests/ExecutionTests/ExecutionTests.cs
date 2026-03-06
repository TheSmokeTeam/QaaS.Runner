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

namespace QaaS.Runner.Tests.ExecutionTests;

[TestFixture]
public class ExecutionTests
{
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

    private static InternalContext CreateContext()
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
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
