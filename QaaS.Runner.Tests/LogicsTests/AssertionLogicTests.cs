using System.Collections.Generic;
using System.Collections.Immutable;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Logics;

namespace QaaS.Runner.Tests.LogicsTests;

public class AssertionLogicTests
{
    [Test]
    [TestCase(ExecutionType.Assert, true)]
    [TestCase(ExecutionType.Run, true)]
    [TestCase(ExecutionType.Template, false)]
    [TestCase(ExecutionType.Act, false)]
    public void TestShouldRun_WithExecutionType_ReturnsExpectedBoolean(ExecutionType executionType, bool expected)
    {
        // Arrange
        var mockAssertions = new List<Assertion>();
        var context = new InternalContext();
        var assertionLogic = new AssertionLogic(mockAssertions, context);

        // Act & Assert
        Assert.That(assertionLogic.ShouldRun(executionType), Is.EqualTo(expected));
    }

    [Test]
    public void TestRun_WithAssertions_ReturnsExecutionDataWithAssertionResults()
    {
        // Arrange
        var mockAssertion1 = new Mock<Assertion>();
        var mockAssertion2 = new Mock<Assertion>();
        var mockResult1 = new AssertionResult
            { Assertion = mockAssertion1.Object, AssertionStatus = AssertionStatus.Passed };
        var mockResult2 = new AssertionResult
            { Assertion = mockAssertion2.Object, AssertionStatus = AssertionStatus.Skipped };

        mockAssertion1.Setup(a =>
                a.Execute(It.IsAny<IImmutableList<SessionData?>>(), It.IsAny<IImmutableList<DataSource>?>()))
            .Returns(mockResult1);
        mockAssertion2.Setup(a =>
                a.Execute(It.IsAny<IImmutableList<SessionData?>>(), It.IsAny<IImmutableList<DataSource>?>()))
            .Returns(mockResult2);

        var mockAssertions = new List<Assertion> { mockAssertion1.Object, mockAssertion2.Object };
        var assertionLogic = new AssertionLogic(mockAssertions, Globals.GetContextWithMetadata());
        var executionData = new ExecutionData();

        // Act
        var result = assertionLogic.Run(executionData);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
        Assert.That(executionData.AssertionResults.Count, Is.EqualTo(2));
        Assert.That(executionData.AssertionResults, Is.All.AnyOf(mockResult1, mockResult2));
    }
}