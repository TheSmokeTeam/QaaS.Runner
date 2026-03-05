using System;
using System.Collections.Generic;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.MockerObjects.ConfigurationObjects.Command;
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
    public void Build_WithMissingCommandConfig_ReturnsNullAndAppendsFailure()
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
            .WithCommand(new CommandConfig());

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
            .WithCommand(new CommandConfig
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
    public void Build_WithSingleSupportedTypeAndMissingRedis_ReturnsNullAndAppendsFailure(CommandConfig commandConfig)
    {
        var builder = new MockerCommandBuilder()
            .Named("MockerCommandWithSingleType")
            .WithCommand(commandConfig)
            .WithServerName("test-server");

        var result = builder.Build(_context, _actionFailures, SessionName);

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures, Has.Count.EqualTo(1));
        Assert.That(_actionFailures[0].Reason.Message, Is.Not.Empty);
        Assert.That(_actionFailures[0].Reason.Message, Does.Not.Contain("Missing supported type"));
    }

    [Test]
    public void UpdateCommand_WithoutExistingCommand_ThrowsInvalidOperationException()
    {
        var builder = new MockerCommandBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.UpdateCommand(command =>
        {
            command.TriggerAction = new TriggerAction();
            return command;
        }));
    }

    [Test]
    public void CreateReadUpdateDeleteCommand_PerformsExpectedCrudFlow()
    {
        var builder = new MockerCommandBuilder();
        var initialCommand = new CommandConfig { TriggerAction = new TriggerAction() };

        builder.CreateCommand(initialCommand);
        Assert.That(builder.ReadCommand(), Is.SameAs(initialCommand));

        builder.UpdateCommand(command =>
        {
            command.TriggerAction = null;
            command.ChangeActionStub = new ChangeActionStub();
            return command;
        });

        Assert.That(builder.ReadCommand()!.ChangeActionStub, Is.Not.Null);
        Assert.That(builder.ReadCommand()!.TriggerAction, Is.Null);

        builder.DeleteCommand();
        Assert.That(builder.ReadCommand(), Is.Null);
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

    private static IEnumerable<CommandConfig> ValidSupportedCommands()
    {
        yield return new CommandConfig { ChangeActionStub = new ChangeActionStub() };
        yield return new CommandConfig { TriggerAction = new TriggerAction() };
        yield return new CommandConfig { Consume = new ConsumeConfig() };
    }
}
