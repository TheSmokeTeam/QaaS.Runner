using System.Collections.Immutable;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Logics;
using QaaS.Runner.Storage;

namespace QaaS.Runner.Tests.LogicsTests;

public class StorageLogicTests
{
    [TestCase(ExecutionType.Assert, true)]
    [TestCase(ExecutionType.Run, false)]
    [TestCase(ExecutionType.Template, false)]
    [TestCase(ExecutionType.Act, true)]
    public void TestShouldRun_WithExecutionType_ReturnsExpectedBoolean(ExecutionType executionType, bool expected)
    {
        // Arrange
        var mockStorages = new List<IStorage>();
        var context = new InternalContext();
        var storageLogic = new StorageLogic(mockStorages, context, executionType);

        // Act & Assert
        Assert.That(storageLogic.ShouldRun(executionType), Is.EqualTo(expected));
    }

    [Test]
    public void TestRun_WithAssertExecutionType_RetrievesFromStorages()
    {
        // Arrange
        var mockStorage1 = new Mock<IStorage>();
        var mockStorage2 = new Mock<IStorage>();
        var mockSessionData1 = new SessionData { Name = "Session1" };
        var mockSessionData2 = new SessionData { Name = "Session2" };
        var mockSessionData3 = new SessionData { Name = "Session3" };
        var sessionDataList1 = new List<SessionData> { mockSessionData1, mockSessionData2 };
        var sessionDataList2 = new List<SessionData> { mockSessionData3 };

        mockStorage1.Setup(s => s.Retrieve(It.IsAny<string>())).Returns(sessionDataList1.ToImmutableList());
        mockStorage2.Setup(s => s.Retrieve(It.IsAny<string>())).Returns(sessionDataList2.ToImmutableList());

        var mockStorages = new List<IStorage> { mockStorage1.Object, mockStorage2.Object };
        var context = new InternalContext { CaseName = "TestCase", Logger = Globals.Logger };
        var storageLogic = new StorageLogic(mockStorages, context, ExecutionType.Assert);
        var executionData = new ExecutionData();

        // Act
        var result = storageLogic.Run(executionData);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
        mockStorage1.Verify(s => s.Retrieve(context.CaseName), Times.Once());
        mockStorage2.Verify(s => s.Retrieve(context.CaseName), Times.Once());
        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(3));
    }

    [Test]
    public void TestRun_WithActExecutionType_StoresToStorages()
    {
        // Arrange
        var mockStorage1 = new Mock<IStorage>();
        var mockStorage2 = new Mock<IStorage>();
        var sessionData1 = new SessionData();
        var sessionData2 = new SessionData();

        var sessionDataList = new List<SessionData> { sessionData1, sessionData2 };
        var immutableSessionData = sessionDataList.ToImmutableList();

        var mockStorages = new List<IStorage> { mockStorage1.Object, mockStorage2.Object };
        var context = new InternalContext { CaseName = "TestCase", Logger = Globals.Logger };
        var storageLogic = new StorageLogic(mockStorages, context, ExecutionType.Act);
        var executionData = new ExecutionData();
        executionData.SessionDatas.AddRange(sessionDataList);

        // Act
        var result = storageLogic.Run(executionData);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
        mockStorage1.Verify(s => s.Store(immutableSessionData!, context.CaseName), Times.Once());
        mockStorage2.Verify(s => s.Store(immutableSessionData!, context.CaseName), Times.Once());
    }

    [Test]
    public void TestRun_WithAssertExecutionType_DuplicateSessionNamesHandled()
    {
        // Arrange
        var mockStorage1 = new Mock<IStorage>();
        var sessionData1 = new SessionData { Name = "DuplicateSession" };
        var sessionData2 = new SessionData { Name = "DuplicateSession" };

        var sessionDataList = new List<SessionData> { sessionData1 };
        mockStorage1.Setup(s => s.Retrieve(It.IsAny<string>())).Returns(sessionDataList.ToImmutableList());

        var mockStorages = new List<IStorage> { mockStorage1.Object };
        var context = new InternalContext { CaseName = "TestCase", Logger = Globals.Logger };
        var storageLogic = new StorageLogic(mockStorages, context, ExecutionType.Assert);
        var executionData = new ExecutionData();
        executionData.SessionDatas.Add(sessionData2); // Add duplicate session

        // Act
        var result = storageLogic.Run(executionData);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
        Assert.That(executionData.SessionDatas, Has.Count.EqualTo(1)); // Should only have one session
    }
}