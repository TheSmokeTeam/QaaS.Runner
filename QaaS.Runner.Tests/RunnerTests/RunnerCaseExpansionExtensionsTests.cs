using NUnit.Framework;
using QaaS.Runner;
using QaaS.Runner.Cases;
using QaaS.Runner.Sessions.Session.Builders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QaaS.Runner.Tests.RunnerTests;

[TestFixture]
public class RunnerCaseExpansionExtensionsTests
{
    private class DummyTestCase : TestCase
    {
        private readonly string _sessionName;

        public DummyTestCase(string sessionName)
        {
            _sessionName = sessionName;
        }

        public override void SetupExecutionBuilder(ExecutionBuilder builder)
        {
            builder.AddSession(new SessionBuilder().Named(_sessionName));
        }
    }

    [Test]
    public void ExtractBaseBuilder_WithExistingBuilders_ExtractsFirstAndRemovesIt()
    {
        // Arrange
        var runner = Bootstrap.New(["run", "TestData/test.qaas.yaml", "--no-process-exit"]);
        Assert.That(runner.ExecutionBuilders, Has.Count.EqualTo(1));
        var originalBuilder = runner.ExecutionBuilders[0];

        // Act
        var baseBuilder = runner.ExtractBaseBuilder();

        // Assert
        Assert.That(runner.ExecutionBuilders, Is.Empty);
        Assert.That(baseBuilder, Is.SameAs(originalBuilder));
    }

    [Test]
    public void ExtractBaseBuilder_WithNoBuilders_ReturnsNewBuilder()
    {
        // Arrange
        var runner = Bootstrap.New(["run", "TestData/test.qaas.yaml", "--no-process-exit"]);
        runner.ExecutionBuilders.Clear();

        // Act
        var baseBuilder = runner.ExtractBaseBuilder();

        // Assert
        Assert.That(baseBuilder, Is.Not.Null);
        Assert.That(runner.ExecutionBuilders, Is.Empty);
    }

    [Test]
    public void ExtractBaseBuilder_WithSetupAction_AppliesAction()
    {
        // Arrange
        var runner = Bootstrap.New(["run", "TestData/test.qaas.yaml", "--no-process-exit"]);
        
        // Act
        var baseBuilder = runner.ExtractBaseBuilder(builder =>
        {
            builder.AddSession(new SessionBuilder().Named("GlobalBaseSession"));
        });

        // Assert
        Assert.That(baseBuilder.Sessions.Any(s => s.Name == "GlobalBaseSession"), Is.True);
    }

    [Test]
    public void AddTestCases_WithNoCases_AppendsBaseBuilderBack()
    {
        // Arrange
        var runner = Bootstrap.New(["run", "TestData/test.qaas.yaml", "--no-process-exit"]);
        var baseBuilder = runner.ExtractBaseBuilder();
        Assert.That(runner.ExecutionBuilders, Is.Empty);

        // Act
        runner.AddTestCases(baseBuilder);

        // Assert
        Assert.That(runner.ExecutionBuilders, Has.Count.EqualTo(1));
        Assert.That(runner.ExecutionBuilders[0], Is.SameAs(baseBuilder));
    }

    [Test]
    public void AddTestCases_WithMultipleCases_ClonesAndConfiguresEach()
    {
        // Arrange
        var runner = Bootstrap.New(["run", "TestData/test.qaas.yaml", "--no-process-exit"]);
        var baseBuilder = runner.ExtractBaseBuilder();
        Assert.That(runner.ExecutionBuilders, Is.Empty);

        // Act
        runner.AddTestCases(baseBuilder, 
            new DummyTestCase("Case1Session"),
            new DummyTestCase("Case2Session")
        );

        // Assert
        Assert.That(runner.ExecutionBuilders, Has.Count.EqualTo(2));
        
        var firstClone = runner.ExecutionBuilders[0];
        var secondClone = runner.ExecutionBuilders[1];

        Assert.That(firstClone, Is.Not.SameAs(baseBuilder));
        Assert.That(secondClone, Is.Not.SameAs(baseBuilder));
        Assert.That(firstClone, Is.Not.SameAs(secondClone));

        Assert.That(firstClone.Sessions.Any(s => s.Name == "Case1Session"), Is.True);
        Assert.That(firstClone.Sessions.Any(s => s.Name == "Case2Session"), Is.False);

        Assert.That(secondClone.Sessions.Any(s => s.Name == "Case2Session"), Is.True);
        Assert.That(secondClone.Sessions.Any(s => s.Name == "Case1Session"), Is.False);
        
        // Ensure base builder is unmodified (remains clean)
        Assert.That(baseBuilder.Sessions.Any(s => s.Name == "Case1Session" || s.Name == "Case2Session"), Is.False);
    }
}
