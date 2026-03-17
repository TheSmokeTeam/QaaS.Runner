using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Logics;
using QaaS.Runner.Sessions.Session;

namespace QaaS.Runner.Tests.LogicsTests;

public class SessionLogicTests
{
    [Test]
    public void TestRun_WithMultipleSessions_ReturnsExecutionDataWithSessionDatas()
    {
        // Arrange
        var mockSession1 = new Mock<ISession>();
        var mockSession2 = new Mock<ISession>();
        var sessionData1 = new SessionData { Name = "Session1" };
        var sessionData2 = new SessionData { Name = "Session2" };

        mockSession1.SetupGet(session => session.RunUntilStage).Returns((int?)null);
        mockSession2.SetupGet(session => session.RunUntilStage).Returns((int?)null);

        mockSession1.Setup(s => s.RunAsync(It.IsAny<ExecutionData>())).ReturnsAsync(sessionData1);
        mockSession2.Setup(s => s.RunAsync(It.IsAny<ExecutionData>())).ReturnsAsync(sessionData2);

        var mockSessions = new List<ISession> { mockSession1.Object, mockSession2.Object };
        var context = new InternalContext { Logger = Globals.Logger };
        var sessionLogic = new SessionLogic(mockSessions, context);
        var executionData = new ExecutionData();

        // Act
        var result = sessionLogic.Run(executionData);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(2));
        Assert.That(executionData.SessionDatas, Contains.Item(sessionData1));
        Assert.That(executionData.SessionDatas, Contains.Item(sessionData2));
    }

    [Test]
    public void TestRun_WithBlockingSessions_ReturnsExecutionDataWithAllSessionDatas()
    {
        // Arrange
        var mockSession1 = new Mock<ISession>();
        var mockSession2 = new Mock<ISession>();
        var sessionData1 = new SessionData() { Name = "Session1" };
        var sessionData2 = new SessionData() { Name = "Session2" };

        mockSession1.SetupGet(session => session.SessionStage).Returns(1);
        mockSession2.SetupGet(session => session.SessionStage).Returns(2);
        mockSession1.SetupGet(session => session.RunUntilStage).Returns(2);
        mockSession2.SetupGet(session => session.RunUntilStage).Returns((int?)null);

        mockSession1.Setup(s => s.RunAsync(It.IsAny<ExecutionData>())).ReturnsAsync(sessionData1);
        mockSession2.Setup(s => s.RunAsync(It.IsAny<ExecutionData>())).ReturnsAsync(sessionData2);

        var mockSessions = new List<ISession> { mockSession1.Object, mockSession2.Object };
        var context = new InternalContext { Logger = Globals.Logger };
        var sessionLogic = new SessionLogic(mockSessions, context);
        var executionData = new ExecutionData();

        // Act
        var result = sessionLogic.Run(executionData);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(2));
        Assert.That(executionData.SessionDatas, Contains.Item(sessionData1));
        Assert.That(executionData.SessionDatas, Contains.Item(sessionData2));
    }

    [Test]
    public void TestRun_WithNoSessions_ReturnsSameExecutionData()
    {
        // Arrange
        var mockSessions = new List<ISession>();
        var context = new InternalContext { Logger = Globals.Logger };
        var sessionLogic = new SessionLogic(mockSessions, context);
        var executionData = new ExecutionData();

        // Act
        var result = sessionLogic.Run(executionData);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(0));
    }

    [Test]
    public void TestRun_WithUnsortedSessionStages_RunsInStageOrder()
    {
        // Arrange
        var runOrder = new ConcurrentQueue<string>();
        var stage2SessionData = new SessionData { Name = "Stage2Session" };
        var stage1SessionData = new SessionData { Name = "Stage1Session" };

        var stage2Session = new Mock<ISession>();
        stage2Session.SetupGet(s => s.SessionStage).Returns(2);
        stage2Session.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        stage2Session.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns(() =>
            {
                runOrder.Enqueue("stage-2");
                return Task.FromResult<SessionData?>(stage2SessionData);
            });

        var stage1Session = new Mock<ISession>();
        stage1Session.SetupGet(s => s.SessionStage).Returns(1);
        stage1Session.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        stage1Session.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns(() =>
            {
                runOrder.Enqueue("stage-1");
                return Task.FromResult<SessionData?>(stage1SessionData);
            });

        var sessionLogic = new SessionLogic([stage2Session.Object, stage1Session.Object], new InternalContext
        {
            Logger = Globals.Logger
        });
        var executionData = new ExecutionData();

        // Act
        sessionLogic.Run(executionData);

        // Assert
        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(2));
        Assert.That(runOrder.ToArray(), Is.EqualTo(new[] { "stage-1", "stage-2" }));
    }

    [Test]
    public void TestRun_WithBlockingSession_WaitsForBlockerBeforeRunningBlockedStage()
    {
        // Arrange
        var blockerCompleted = 0;

        var blockingSession = new Mock<ISession>();
        var blockedStageSession = new Mock<ISession>();

        blockingSession.SetupGet(s => s.SessionStage).Returns(1);
        blockingSession.SetupGet(s => s.RunUntilStage).Returns(2);
        blockingSession.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns(() =>
            {
                Thread.Sleep(60);
                Interlocked.Exchange(ref blockerCompleted, 1);
                return Task.FromResult<SessionData?>(new SessionData { Name = "BlockingSession" });
            });

        blockedStageSession.SetupGet(s => s.SessionStage).Returns(2);
        blockedStageSession.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        blockedStageSession.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns(() =>
            {
                Assert.That(Interlocked.CompareExchange(ref blockerCompleted, 0, 0), Is.EqualTo(1));
                return Task.FromResult<SessionData?>(new SessionData { Name = "BlockedStageSession" });
            });

        var sessionLogic = new SessionLogic([blockingSession.Object, blockedStageSession.Object], new InternalContext
        {
            Logger = Globals.Logger
        });
        var executionData = new ExecutionData();

        // Act
        sessionLogic.Run(executionData);

        // Assert
        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(2));
        blockingSession.Verify(s => s.RunAsync(It.IsAny<ExecutionData>()), Times.Once);
        blockedStageSession.Verify(s => s.RunAsync(It.IsAny<ExecutionData>()), Times.Once);
    }

    [Test]
    public void TestRun_WithDeferredSessionDataNeededByNextStage_MaterializesBeforeStageStartsWithoutDuplication()
    {
        var sessionAData = new SessionData { Name = "SessionA" };
        var sessionBData = new SessionData { Name = "SessionB" };

        var sessionA = new Mock<ISession>();
        sessionA.SetupGet(s => s.Name).Returns("SessionA");
        sessionA.SetupGet(s => s.SessionStage).Returns(0);
        sessionA.SetupGet(s => s.RunUntilStage).Returns(1);
        sessionA.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns(async () =>
            {
                await Task.Delay(40);
                return sessionAData;
            });

        var sessionB = new Mock<ISession>();
        sessionB.SetupGet(s => s.Name).Returns("SessionB");
        sessionB.SetupGet(s => s.SessionStage).Returns(1);
        sessionB.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        sessionB.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns<ExecutionData>(executionData =>
            {
                Assert.That(executionData.SessionDatas.Count, Is.EqualTo(1));
                Assert.That(executionData.SessionDatas[0], Is.SameAs(sessionAData));
                return Task.FromResult<SessionData?>(sessionBData);
            });

        var sessionLogic = new SessionLogic([sessionA.Object, sessionB.Object], new InternalContext
        {
            Logger = Globals.Logger
        });
        var executionData = new ExecutionData();

        sessionLogic.Run(executionData);

        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(2));
        Assert.That(executionData.SessionDatas.Count(sessionData => ReferenceEquals(sessionData, sessionAData)),
            Is.EqualTo(1));
        Assert.That(executionData.SessionDatas.Count(sessionData => ReferenceEquals(sessionData, sessionBData)),
            Is.EqualTo(1));
    }

    [Test]
    public void TestRun_WithBlockingSessionOnMissingTargetStage_AddsBlockingResultAtEnd()
    {
        // Arrange
        var blockingSessionData = new SessionData { Name = "BlockingSession" };
        var regularSessionData = new SessionData { Name = "RegularSession" };

        var blockingSession = new Mock<ISession>();
        blockingSession.SetupGet(s => s.SessionStage).Returns(1);
        blockingSession.SetupGet(s => s.RunUntilStage).Returns(99);
        blockingSession.Setup(s => s.RunAsync(It.IsAny<ExecutionData>())).ReturnsAsync(blockingSessionData);

        var regularSession = new Mock<ISession>();
        regularSession.SetupGet(s => s.SessionStage).Returns(1);
        regularSession.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        regularSession.Setup(s => s.RunAsync(It.IsAny<ExecutionData>())).ReturnsAsync(regularSessionData);

        var sessionLogic = new SessionLogic([blockingSession.Object, regularSession.Object], new InternalContext
        {
            Logger = Globals.Logger
        });
        var executionData = new ExecutionData();

        // Act
        sessionLogic.Run(executionData);

        // Assert
        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(2));
        Assert.That(executionData.SessionDatas, Contains.Item(blockingSessionData));
        Assert.That(executionData.SessionDatas, Contains.Item(regularSessionData));
    }

    [Test]
    public void TestRun_WithMultipleSessionsInSameStage_MaterializesAllBeforeNextStageStarts()
    {
        var firstStageCompletedCount = 0;
        var stage0SessionAData = new SessionData { Name = "Stage0-A" };
        var stage0SessionBData = new SessionData { Name = "Stage0-B" };
        var stage1SessionData = new SessionData { Name = "Stage1" };

        var stage0SessionA = new Mock<ISession>();
        stage0SessionA.SetupGet(s => s.Name).Returns("Stage0-A");
        stage0SessionA.SetupGet(s => s.SessionStage).Returns(0);
        stage0SessionA.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        stage0SessionA.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns(async () =>
            {
                await Task.Delay(50);
                Interlocked.Increment(ref firstStageCompletedCount);
                return stage0SessionAData;
            });

        var stage0SessionB = new Mock<ISession>();
        stage0SessionB.SetupGet(s => s.Name).Returns("Stage0-B");
        stage0SessionB.SetupGet(s => s.SessionStage).Returns(0);
        stage0SessionB.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        stage0SessionB.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns(async () =>
            {
                await Task.Delay(20);
                Interlocked.Increment(ref firstStageCompletedCount);
                return stage0SessionBData;
            });

        var stage1Session = new Mock<ISession>();
        stage1Session.SetupGet(s => s.Name).Returns("Stage1");
        stage1Session.SetupGet(s => s.SessionStage).Returns(1);
        stage1Session.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        stage1Session.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns<ExecutionData>(executionData =>
            {
                Assert.That(Interlocked.CompareExchange(ref firstStageCompletedCount, 0, 0), Is.EqualTo(2));
                Assert.That(executionData.SessionDatas, Has.Count.EqualTo(2));
                Assert.That(executionData.SessionDatas, Contains.Item(stage0SessionAData));
                Assert.That(executionData.SessionDatas, Contains.Item(stage0SessionBData));
                return Task.FromResult<SessionData?>(stage1SessionData);
            });

        var sessionLogic = new SessionLogic(
            [stage0SessionA.Object, stage0SessionB.Object, stage1Session.Object],
            new InternalContext { Logger = Globals.Logger });
        var executionData = new ExecutionData();

        sessionLogic.Run(executionData);

        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(3));
        Assert.That(executionData.SessionDatas, Contains.Item(stage1SessionData));
    }

    [Test]
    public void TestRun_WithDeferredSession_OverlapsIntermediateStageButBlocksItsTargetStage()
    {
        var deferredCompleted = 0;
        var deferredSessionData = new SessionData { Name = "Deferred" };
        var intermediateSessionData = new SessionData { Name = "Intermediate" };
        var blockedSessionData = new SessionData { Name = "Blocked" };

        var deferredSession = new Mock<ISession>();
        deferredSession.SetupGet(s => s.Name).Returns("Deferred");
        deferredSession.SetupGet(s => s.SessionStage).Returns(0);
        deferredSession.SetupGet(s => s.RunUntilStage).Returns(2);
        deferredSession.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns(async () =>
            {
                await Task.Delay(75);
                Interlocked.Exchange(ref deferredCompleted, 1);
                return deferredSessionData;
            });

        var intermediateSession = new Mock<ISession>();
        intermediateSession.SetupGet(s => s.Name).Returns("Intermediate");
        intermediateSession.SetupGet(s => s.SessionStage).Returns(1);
        intermediateSession.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        intermediateSession.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns<ExecutionData>(executionData =>
            {
                Assert.That(Interlocked.CompareExchange(ref deferredCompleted, 0, 0), Is.EqualTo(0));
                Assert.That(executionData.SessionDatas, Is.Empty);
                return Task.FromResult<SessionData?>(intermediateSessionData);
            });

        var blockedSession = new Mock<ISession>();
        blockedSession.SetupGet(s => s.Name).Returns("Blocked");
        blockedSession.SetupGet(s => s.SessionStage).Returns(2);
        blockedSession.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        blockedSession.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .Returns<ExecutionData>(executionData =>
            {
                Assert.That(Interlocked.CompareExchange(ref deferredCompleted, 0, 0), Is.EqualTo(1));
                Assert.That(executionData.SessionDatas, Has.Count.EqualTo(2));
                Assert.That(executionData.SessionDatas, Contains.Item(deferredSessionData));
                Assert.That(executionData.SessionDatas, Contains.Item(intermediateSessionData));
                return Task.FromResult<SessionData?>(blockedSessionData);
            });

        var sessionLogic = new SessionLogic(
            [deferredSession.Object, intermediateSession.Object, blockedSession.Object],
            new InternalContext { Logger = Globals.Logger });
        var executionData = new ExecutionData();

        sessionLogic.Run(executionData);

        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(3));
        Assert.That(executionData.SessionDatas.Count(sessionData => ReferenceEquals(sessionData, deferredSessionData)),
            Is.EqualTo(1));
        Assert.That(executionData.SessionDatas.Count(sessionData => ReferenceEquals(sessionData, intermediateSessionData)),
            Is.EqualTo(1));
        Assert.That(executionData.SessionDatas.Count(sessionData => ReferenceEquals(sessionData, blockedSessionData)),
            Is.EqualTo(1));
    }

    [Test]
    public void TestRun_WithSyncOnlySession_UsesDefaultRunAsyncBridge()
    {
        var sessionData = new SessionData { Name = "SyncSession" };
        var session = new SyncOnlySession(sessionData);
        var sessionLogic = new SessionLogic([session], new InternalContext { Logger = Globals.Logger });
        var executionData = new ExecutionData();

        sessionLogic.Run(executionData);

        Assert.That(session.RunCalls, Is.EqualTo(1));
        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(1));
        Assert.That(executionData.SessionDatas[0], Is.SameAs(sessionData));
    }

    [Test]
    public void TestRun_LogsStageStartAtInformationLevel_WithoutPrematureStageFinishNoise()
    {
        var logger = new CapturingLogger();
        var session = new Mock<ISession>();
        session.SetupGet(s => s.Name).Returns("SessionA");
        session.SetupGet(s => s.SessionStage).Returns(0);
        session.SetupGet(s => s.RunUntilStage).Returns((int?)null);
        session.Setup(s => s.RunAsync(It.IsAny<ExecutionData>()))
            .ReturnsAsync(new SessionData { Name = "SessionA" });

        var sessionLogic = new SessionLogic([session.Object], new InternalContext { Logger = logger });
        var executionData = new ExecutionData();

        sessionLogic.Run(executionData);

        var informationMessages = logger.Entries
            .Where(entry => entry.LogLevel == LogLevel.Information)
            .Select(entry => entry.Message)
            .ToArray();

        Assert.That(informationMessages,
            Contains.Item("Starting session stage 0 with 1 session(s): SessionA"));
        Assert.That(informationMessages,
            Has.None.Matches<string>(message => message.StartsWith("Finished session stage 0",
                StringComparison.Ordinal)));
    }

    private sealed class SyncOnlySession(SessionData sessionData) : ISession
    {
        public string Name => sessionData.Name!;
        public int? RunUntilStage => null;
        public int SessionStage => 0;
        public int RunCalls { get; private set; }

        public SessionData? Run(ExecutionData executionData)
        {
            RunCalls++;
            return sessionData;
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NoOpScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message);
}
