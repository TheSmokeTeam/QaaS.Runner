using System.Reflection;
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
    private PropertyInfo _sessionStageInfo =
        typeof(Session).GetProperty(nameof(Session.SessionStage), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private PropertyInfo _runUntilStageInfo =
        typeof(Session).GetProperty(nameof(Session.RunUntilStage), BindingFlags.Instance | BindingFlags.NonPublic)!;

    [TestCase(ExecutionType.Assert, false)]
    [TestCase(ExecutionType.Run, true)]
    [TestCase(ExecutionType.Template, false)]
    [TestCase(ExecutionType.Act, true)]
    public void TestShouldRun_WithExecutionType_ReturnsExpectedBoolean(ExecutionType executionType, bool expected)
    {
        // Arrange
        var mockSessions = new List<ISession>();
        var context = new InternalContext() { Logger = Globals.Logger };
        var sessionLogic = new SessionLogic(mockSessions, context);

        // Act & Assert
        Assert.That(sessionLogic.ShouldRun(executionType), Is.EqualTo(expected));
    }

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

        mockSession1.Setup(s => s.Run(It.IsAny<ExecutionData>())).Returns(sessionData1);
        mockSession2.Setup(s => s.Run(It.IsAny<ExecutionData>())).Returns(sessionData2);

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

        mockSession1.Setup(s => s.Run(It.IsAny<ExecutionData>())).Returns(sessionData1);
        mockSession2.Setup(s => s.Run(It.IsAny<ExecutionData>())).Returns(sessionData2);

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
}