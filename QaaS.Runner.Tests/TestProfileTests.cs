using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.Executions;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner;
using QaaS.Runner.Cases;
using QaaS.Runner.Sessions.Session.Builders;

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
        private readonly bool _provideValidMetadata;

        public DummyProfile(string testEnv, bool addBaseCase = true, string customCommandKeyword = "", bool provideValidMetadata = true)
            : base(testEnv, addBaseCase, customCommandKeyword)
        {
            _provideValidMetadata = provideValidMetadata;
        }

        protected override ITestCase? BaseCase => SampleBaseCase.Instance;

        protected override string[] VariablesYamlFiles => [];

        public void AddTest(ITestCase testCase)
        {
            TestCases.Add(testCase);
        }

        protected override void SetupBaseExecutionBuilder(ExecutionBuilder builder)
        {
            if (_provideValidMetadata)
            {
                builder.WithMetadata(new MetaDataConfig { Team = "TestTeam", System = "TestSystem" });
            }
            builder.AddSession(new SessionBuilder().Named("ProfileBaseSession"));
        }

        public ExecutionBuilder GetBaseExecutionBuilder() => BaseExecutionBuilder;
    }

    [Test]
    public void TestProfile_Execute_WithCases_AppliesBaseAndClonesCasesAndSucceeds()
    {
        // Arrange
        var profile = new DummyProfile("local", addBaseCase: true);
        profile.AddTest(SampleTestCase.Instance);

        // Act
        var exitCode = profile.Execute();

        // Assert - execution should succeed with exit code 0
        Assert.That(exitCode, Is.Zero);
        Assert.That(profile.Runner.LastExitCode, Is.Zero);

        // Runner should contain the expanded case
        Assert.That(profile.Runner.ExecutionBuilders, Has.Count.EqualTo(1));
        var caseBuilder = profile.Runner.ExecutionBuilders[0];

        var sessionNames = caseBuilder.Sessions.Select(s => s.Name).ToList();
        Assert.That(sessionNames, Contains.Item("BaseSession"));
        Assert.That(sessionNames, Contains.Item("ProfileBaseSession"));
        Assert.That(sessionNames, Contains.Item("DefaultCaseSession"));
        Assert.That(caseBuilder.CaseName, Is.EqualTo("SampleTestCase"));
    }

    [Test]
    public void TestProfile_Execute_WithoutCases_RunsBaseExecutionBuilderDirectlyAndSucceeds()
    {
        // Arrange
        var profile = new DummyProfile("local", addBaseCase: false);

        // Act
        var exitCode = profile.Execute();

        // Assert
        Assert.That(exitCode, Is.Zero);
        Assert.That(profile.Runner.LastExitCode, Is.Zero);
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
    public void TestProfile_Execute_WithHardcodedCaseWithoutVariables_Succeeds()
    {
        // Arrange
        var profile = new DummyProfile("local", addBaseCase: false);
        profile.AddTest(HardcodedTestCase.Instance);

        // Act
        var exitCode = profile.Execute();

        // Assert
        Assert.That(exitCode, Is.Zero);
        Assert.That(profile.Runner.LastExitCode, Is.Zero);
        Assert.That(profile.Runner.ExecutionBuilders, Has.Count.EqualTo(1));

        var caseBuilder = profile.Runner.ExecutionBuilders[0];
        var sessionNames = caseBuilder.Sessions.Select(s => s.Name).ToList();
        Assert.That(sessionNames, Contains.Item("HardcodedSession"));
        Assert.That(caseBuilder.CaseName, Is.EqualTo("HardcodedTestCase"));
    }

    [Test]
    public void TestProfile_Execute_CalledTwice_ThrowsInvalidOperationException()
    {
        // Arrange
        var profile = new DummyProfile("local", addBaseCase: false);

        // Act & Assert
        profile.Execute();
        Assert.Throws<InvalidOperationException>(() => profile.Execute());
    }

    [Test]
    public void TestProfile_Execute_WithInvalidMetadata_PropagatesNonZeroExitCode()
    {
        // Arrange - provideValidMetadata: false leaves MetaData null, which fails configuration validation
        var profile = new DummyProfile("local", addBaseCase: false, provideValidMetadata: false);

        // Act
        var exitCode = profile.Execute();

        // Assert - validation failure must produce a non-zero exit code
        Assert.That(exitCode, Is.Not.Zero);
        Assert.That(profile.Runner.LastExitCode, Is.Not.Zero);
    }

    [Test]
    [TestCase("act", ExecutionType.Act)]
    [TestCase("assert", ExecutionType.Assert)]
    [TestCase("template", ExecutionType.Template)]
    [TestCase("run", ExecutionType.Run)]
    [TestCase("", ExecutionType.Run)]
    public void TestProfile_FilelessProfile_PreservesExecutionMode(string commandKeyword, ExecutionType expectedType)
    {
        // Arrange
        var profile = new DummyProfile("local", addBaseCase: false, customCommandKeyword: commandKeyword);

        // Act
        var baseBuilder = profile.GetBaseExecutionBuilder();

        // Assert
        Assert.That(baseBuilder.Type, Is.EqualTo(expectedType));
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

    private class InjectedStateProfile : TestProfile
    {
        public InjectedStateProfile(Runner injectedRunner, IConfiguration injectedVariables)
            : base("local", addBaseCase: false)
        {
            Runner = injectedRunner;
            Variables = injectedVariables;
        }

        protected override string[] VariablesYamlFiles => [];
    }

    [Test]
    public void TestProfile_DerivedConstructor_SuppliedStateNotOverwritten()
    {
        // Arrange
        var customRunner = Bootstrap.CreateDefaultRunner<Runner>();
        var customVariables = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["customKey"] = "customValue"
        }).Build();

        // Act
        var profile = new InjectedStateProfile(customRunner, customVariables);

        // Assert - EnsureInitialized must not overwrite caller-assigned dependencies
        Assert.That(profile.Runner, Is.SameAs(customRunner));
        Assert.That(profile.Variables["customKey"], Is.EqualTo("customValue"));
    }
}
