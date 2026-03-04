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

    private static IEnumerable<CommandConfig> ValidSupportedCommands()
    {
        yield return new CommandConfig { ChangeActionStub = new ChangeActionStub() };
        yield return new CommandConfig { TriggerAction = new TriggerAction() };
        yield return new CommandConfig { Consume = new ConsumeConfig() };
    }
}
