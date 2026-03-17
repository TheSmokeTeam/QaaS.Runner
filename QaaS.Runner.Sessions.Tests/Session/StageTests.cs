using System.Collections.Concurrent;
using NUnit.Framework;
using QaaS.Framework.Protocols.ConfigurationObjects.Http;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using QaaS.Runner.Sessions.Session;
using QaaS.Runner.Sessions.Tests.Actions;
using QaaS.Runner.Sessions.Tests.Actions.Utils;

namespace QaaS.Runner.Sessions.Tests.Session;

[TestFixture]
public class StageTests
{
    private readonly string _sessionName = "TestSession";
    private Stage _stage = null!;
    private InternalContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _context = Globals.GetContextWithMetadata();
        _stage = new Stage(_context, [], _sessionName, 0);
    }

    [Test]
    public void TestExportRCD_ValidExportRcdParams_WillLoadTheRcdToTheContextDict()
    {
        _context.InternalRunningSessions.RunningSessionsDict[_sessionName] =
            new RunningSessionData<object, object> { Inputs = [], Outputs = [] };

        _stage!.AddCommunication(
            new PublisherBuilder().Configure(new KafkaTopicSenderConfig
                {
                    TopicName = "test", Username = "testUser", Password = "SHHHHHH", HostNames = ["h1-prod", "h2-test"]
                })
                .Build(_context!, [], _sessionName)!);
        _stage.AddCommunication(
            new ConsumerBuilder().WithTimeout(1000).Configure(new KafkaTopicReaderConfig
                {
                    TopicName = "test", Username = "testUser", Password = "SHHHHHH", HostNames = ["h1-prod", "h2-test"],
                    GroupId = "1"
                })
                .Build(_context!, [], _sessionName)!);
        _stage.AddCommunication(
            new TransactionBuilder().WithTimeout(1000)
                .Configure(new HttpTransactorConfig { BaseAddress = "http://test", Method = HttpMethods.Get })
                .Build(_context!, [], _sessionName)!);
        _stage.AddCommunication(
            new ProbeBuilder().Named("testProbe").Build(_context!,
                [new(ProbeBuilder.BuildScopedHookName(_sessionName, "testProbe"), InitializeProbeHook())], [],
                _sessionName)!);
        _stage.ExportRunningCommunicationData();

        const int exportedNumOfInputRcd = 2;
        const int exportedNumOfOutputRcd = 2;

        Assert.That(
            _context.InternalRunningSessions.RunningSessionsDict[_sessionName].Inputs!.Count,
            Is.EqualTo(exportedNumOfInputRcd),
            "Test Failed: the number of input rcd that were loaded was not 2!");

        Assert.That(
            _context.InternalRunningSessions.RunningSessionsDict[_sessionName].Outputs!.Count,
            Is.EqualTo(exportedNumOfOutputRcd),
            "Test Failed: the number of output rcd that were loaded was not 2!");
    }

    [Test]
    public void PrepareActions_WithUnsupportedAction_DoesNotThrow()
    {
        _stage.AddCommunication(new NoOpStagedAction("noop", 0));

        Assert.DoesNotThrow(() => _stage.PrepareActions([], []));
    }

    [Test]
    public async Task RunAsync_WithSleepBeforeAndAfter_AppendsStageLogsAndCompletesTasks()
    {
        var context = CreationalFunctions.CreateContext(_sessionName, []);
        var stage = new Stage(context, new ConcurrentBag<ActionFailure>(), _sessionName, 0, 1, 1);
        stage.AddCommunication(new NoOpStagedAction("noop", 0));

        var tasks = await stage.RunAsync();
        await Task.WhenAll(tasks);

        var sessionLog = context.GetSessionLog(_sessionName);
        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(sessionLog, Does.Contain("Starting action stage 0 for session TestSession with 1 action(s)"));
        Assert.That(sessionLog, Does.Contain("Finished action stage 0 for session TestSession"));
    }

    private IProbe InitializeProbeHook()
    {
        return new TestProbe();
    }

    private sealed class NoOpStagedAction(string name, int stage) : StagedAction(name, stage, null, Globals.Logger)
    {
        internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
        {
        }

        internal override InternalCommunicationData<object> Act()
        {
            return new InternalCommunicationData<object>
            {
                Output = [new DetailedData<object> { Body = "ok" }],
                OutputSerializationType = SerializationType.Json
            };
        }

        protected internal override void LogData(InternalCommunicationData<object> actData,
            DetailedData<object> itemBeforeSerialization, InputOutputState? saveAt = null)
        {
        }
    }
}
