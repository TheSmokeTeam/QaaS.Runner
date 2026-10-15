using NUnit.Framework;
using Microsoft.Extensions.Configuration;
using QaaS.Runner;
using QaaS.Runner.Cases;
using QaaS.Runner.Sessions.Session.Builders;
using System.Collections.Generic;
using System.Linq;

namespace QaaS.Runner.Tests;

[TestFixture]
public class TestProfileTests
{
    private class SampleTestCase : TestCase<SampleTestCase>
    {
        public override void ModifyCaseExecutionBuilder(ExecutionBuilder baseBuilder, IConfiguration? variables = null)
        {
            var sessionName = variables?["customSession"] ?? "DefaultCaseSession";
            baseBuilder.AddSession(new SessionBuilder().Named(sessionName));
        }
    }

    private class SampleBaseCase : TestCase<SampleBaseCase>
    {
        public override void ModifyCaseExecutionBuilder(ExecutionBuilder baseBuilder, IConfiguration? variables = null)
        {
            baseBuilder.AddSession(new SessionBuilder().Named("BaseSession"));
        }
    }

    private class DummyProfile : TestProfile
    {
        public DummyProfile(string testEnv, bool addBaseCase = true)
            : base(testEnv, addBaseCase)
        {
        }

        protected override ITestCase? BaseCase => SampleBaseCase.Instance;

        protected override string[] VariablesYamlFiles => [];

        public void AddTest(ITestCase testCase)
        {
            TestCases.Add(testCase);
        }

        protected override void SetupBaseExecutionBuilder(ExecutionBuilder builder)
        {
            builder.AddSession(new SessionBuilder().Named("ProfileBaseSession"));
        }
    }

    [Test]
    public void TestProfile_Execute_WithCases_AppliesBaseAndClonesCases()
    {
        // Arrange
        var profile = new DummyProfile("local", addBaseCase: true);
        profile.AddTest(SampleTestCase.Instance);

        // Act
        profile.Execute();

        // Assert
        Assert.That(profile.Runner.ExecutionBuilders, Has.Count.EqualTo(1));
        var caseBuilder = profile.Runner.ExecutionBuilders[0];

        // Should have BaseSession (from BaseCase), ProfileBaseSession (from SetupBaseExecutionBuilder),
        // and DefaultCaseSession (from SampleTestCase)
        var sessionNames = caseBuilder.Sessions.Select(s => s.Name).ToList();
        Assert.That(sessionNames, Contains.Item("BaseSession"));
        Assert.That(sessionNames, Contains.Item("ProfileBaseSession"));
        Assert.That(sessionNames, Contains.Item("DefaultCaseSession"));
        Assert.That(caseBuilder.CaseName, Is.EqualTo("SampleTestCase"));
    }

    [Test]
    public void TestProfile_Execute_WithoutCases_RunsBaseExecutionBuilderDirectly()
    {
        // Arrange
        var profile = new DummyProfile("local", addBaseCase: false);

        // Act
        profile.Execute();

        // Assert
        Assert.That(profile.Runner.ExecutionBuilders, Has.Count.EqualTo(1));
        var caseBuilder = profile.Runner.ExecutionBuilders[0];

        var sessionNames = caseBuilder.Sessions.Select(s => s.Name).ToList();
        Assert.That(sessionNames, Contains.Item("ProfileBaseSession"));
        Assert.That(sessionNames, Does.Not.Contain("BaseSession"));
    }

    private class HardcodedTestCase : TestCase<HardcodedTestCase>
    {
        public override void ModifyCaseExecutionBuilder(ExecutionBuilder baseBuilder)
        {
            baseBuilder.AddSession(new SessionBuilder().Named("HardcodedSession"));
        }
    }

    [Test]
    public void TestProfile_Execute_WithHardcodedCaseWithoutVariables_Works()
    {
        // Arrange
        var profile = new DummyProfile("local", addBaseCase: false);
        profile.AddTest(HardcodedTestCase.Instance);

        // Act
        profile.Execute();

        // Assert
        Assert.That(profile.Runner.ExecutionBuilders, Has.Count.EqualTo(1));
        var caseBuilder = profile.Runner.ExecutionBuilders[0];
        var sessionNames = caseBuilder.Sessions.Select(s => s.Name).ToList();
        Assert.That(sessionNames, Contains.Item("HardcodedSession"));
        Assert.That(caseBuilder.CaseName, Is.EqualTo("HardcodedTestCase"));
    }

    private class DerivedFieldProfile : TestProfile
    {
        private readonly string _configuredModule;

        public DerivedFieldProfile(string moduleName) : base("local", addBaseCase: false)
        {
            _configuredModule = moduleName;
        }

        protected override string[] ProfileSpecificYamlFiles => [ _configuredModule ];
        protected override string[] VariablesYamlFiles => [];

        public string ReadConfiguredModule() => ProfileSpecificYamlFiles.FirstOrDefault() ?? "";
    }

    [Test]
    public void TestProfile_DeferredInitialization_DerivedConstructorFieldsAvailable()
    {
        // Act
        var profile = new DerivedFieldProfile("TestData/test.qaas.yaml");

        // Assert - EnsureInitialized runs after derived constructor body, so _configuredModule is available
        Assert.That(profile.ReadConfiguredModule(), Is.EqualTo("TestData/test.qaas.yaml"));
        Assert.That(profile.Runner, Is.Not.Null);
    }

    [Test]
    public void TestProfile_FilelessProfile_InitializesRunnableRunner()
    {
        // Arrange - DummyProfile supplies no YAMLs
        var profile = new DummyProfile("local", addBaseCase: false);

        // Act & Assert - verify Runner is initialized as a normal runnable runner
        Assert.That(profile.Runner, Is.Not.Null);
        Assert.That(profile.Runner.LastExitCode, Is.Null);
    }
}
