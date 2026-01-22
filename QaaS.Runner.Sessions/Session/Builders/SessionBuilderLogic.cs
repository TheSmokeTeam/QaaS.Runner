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
        SaveData = true;
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

    public SessionBuilder AddPublisher(PublisherBuilder publisherBuilder)
    {
        Publishers = Publishers is null ? [publisherBuilder] : Publishers.Append(publisherBuilder).ToArray();
        return this;
    }

    public SessionBuilder AddTransaction(TransactionBuilder transactionBuilder)
    {
        Transactions = Transactions is null ? [transactionBuilder] : Transactions.Append(transactionBuilder).ToArray();
        return this;
    }

    public SessionBuilder AddProbe(ProbeBuilder probeBuilder)
    {
        Probes = Probes is null ? [probeBuilder] : Probes.Append(probeBuilder).ToArray();
        return this;
    }

    public SessionBuilder AddCollector(CollectorBuilder collectorBuilder)
    {
        Collectors = Collectors is null ? [collectorBuilder] : Collectors.Append(collectorBuilder).ToArray();
        return this;
    }

    public SessionBuilder AddMockerCommand(MockerCommandBuilder mockerCommandBuilder)
    {
        MockerCommands = MockerCommands is null
            ? [mockerCommandBuilder]
            : MockerCommands.Append(mockerCommandBuilder).ToArray();
        return this;
    }

    public SessionBuilder AddStage(StageConfig stage)
    {
        Stages = Stages.Append(stage).ToArray();
        return this;
    }

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
}