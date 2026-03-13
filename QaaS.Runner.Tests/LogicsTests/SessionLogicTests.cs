using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
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
}
