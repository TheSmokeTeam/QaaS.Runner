using NUnit.Framework;
using QaaS.Framework.Protocols.ConfigurationObjects.Http;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using QaaS.Runner.Sessions.Session;
using QaaS.Runner.Sessions.Tests.Actions;

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

    private IProbe InitializeProbeHook()
    {
        return new TestProbe();
    }
}
