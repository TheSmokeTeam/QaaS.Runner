using System;
using System.Collections.Generic;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.Serialization;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Tests.Actions.Utils;

namespace QaaS.Runner.Sessions.Tests.Actions.MockerCommands;

[TestFixture]
public class MockerCommandBuilderTests
{
    private const string SessionName = "TestSession";
    private IList<ActionFailure> _actionFailures = null!;
    private InternalContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _context = CreationalFunctions.CreateContext(SessionName, []);
        _actionFailures = new List<ActionFailure>();
    }

    [Test]
    public void Build_WithMissingCommandConfiguration_ReturnsNullAndAppendsFailure()
    {
        var builder = new MockerCommandBuilder()
            .Named("MockerCommandWithoutConfig");

        var result = builder.Build(_context, _actionFailures, SessionName);

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures, Has.Count.EqualTo(1));
        Assert.That(_actionFailures[0].Reason.Message, Does.Contain("Missing command configuration"));
    }

    [Test]
    public void Build_WithNoSupportedType_ReturnsNullAndAppendsFailure()
    {
        var builder = new MockerCommandBuilder()
            .Named("MockerCommandWithoutType")
            .Configure(new MockerCommandConfig());

        var result = builder.Build(_context, _actionFailures, SessionName);

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures, Has.Count.EqualTo(1));
        Assert.That(_actionFailures[0].Reason.Message, Does.Contain("Missing supported type"));
    }

    [Test]
    public void Build_WithMultipleSupportedTypes_ReturnsNullAndAppendsFailure()
    {
        var builder = new MockerCommandBuilder()
            .Named("MockerCommandWithConflicts")
            .Configure(new MockerCommandConfig
            {
                ChangeActionStub = new ChangeActionStub(),
                TriggerAction = new TriggerAction()
            });

        var result = builder.Build(_context, _actionFailures, SessionName);

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures, Has.Count.EqualTo(1));
        Assert.That(_actionFailures[0].Reason.Message, Does.Contain("Multiple configurations provided"));
    }

    [TestCaseSource(nameof(ValidSupportedCommands))]
    public void Build_WithSingleSupportedTypeAndMissingRedis_ReturnsNullAndAppendsFailure(MockerCommandConfig commandConfig)
    {
        var builder = new MockerCommandBuilder()
            .Named("MockerCommandWithSingleType")
            .Configure(commandConfig)
            .WithServerName("test-server");

        var result = builder.Build(_context, _actionFailures, SessionName);

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures, Has.Count.EqualTo(1));
        Assert.That(_actionFailures[0].Reason.Message, Is.Not.Empty);
        Assert.That(_actionFailures[0].Reason.Message, Does.Not.Contain("Missing supported type"));
    }

    [Test]
    public void UpdateConfiguration_WithoutExistingConfiguration_ThrowsInvalidOperationException()
    {
        var builder = new MockerCommandBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.UpdateConfiguration(command =>
        {
            command.TriggerAction = new TriggerAction();
            return command;
        }));
    }

    [Test]
    public void UpdateConfiguration_WithConfigurationWithoutExistingConfiguration_ThrowsInvalidOperationException()
    {
        var builder = new MockerCommandBuilder();

        Assert.Throws<InvalidOperationException>(() =>
            builder.UpdateConfiguration(new MockerCommandConfig { TriggerAction = new TriggerAction() }));
    }

    [Test]
    public void CreateReadUpdateConfiguration_PerformsExpectedCrudFlow()
    {
        var builder = new MockerCommandBuilder();
        var initialCommand = new MockerCommandConfig { TriggerAction = new TriggerAction() };

        builder.Configure(initialCommand);
        Assert.That(builder.Configuration, Is.SameAs(initialCommand));

        builder.UpdateConfiguration(command =>
        {
            command.TriggerAction = null;
            command.ChangeActionStub = new ChangeActionStub();
            return command;
        });

        Assert.That(builder.Configuration!.ChangeActionStub, Is.Not.Null);
        Assert.That(builder.Configuration!.TriggerAction, Is.Null);

        builder.Configure(new MockerCommandConfig
        {
            TriggerAction = new TriggerAction()
        });
        Assert.That(builder.Configuration, Is.Not.Null);
        Assert.That(builder.Configuration!.TriggerAction, Is.Not.Null);
    }

    [Test]
    public void UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new MockerCommandBuilder()
            .Configure(new MockerCommandConfig
            {
                Consume = new ConsumeCommandConfig
                {
                    InputDeserialize = new DeserializeConfig
                    {
                        Deserializer = SerializationType.Json
                    }
                }
            });

        builder.UpdateConfiguration(new MockerCommandConfig
        {
            Consume = new ConsumeCommandConfig
            {
                OutputDeserialize = new DeserializeConfig
                {
                    Deserializer = SerializationType.Binary
                }
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(builder.Configuration!.Consume, Is.Not.Null);
            Assert.That(builder.Configuration!.Consume!.InputDeserialize!.Deserializer, Is.EqualTo(SerializationType.Json));
            Assert.That(builder.Configuration!.Consume!.OutputDeserialize!.Deserializer, Is.EqualTo(SerializationType.Binary));
        });
    }

    [Test]
    public void FluentSetters_AssignExpectedProperties()
    {
        var redis = new RedisConfig { Host = "localhost:6379" };
        var builder = new MockerCommandBuilder()
            .Named("mocker")
            .AtStage(7)
            .WithServerName("server-a")
            .WithRedis(redis)
            .WithRequestDurationMs(123)
            .WithRequestRetries(9);

        Assert.That(builder.Name, Is.EqualTo("mocker"));
        Assert.That(builder.Stage, Is.EqualTo(7));
        Assert.That(builder.ServerName, Is.EqualTo("server-a"));
        Assert.That(builder.Redis, Is.SameAs(redis));
        Assert.That(builder.RequestDurationMs, Is.EqualTo(123));
        Assert.That(builder.RequestRetries, Is.EqualTo(9));
    }

    private static IEnumerable<MockerCommandConfig> ValidSupportedCommands()
    {
        yield return new MockerCommandConfig { ChangeActionStub = new ChangeActionStub() };
        yield return new MockerCommandConfig { TriggerAction = new TriggerAction() };
        yield return new MockerCommandConfig { Consume = new ConsumeCommandConfig() };
    }
}


