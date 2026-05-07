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
            Assert.That(builder.Consumers, Is.Null);
            Assert.That(builder.Publishers, Is.Null);
            Assert.That(builder.Transactions, Is.Null);
            Assert.That(builder.Probes, Is.Null);
            Assert.That(builder.Collectors, Is.Null);
            Assert.That(builder.MockerCommands, Is.Null);
        });

        Assert.DoesNotThrow(() =>
        {
            builder.RemoveConsumer("missing")
                .RemovePublisher("missing")
                .RemoveTransaction("missing")
                .RemoveProbe("missing")
                .RemoveCollector("missing")
                .RemoveMockerCommand("missing");
        });

        builder.AddConsumer(new ConsumerBuilder().Named("consumer-a"))
            .AddPublisher(new PublisherBuilder().Named("publisher-a"))
            .AddTransaction(new TransactionBuilder().Named("transaction-a"))
            .AddProbe(new ProbeBuilder().Named("probe-a"))
            .AddCollector(new CollectorBuilder().Named("collector-a"))
            .AddMockerCommand(new MockerCommandBuilder().Named("command-a"));

        Assert.Multiple(() =>
        {
            Assert.That(builder.Consumers, Has.Length.EqualTo(1));
            Assert.That(builder.Publishers, Has.Length.EqualTo(1));
            Assert.That(builder.Transactions, Has.Length.EqualTo(1));
            Assert.That(builder.Probes, Has.Length.EqualTo(1));
            Assert.That(builder.Collectors, Has.Length.EqualTo(1));
            Assert.That(builder.MockerCommands, Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void Consumers_ShouldSupportCrudByName()
    {
        var builder = new SessionBuilder()
            .AddConsumer(new ConsumerBuilder().Named("consumer-a"))
            .AddConsumer(new ConsumerBuilder().Named("consumer-b"));

        builder.UpdateConsumer("consumer-a", new ConsumerBuilder().Named("consumer-updated"));
        builder.RemoveConsumer("consumer-b");

        Assert.That(builder.Consumers, Has.Length.EqualTo(1));
        Assert.That(builder.Consumers![0].Name, Is.EqualTo("consumer-updated"));
        Assert.That(builder.Consumers.FirstOrDefault(consumer => consumer.Name == "consumer-updated")?.Name,
            Is.EqualTo("consumer-updated"));
    }

    [Test]
    public void ActionCollections_ShouldSupportRemoveAtByIndex()
    {
        var builder = new SessionBuilder()
            .AddConsumer(new ConsumerBuilder().Named("consumer-a"))
            .AddConsumer(new ConsumerBuilder().Named("consumer-b"))
            .AddPublisher(new PublisherBuilder().Named("publisher-a"))
            .AddPublisher(new PublisherBuilder().Named("publisher-b"))
            .AddTransaction(new TransactionBuilder().Named("transaction-a"))
            .AddTransaction(new TransactionBuilder().Named("transaction-b"))
            .AddProbe(new ProbeBuilder().Named("probe-a"))
            .AddProbe(new ProbeBuilder().Named("probe-b"))
            .AddCollector(new CollectorBuilder().Named("collector-a"))
            .AddCollector(new CollectorBuilder().Named("collector-b"))
            .AddMockerCommand(new MockerCommandBuilder().Named("command-a"))
            .AddMockerCommand(new MockerCommandBuilder().Named("command-b"))
            .AddStage(new StageConfig(stageNumber: 1, timeoutBefore: 10, timeoutAfter: 20))
            .AddStage(new StageConfig(stageNumber: 2, timeoutBefore: 30, timeoutAfter: 40));

        builder.RemoveConsumerAt(0)
            .RemovePublisherAt(0)
            .RemoveTransactionAt(0)
            .RemoveProbeAt(0)
            .RemoveCollectorAt(0)
            .RemoveMockerCommandAt(0)
            .RemoveStageAt(0);

        Assert.Multiple(() =>
        {
            Assert.That(builder.Consumers!.Single().Name, Is.EqualTo("consumer-b"));
            Assert.That(builder.Publishers!.Single().Name, Is.EqualTo("publisher-b"));
            Assert.That(builder.Transactions!.Single().Name, Is.EqualTo("transaction-b"));
            Assert.That(builder.Probes!.Single().Name, Is.EqualTo("probe-b"));
            Assert.That(builder.Collectors!.Single().Name, Is.EqualTo("collector-b"));
            Assert.That(builder.MockerCommands!.Single().Name, Is.EqualTo("command-b"));
            Assert.That(builder.Stages.Single().StageNumber, Is.EqualTo(2));
        });
    }

    [Test]
    public void PublishersTransactionsAndProbes_ShouldSupportCrudByName()
    {
        var builder = new SessionBuilder()
            .AddPublisher(new PublisherBuilder().Named("publisher-a"))
            .AddTransaction(new TransactionBuilder().Named("transaction-a"))
            .AddProbe(new ProbeBuilder().Named("probe-a"));

        builder.UpdatePublisher("publisher-a", new PublisherBuilder().Named("publisher-updated"));
        builder.UpdateTransaction("transaction-a", new TransactionBuilder().Named("transaction-updated"));
        builder.UpdateProbe("probe-a", new ProbeBuilder().Named("probe-updated"));

        Assert.That(builder.Publishers![0].Name, Is.EqualTo("publisher-updated"));
        Assert.That(builder.Transactions![0].Name, Is.EqualTo("transaction-updated"));
        Assert.That(builder.Probes![0].Name, Is.EqualTo("probe-updated"));
        Assert.That(builder.Publishers.FirstOrDefault(publisher => publisher.Name == "publisher-updated")?.Name,
            Is.EqualTo("publisher-updated"));
        Assert.That(builder.Transactions.FirstOrDefault(transaction => transaction.Name == "transaction-updated")?.Name,
            Is.EqualTo("transaction-updated"));
        Assert.That(builder.Probes.FirstOrDefault(probe => probe.Name == "probe-updated")?.Name,
            Is.EqualTo("probe-updated"));

        builder.RemovePublisher("publisher-updated")
            .RemoveTransaction("transaction-updated")
            .RemoveProbe("probe-updated");

        Assert.That(builder.Publishers, Is.Empty);
        Assert.That(builder.Transactions, Is.Empty);
        Assert.That(builder.Probes, Is.Empty);
    }

    [Test]
    public void CollectorsAndMockerCommands_ShouldSupportCrudByName()
    {
        var builder = new SessionBuilder()
            .AddCollector(new CollectorBuilder().Named("collector-a"))
            .AddMockerCommand(new MockerCommandBuilder().Named("command-a"));

        builder.UpdateCollector("collector-a", new CollectorBuilder().Named("collector-updated"));
        builder.UpdateMockerCommand("command-a", new MockerCommandBuilder().Named("command-updated"));

        Assert.That(builder.Collectors![0].Name, Is.EqualTo("collector-updated"));
        Assert.That(builder.MockerCommands![0].Name, Is.EqualTo("command-updated"));
        Assert.That(builder.Collectors.FirstOrDefault(collector => collector.Name == "collector-updated")?.Name,
            Is.EqualTo("collector-updated"));
        Assert.That(builder.MockerCommands.FirstOrDefault(command => command.Name == "command-updated")?.Name,
            Is.EqualTo("command-updated"));

        builder.RemoveCollector("collector-updated")
            .RemoveMockerCommand("command-updated");

        Assert.That(builder.Collectors, Is.Empty);
        Assert.That(builder.MockerCommands, Is.Empty);
    }

    [Test]
    public void Stages_ShouldSupportCrudByStageNumber()
    {
        var builder = new SessionBuilder()
            .AddStage(new StageConfig(stageNumber: 1, timeoutBefore: 10, timeoutAfter: 20))
            .AddStage(new StageConfig(stageNumber: 2, timeoutBefore: 30, timeoutAfter: 40));

        builder.UpdateStage(1, new StageConfig(stageNumber: 1, timeoutBefore: 99, timeoutAfter: 100));

        Assert.That(builder.Stages, Has.Length.EqualTo(2));
        Assert.That(builder.Stages.First(stage => stage.StageNumber == 1).TimeoutBefore, Is.EqualTo(99));
        Assert.That(builder.Stages.FirstOrDefault(stage => stage.StageNumber == 1)?.TimeoutBefore, Is.EqualTo(99));

        builder.RemoveStage(2);
        Assert.That(builder.Stages, Has.Length.EqualTo(1));
        Assert.That(builder.Stages[0].StageNumber, Is.EqualTo(1));
    }

    [Test]
    public void UpdateByName_WithReplacementBuilder_ShouldReplaceExistingBuilders()
    {
        var builder = new SessionBuilder()
            .AddConsumer(new ConsumerBuilder().Named("consumer-a"))
            .AddPublisher(new PublisherBuilder().Named("publisher-a"))
            .AddTransaction(new TransactionBuilder().Named("transaction-a"))
            .AddProbe(new ProbeBuilder().Named("probe-a"))
            .AddCollector(new CollectorBuilder().Named("collector-a"))
            .AddMockerCommand(new MockerCommandBuilder().Named("command-a"));

        builder.UpdateConsumer("consumer-a", new ConsumerBuilder().Named("consumer-mutated"));
        builder.UpdatePublisher("publisher-a", new PublisherBuilder().Named("publisher-mutated"));
        builder.UpdateTransaction("transaction-a", new TransactionBuilder().Named("transaction-mutated"));
        builder.UpdateProbe("probe-a", new ProbeBuilder().Named("probe-mutated"));
        builder.UpdateCollector("collector-a", new CollectorBuilder().Named("collector-mutated"));
        builder.UpdateMockerCommand("command-a", new MockerCommandBuilder().Named("command-mutated"));

        Assert.That(builder.Consumers!.FirstOrDefault(consumer => consumer.Name == "consumer-mutated"), Is.Not.Null);
        Assert.That(builder.Publishers!.FirstOrDefault(publisher => publisher.Name == "publisher-mutated"), Is.Not.Null);
        Assert.That(builder.Transactions!.FirstOrDefault(transaction => transaction.Name == "transaction-mutated"), Is.Not.Null);
        Assert.That(builder.Probes!.FirstOrDefault(probe => probe.Name == "probe-mutated"), Is.Not.Null);
        Assert.That(builder.Collectors!.FirstOrDefault(collector => collector.Name == "collector-mutated"), Is.Not.Null);
        Assert.That(builder.MockerCommands!.FirstOrDefault(command => command.Name == "command-mutated"), Is.Not.Null);
    }

    [Test]
    public void UpdateStage_WhenStageNumberDoesNotExist_DoesNotChangeStages()
    {
        var builder = new SessionBuilder()
            .AddStage(new StageConfig(stageNumber: 1, timeoutBefore: 10, timeoutAfter: 20));

        builder.UpdateStage(99, new StageConfig(stageNumber: 99, timeoutBefore: 1, timeoutAfter: 1));

        Assert.That(builder.Stages, Has.Length.EqualTo(1));
        Assert.That(builder.Stages[0].StageNumber, Is.EqualTo(1));
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
            .AddPublisher(new PublisherBuilder().Named("publisher-a"));

        builder.UpdatePublisher("publisher-missing", new PublisherBuilder().Named("publisher-updated"));

        Assert.That(builder.Publishers, Has.Length.EqualTo(1));
        Assert.That(builder.Publishers![0].Name, Is.EqualTo("publisher-a"));
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




