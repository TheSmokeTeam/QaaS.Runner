using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    public void Run_EmitsStructuredAssertionIdentityAndStatus()
    {
        var logger = new Mock<ILogger>();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var context = new InternalContext
        {
            Logger = logger.Object,
            RootConfiguration = new ConfigurationBuilder().Build(),
        };
        var assertion = new Mock<Assertion>();
        assertion.Object.Name = "response-is-correct";
        assertion.Object.AssertionName = "CustomAssertion";
        var outcome = new AssertionResult
        {
            Assertion = assertion.Object,
            AssertionStatus = AssertionStatus.Failed,
            TestDurationMs = 0,
            Flaky = null,
        };
        assertion
            .Setup(value =>
                value.Execute(
                    It.IsAny<IImmutableList<SessionData?>>(),
                    It.IsAny<IImmutableList<DataSource>?>()
                )
            )
            .Returns(outcome);

        var execution = new AssertionLogic([assertion.Object], context).Run(new ExecutionData());
        var completion = logger
            .Invocations.Where(call => call.Method.Name == "Log")
            .Select(call => call.Arguments[2])
            .OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .Single(fields => fields.Any(field => field.Key == "DiagnosticCode"));
        var properties = completion.ToDictionary(field => field.Key, field => field.Value);

        Assert.Multiple(() =>
        {
            Assert.That(properties["DiagnosticCode"], Is.EqualTo("QAAS_ASSERTION_COMPLETED"));
            Assert.That(properties["AssertionName"], Is.EqualTo("response-is-correct"));
            Assert.That(properties["AssertionType"], Is.EqualTo("CustomAssertion"));
            Assert.That(properties["AssertionStatus"], Is.EqualTo("Failed"));
            Assert.That(execution.AssertionResults.Single(), Is.SameAs(outcome));
        });
    }

    [Test]
    public void TestRun_WithAssertions_ReturnsExecutionDataWithAssertionResults()
    {
        // Arrange
        var mockAssertion1 = new Mock<Assertion>();
        var mockAssertion2 = new Mock<Assertion>();
        var mockResult1 = new AssertionResult
        {
            Assertion = mockAssertion1.Object,
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null,
        };
        var mockResult2 = new AssertionResult
        {
            Assertion = mockAssertion2.Object,
            AssertionStatus = AssertionStatus.Skipped,
            TestDurationMs = 0,
            Flaky = null,
        };

        mockAssertion1
            .Setup(a =>
                a.Execute(
                    It.IsAny<IImmutableList<SessionData?>>(),
                    It.IsAny<IImmutableList<DataSource>?>()
                )
            )
            .Returns(mockResult1);
        mockAssertion2
            .Setup(a =>
                a.Execute(
                    It.IsAny<IImmutableList<SessionData?>>(),
                    It.IsAny<IImmutableList<DataSource>?>()
                )
            )
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
