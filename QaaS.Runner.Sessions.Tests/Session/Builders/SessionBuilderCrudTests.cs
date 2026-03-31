using System.Linq;
using System.Reflection;
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
    public void CrudOperations_WhenCollectionsAreNull_TreatCollectionsAsEmptyUntilItemsAreAdded()
    {
        var builder = new SessionBuilder();
        SetActionCollections(builder, null);

        Assert.Multiple(() =>
        {
            Assert.That(builder.ReadConsumers(), Is.Empty);
            Assert.That(builder.ReadPublishers(), Is.Empty);
            Assert.That(builder.ReadTransactions(), Is.Empty);
            Assert.That(builder.ReadProbes(), Is.Empty);
            Assert.That(builder.ReadCollectors(), Is.Empty);
            Assert.That(builder.ReadMockerCommands(), Is.Empty);
            Assert.That(builder.ReadConsumer("missing"), Is.Null);
            Assert.That(builder.ReadPublisher("missing"), Is.Null);
            Assert.That(builder.ReadTransaction("missing"), Is.Null);
            Assert.That(builder.ReadProbe("missing"), Is.Null);
            Assert.That(builder.ReadCollector("missing"), Is.Null);
            Assert.That(builder.ReadMockerCommand("missing"), Is.Null);
        });

        Assert.DoesNotThrow(() =>
        {
            builder.DeleteConsumer("missing")
                .DeletePublisher("missing")
                .DeleteTransaction("missing")
                .DeleteProbe("missing")
                .DeleteCollector("missing")
                .DeleteMockerCommand("missing");
        });

        builder.CreateConsumer(new ConsumerBuilder().Named("consumer-a"))
            .CreatePublisher(new PublisherBuilder().Named("publisher-a"))
            .CreateTransaction(new TransactionBuilder().Named("transaction-a"))
            .CreateProbe(new ProbeBuilder().Named("probe-a"))
            .CreateCollector(new CollectorBuilder().Named("collector-a"))
            .CreateMockerCommand(new MockerCommandBuilder().Named("command-a"));

        Assert.Multiple(() =>
        {
            Assert.That(builder.ReadConsumers(), Has.Count.EqualTo(1));
            Assert.That(builder.ReadPublishers(), Has.Count.EqualTo(1));
            Assert.That(builder.ReadTransactions(), Has.Count.EqualTo(1));
            Assert.That(builder.ReadProbes(), Has.Count.EqualTo(1));
            Assert.That(builder.ReadCollectors(), Has.Count.EqualTo(1));
            Assert.That(builder.ReadMockerCommands(), Has.Count.EqualTo(1));
        });
    }

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
        Assert.That(builder.ReadConsumer("consumer-updated")?.Name, Is.EqualTo("consumer-updated"));
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
        Assert.That(builder.ReadPublisher("publisher-updated")?.Name, Is.EqualTo("publisher-updated"));
        Assert.That(builder.ReadTransaction("transaction-updated")?.Name, Is.EqualTo("transaction-updated"));
        Assert.That(builder.ReadProbe("probe-updated")?.Name, Is.EqualTo("probe-updated"));

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
        Assert.That(builder.ReadCollector("collector-updated")?.Name, Is.EqualTo("collector-updated"));
        Assert.That(builder.ReadMockerCommand("command-updated")?.Name, Is.EqualTo("command-updated"));

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
        Assert.That(builder.ReadStage(1)?.TimeoutBefore, Is.EqualTo(99));

        builder.DeleteStage(2);
        Assert.That(builder.ReadStages(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadStages()[0].StageNumber, Is.EqualTo(1));
    }

    [Test]
    public void UpdateByName_WithMutationDelegate_ShouldAllowEditingExistingBuildersInPlace()
    {
        var builder = new SessionBuilder()
            .CreateConsumer(new ConsumerBuilder().Named("consumer-a"))
            .CreatePublisher(new PublisherBuilder().Named("publisher-a"))
            .CreateTransaction(new TransactionBuilder().Named("transaction-a"))
            .CreateProbe(new ProbeBuilder().Named("probe-a"))
            .CreateCollector(new CollectorBuilder().Named("collector-a"))
            .CreateMockerCommand(new MockerCommandBuilder().Named("command-a"));

        builder.UpdateConsumer("consumer-a", consumer => consumer.Named("consumer-mutated"));
        builder.UpdatePublisher("publisher-a", publisher => publisher.Named("publisher-mutated"));
        builder.UpdateTransaction("transaction-a", transaction => transaction.Named("transaction-mutated"));
        builder.UpdateProbe("probe-a", probe => probe.Named("probe-mutated"));
        builder.UpdateCollector("collector-a", collector => collector.Named("collector-mutated"));
        builder.UpdateMockerCommand("command-a", command => command.Named("command-mutated"));

        Assert.That(builder.ReadConsumer("consumer-mutated"), Is.Not.Null);
        Assert.That(builder.ReadPublisher("publisher-mutated"), Is.Not.Null);
        Assert.That(builder.ReadTransaction("transaction-mutated"), Is.Not.Null);
        Assert.That(builder.ReadProbe("probe-mutated"), Is.Not.Null);
        Assert.That(builder.ReadCollector("collector-mutated"), Is.Not.Null);
        Assert.That(builder.ReadMockerCommand("command-mutated"), Is.Not.Null);
    }

    [Test]
    public void UpdateStage_WhenStageNumberDoesNotExist_DoesNotChangeStages()
    {
        var builder = new SessionBuilder()
            .CreateStage(new StageConfig(stageNumber: 1, timeoutBefore: 10, timeoutAfter: 20));

        builder.UpdateStage(99, new StageConfig(stageNumber: 99, timeoutBefore: 1, timeoutAfter: 1));

        Assert.That(builder.ReadStages(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadStages()[0].StageNumber, Is.EqualTo(1));
    }

    [Test]
    public void UpdateByName_WhenCollectionIsNull_DoesNotThrowAndKeepsCollectionNull()
    {
        var builder = new SessionBuilder();
        typeof(SessionBuilder).GetProperty("Consumers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(builder, null);

        Assert.DoesNotThrow(() => builder.UpdateConsumer("missing", new ConsumerBuilder().Named("replacement")));
        Assert.That(typeof(SessionBuilder).GetProperty("Consumers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(builder), Is.Null);
    }

    [Test]
    public void UpdateByName_WhenNameIsMissing_DoesNotMutateCollection()
    {
        var builder = new SessionBuilder()
            .CreatePublisher(new PublisherBuilder().Named("publisher-a"));

        builder.UpdatePublisher("publisher-missing", new PublisherBuilder().Named("publisher-updated"));

        Assert.That(builder.ReadPublishers(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadPublishers()[0].Name, Is.EqualTo("publisher-a"));
    }

    private static void SetActionCollections(SessionBuilder builder, object? value)
    {
        foreach (var propertyName in new[]
                 {
                     "Consumers",
                     "Publishers",
                     "Transactions",
                     "Probes",
                     "Collectors",
                     "MockerCommands"
                 })
        {
            typeof(SessionBuilder).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(builder, value);
        }
    }
}

