using System.Linq;
using NUnit.Framework;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using QaaS.Runner.Sessions.Session.Builders;

namespace QaaS.Runner.Sessions.Tests.Session.Builders;

[TestFixture]
public class SessionBuilderCrudTests
{
    [Test]
    public void Consumers_ShouldSupportCrudByName()
    {
        var builder = new SessionBuilder()
            .CreateConsumer(new ConsumerBuilder().Named("consumer-a"))
            .CreateConsumer(new ConsumerBuilder().Named("consumer-b"));

        builder.UpdateConsumer("consumer-a", new ConsumerBuilder().Named("consumer-updated"));
        builder.DeleteConsumer("consumer-b");

        Assert.That(builder.ReadConsumers(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadConsumers()[0].Name, Is.EqualTo("consumer-updated"));
    }

    [Test]
    public void PublishersTransactionsAndProbes_ShouldSupportCrudByName()
    {
        var builder = new SessionBuilder()
            .CreatePublisher(new PublisherBuilder().Named("publisher-a"))
            .CreateTransaction(new TransactionBuilder().Named("transaction-a"))
            .CreateProbe(new ProbeBuilder().Named("probe-a"));

        builder.UpdatePublisher("publisher-a", new PublisherBuilder().Named("publisher-updated"));
        builder.UpdateTransaction("transaction-a", new TransactionBuilder().Named("transaction-updated"));
        builder.UpdateProbe("probe-a", new ProbeBuilder().Named("probe-updated"));

        Assert.That(builder.ReadPublishers()[0].Name, Is.EqualTo("publisher-updated"));
        Assert.That(builder.ReadTransactions()[0].Name, Is.EqualTo("transaction-updated"));
        Assert.That(builder.ReadProbes()[0].Name, Is.EqualTo("probe-updated"));

        builder.DeletePublisher("publisher-updated")
            .DeleteTransaction("transaction-updated")
            .DeleteProbe("probe-updated");

        Assert.That(builder.ReadPublishers(), Is.Empty);
        Assert.That(builder.ReadTransactions(), Is.Empty);
        Assert.That(builder.ReadProbes(), Is.Empty);
    }

    [Test]
    public void CollectorsAndMockerCommands_ShouldSupportCrudByName()
    {
        var builder = new SessionBuilder()
            .CreateCollector(new CollectorBuilder().Named("collector-a"))
            .CreateMockerCommand(new MockerCommandBuilder().Named("command-a"));

        builder.UpdateCollector("collector-a", new CollectorBuilder().Named("collector-updated"));
        builder.UpdateMockerCommand("command-a", new MockerCommandBuilder().Named("command-updated"));

        Assert.That(builder.ReadCollectors()[0].Name, Is.EqualTo("collector-updated"));
        Assert.That(builder.ReadMockerCommands()[0].Name, Is.EqualTo("command-updated"));

        builder.DeleteCollector("collector-updated")
            .DeleteMockerCommand("command-updated");

        Assert.That(builder.ReadCollectors(), Is.Empty);
        Assert.That(builder.ReadMockerCommands(), Is.Empty);
    }

    [Test]
    public void Stages_ShouldSupportCrudByStageNumber()
    {
        var builder = new SessionBuilder()
            .CreateStage(new StageConfig(stageNumber: 1, timeoutBefore: 10, timeoutAfter: 20))
            .CreateStage(new StageConfig(stageNumber: 2, timeoutBefore: 30, timeoutAfter: 40));

        builder.UpdateStage(1, new StageConfig(stageNumber: 1, timeoutBefore: 99, timeoutAfter: 100));

        Assert.That(builder.ReadStages(), Has.Count.EqualTo(2));
        Assert.That(builder.ReadStages().First(stage => stage.StageNumber == 1).TimeoutBefore, Is.EqualTo(99));

        builder.DeleteStage(2);
        Assert.That(builder.ReadStages(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadStages()[0].StageNumber, Is.EqualTo(1));
    }
}
