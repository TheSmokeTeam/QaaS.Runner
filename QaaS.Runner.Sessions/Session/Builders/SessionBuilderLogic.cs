using System.Collections.Concurrent;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using ConsumerBuilder = QaaS.Runner.Sessions.Actions.Consumers.Builders.ConsumerBuilder;
using PublisherBuilder = QaaS.Runner.Sessions.Actions.Publishers.Builders.PublisherBuilder;

namespace QaaS.Runner.Sessions.Session.Builders;

public partial class SessionBuilder
{
    // metadata
    public SessionBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    public SessionBuilder WithTimeoutBefore(uint timeout)
    {
        TimeoutBeforeSessionMs = timeout;
        return this;
    }

    public SessionBuilder WithTimeoutAfter(uint timeout)
    {
        TimeoutAfterSessionMs = timeout;
        return this;
    }

    public SessionBuilder AtStage(int stage)
    {
        Stage = stage;
        RunUntilStage = stage + 1;
        return this;
    }

    public SessionBuilder RunSessionUntilStage(int stage)
    {
        RunUntilStage = stage;
        return this;
    }

    public SessionBuilder DiscardData()
    {
        SaveData = false;
        return this;
    }

    public SessionBuilder WithinCategory(string category)
    {
        Category = category;
        return this;
    }

    public SessionBuilder AddConsumer(ConsumerBuilder consumerBuilder)
    {
        Consumers = Consumers is null ? [consumerBuilder] : Consumers.Append(consumerBuilder).ToArray();
        return this;
    }

    public SessionBuilder CreateConsumer(ConsumerBuilder consumerBuilder)
    {
        return AddConsumer(consumerBuilder);
    }

    public IReadOnlyList<ConsumerBuilder> ReadConsumers()
    {
        return Consumers ?? [];
    }

    public ConsumerBuilder? ReadConsumer(string name)
    {
        return ReadByName(Consumers, name, consumer => consumer.Name);
    }

    public SessionBuilder UpdateConsumer(string name, ConsumerBuilder consumerBuilder)
    {
        Consumers = UpdateByName(Consumers, name, consumerBuilder, consumer => consumer.Name);
        return this;
    }

    public SessionBuilder UpdateConsumer(string name, Func<ConsumerBuilder, ConsumerBuilder> update)
    {
        Consumers = UpdateByName(Consumers, name, update, consumer => consumer.Name);
        return this;
    }

    public SessionBuilder DeleteConsumer(string name)
    {
        Consumers = DeleteByName(Consumers, name, consumer => consumer.Name);
        return this;
    }

    public SessionBuilder AddPublisher(PublisherBuilder publisherBuilder)
    {
        Publishers = Publishers is null ? [publisherBuilder] : Publishers.Append(publisherBuilder).ToArray();
        return this;
    }

    public SessionBuilder CreatePublisher(PublisherBuilder publisherBuilder)
    {
        return AddPublisher(publisherBuilder);
    }

    public IReadOnlyList<PublisherBuilder> ReadPublishers()
    {
        return Publishers ?? [];
    }

    public PublisherBuilder? ReadPublisher(string name)
    {
        return ReadByName(Publishers, name, publisher => publisher.Name);
    }

    public SessionBuilder UpdatePublisher(string name, PublisherBuilder publisherBuilder)
    {
        Publishers = UpdateByName(Publishers, name, publisherBuilder, publisher => publisher.Name);
        return this;
    }

    public SessionBuilder UpdatePublisher(string name, Func<PublisherBuilder, PublisherBuilder> update)
    {
        Publishers = UpdateByName(Publishers, name, update, publisher => publisher.Name);
        return this;
    }

    public SessionBuilder DeletePublisher(string name)
    {
        Publishers = DeleteByName(Publishers, name, publisher => publisher.Name);
        return this;
    }

    public SessionBuilder AddTransaction(TransactionBuilder transactionBuilder)
    {
        Transactions = Transactions is null ? [transactionBuilder] : Transactions.Append(transactionBuilder).ToArray();
        return this;
    }

    public SessionBuilder CreateTransaction(TransactionBuilder transactionBuilder)
    {
        return AddTransaction(transactionBuilder);
    }

    public IReadOnlyList<TransactionBuilder> ReadTransactions()
    {
        return Transactions ?? [];
    }

    public TransactionBuilder? ReadTransaction(string name)
    {
        return ReadByName(Transactions, name, transaction => transaction.Name);
    }

    public SessionBuilder UpdateTransaction(string name, TransactionBuilder transactionBuilder)
    {
        Transactions = UpdateByName(Transactions, name, transactionBuilder, transaction => transaction.Name);
        return this;
    }

    public SessionBuilder UpdateTransaction(string name, Func<TransactionBuilder, TransactionBuilder> update)
    {
        Transactions = UpdateByName(Transactions, name, update, transaction => transaction.Name);
        return this;
    }

    public SessionBuilder DeleteTransaction(string name)
    {
        Transactions = DeleteByName(Transactions, name, transaction => transaction.Name);
        return this;
    }

    public SessionBuilder AddProbe(ProbeBuilder probeBuilder)
    {
        Probes = Probes is null ? [probeBuilder] : Probes.Append(probeBuilder).ToArray();
        return this;
    }

    public SessionBuilder CreateProbe(ProbeBuilder probeBuilder)
    {
        return AddProbe(probeBuilder);
    }

    public IReadOnlyList<ProbeBuilder> ReadProbes()
    {
        return Probes ?? [];
    }

    public ProbeBuilder? ReadProbe(string name)
    {
        return ReadByName(Probes, name, probe => probe.Name);
    }

    public SessionBuilder UpdateProbe(string name, ProbeBuilder probeBuilder)
    {
        Probes = UpdateByName(Probes, name, probeBuilder, probe => probe.Name);
        return this;
    }

    public SessionBuilder UpdateProbe(string name, Func<ProbeBuilder, ProbeBuilder> update)
    {
        Probes = UpdateByName(Probes, name, update, probe => probe.Name);
        return this;
    }

    public SessionBuilder DeleteProbe(string name)
    {
        Probes = DeleteByName(Probes, name, probe => probe.Name);
        return this;
    }

    public SessionBuilder AddCollector(CollectorBuilder collectorBuilder)
    {
        Collectors = Collectors is null ? [collectorBuilder] : Collectors.Append(collectorBuilder).ToArray();
        return this;
    }

    public SessionBuilder CreateCollector(CollectorBuilder collectorBuilder)
    {
        return AddCollector(collectorBuilder);
    }

    public IReadOnlyList<CollectorBuilder> ReadCollectors()
    {
        return Collectors ?? [];
    }

    public CollectorBuilder? ReadCollector(string name)
    {
        return ReadByName(Collectors, name, collector => collector.Name);
    }

    public SessionBuilder UpdateCollector(string name, CollectorBuilder collectorBuilder)
    {
        Collectors = UpdateByName(Collectors, name, collectorBuilder, collector => collector.Name);
        return this;
    }

    public SessionBuilder UpdateCollector(string name, Func<CollectorBuilder, CollectorBuilder> update)
    {
        Collectors = UpdateByName(Collectors, name, update, collector => collector.Name);
        return this;
    }

    public SessionBuilder DeleteCollector(string name)
    {
        Collectors = DeleteByName(Collectors, name, collector => collector.Name);
        return this;
    }

    public SessionBuilder AddMockerCommand(MockerCommandBuilder mockerCommandBuilder)
    {
        MockerCommands = MockerCommands is null
            ? [mockerCommandBuilder]
            : MockerCommands.Append(mockerCommandBuilder).ToArray();
        return this;
    }

    public SessionBuilder CreateMockerCommand(MockerCommandBuilder mockerCommandBuilder)
    {
        return AddMockerCommand(mockerCommandBuilder);
    }

    public IReadOnlyList<MockerCommandBuilder> ReadMockerCommands()
    {
        return MockerCommands ?? [];
    }

    public MockerCommandBuilder? ReadMockerCommand(string name)
    {
        return ReadByName(MockerCommands, name, command => command.Name);
    }

    public SessionBuilder UpdateMockerCommand(string name, MockerCommandBuilder mockerCommandBuilder)
    {
        MockerCommands = UpdateByName(MockerCommands, name, mockerCommandBuilder, command => command.Name);
        return this;
    }

    public SessionBuilder UpdateMockerCommand(string name, Func<MockerCommandBuilder, MockerCommandBuilder> update)
    {
        MockerCommands = UpdateByName(MockerCommands, name, update, command => command.Name);
        return this;
    }

    public SessionBuilder DeleteMockerCommand(string name)
    {
        MockerCommands = DeleteByName(MockerCommands, name, command => command.Name);
        return this;
    }

    public SessionBuilder AddStage(StageConfig stage)
    {
        Stages = Stages.Append(stage).ToArray();
        return this;
    }

    public SessionBuilder CreateStage(StageConfig stage)
    {
        return AddStage(stage);
    }

    public IReadOnlyList<StageConfig> ReadStages()
    {
        return Stages;
    }

    public StageConfig? ReadStage(int stageNumber)
    {
        return Stages.FirstOrDefault(configuredStage => configuredStage.StageNumber == stageNumber);
    }

    public SessionBuilder UpdateStage(int stageNumber, StageConfig stage)
    {
        var existingIndex = Array.FindIndex(Stages, configuredStage => configuredStage.StageNumber == stageNumber);
        if (existingIndex < 0)
        {
            return this;
        }

        Stages[existingIndex] = stage;
        return this;
    }

    public SessionBuilder DeleteStage(int stageNumber)
    {
        Stages = Stages.Where(configuredStage => configuredStage.StageNumber != stageNumber).ToArray();
        return this;
    }

    /// <summary>
    /// Builds every configured action for the session, collects action-level failures, and groups staged actions.
    /// </summary>
    internal Session Build(InternalContext context, IList<KeyValuePair<string, IProbe>> probeHooks)
    {
        var actionFailures = new List<ActionFailure>();

        var publishers = (Publishers ??= []).Select(publisher => publisher.Build(context, actionFailures, Name!))
            .Where(publisher => publisher != null).ToArray();

        var transactions = (Transactions ??= []).Select(transaction => transaction.Build(context, actionFailures, Name!))
            .Where(transaction => transaction != null).ToArray();

        var consumers = (Consumers ??= []).Select(consumer => consumer.Build(context, actionFailures, Name!))
            .Where(consumer => consumer != null).ToArray();

        var probes = (Probes ??= []).Select(probe => probe.Build(context, probeHooks, actionFailures, Name!))
            .Where(probe => probe != null).ToArray();

        var mockerCommands = (MockerCommands ??= []).Select(mockerCommand => mockerCommand.Build(context, actionFailures, Name!))
            .Where(mockerCommand => mockerCommand != null).ToArray();

        var collectors = (Collectors ??= []).Select(collector => collector.Build(context, actionFailures, Name!))
            .Where(collector => collector != null).ToArray();

        var concurrentActionFailures = new ConcurrentBag<ActionFailure>(actionFailures);
        var stagedActions = new List<StagedAction>();
        stagedActions.AddRange(publishers!);
        stagedActions.AddRange(transactions!);
        stagedActions.AddRange(consumers!);
        stagedActions.AddRange(probes!);
        stagedActions.AddRange(mockerCommands!);
        var stages = BuildStages(context, stagedActions, concurrentActionFailures);

        return new Session(
            Name!,
            Stage!.Value,
            SaveData,
            TimeoutBeforeSessionMs,
            TimeoutAfterSessionMs,
            stages,
            collectors!,
            context,
            concurrentActionFailures,
            RunUntilStage);
    }

    /// <summary>
    ///     Build all the stages and populates them with the built action based on the action builders
    /// </summary>
    private Dictionary<int, Stage> BuildStages(InternalContext context, List<StagedAction> stagedActions,
        ConcurrentBag<ActionFailure> actionFailures)
    {
        var stages = new Dictionary<int, Stage>();

        foreach (var communication in stagedActions)
        {
            if (!stages.ContainsKey(communication.Stage))
            {
                var stageConfig = Stages?.FirstOrDefault(s => s.StageNumber == communication.Stage);
                if (stageConfig != null)
                    stages[communication.Stage] = new Stage(context, actionFailures, Name!, communication.Stage,
                        stageConfig.TimeoutBefore, stageConfig.TimeoutAfter);
                else
                    stages[communication.Stage] = new Stage(context, actionFailures, Name!, communication.Stage);
            }

            stages[communication.Stage].AddCommunication(communication);
        }

        return stages;
    }

    private static T[]? UpdateByName<T>(T[]? values, string name, T replacement, Func<T, string?> nameSelector)
    {
        if (values == null)
        {
            return values;
        }

        var index = Array.FindIndex(values, value => nameSelector(value) == name);
        if (index < 0)
        {
            return values;
        }

        values[index] = replacement;
        return values;
    }

    private static T[]? UpdateByName<T>(T[]? values, string name, Func<T, T> update, Func<T, string?> nameSelector)
    {
        if (values == null)
        {
            return values;
        }

        var index = Array.FindIndex(values, value => nameSelector(value) == name);
        if (index < 0)
        {
            return values;
        }

        values[index] = update(values[index]);
        return values;
    }

    private static T? ReadByName<T>(T[]? values, string name, Func<T, string?> nameSelector) where T : class
    {
        return values?.FirstOrDefault(value => nameSelector(value) == name);
    }

    private static T[]? DeleteByName<T>(T[]? values, string name, Func<T, string?> nameSelector)
    {
        return values?.Where(value => nameSelector(value) != name).ToArray();
    }
}
