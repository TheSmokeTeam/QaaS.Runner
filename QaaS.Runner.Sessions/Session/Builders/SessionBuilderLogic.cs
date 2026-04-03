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
    /// <summary>
    /// Sets the name used for the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the timeout applied before the session runs.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder WithTimeoutBefore(uint timeout)
    {
        TimeoutBeforeSessionMs = timeout;
        return this;
    }

    /// <summary>
    /// Sets the timeout applied after the session runs.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder WithTimeoutAfter(uint timeout)
    {
        TimeoutAfterSessionMs = timeout;
        return this;
    }

    /// <summary>
    /// Sets the stage used by the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder AtStage(int stage)
    {
        Stage = stage;
        RunUntilStage = stage + 1;
        return this;
    }

    /// <summary>
    /// Limits the session to run only until the specified stage.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder RunSessionUntilStage(int stage)
    {
        RunUntilStage = stage;
        return this;
    }

    /// <summary>
    /// Disables data persistence for the configured session.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder DiscardData()
    {
        SaveData = false;
        return this;
    }

    /// <summary>
    /// Sets the category used by the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder WithinCategory(string category)
    {
        Category = category;
        return this;
    }

    /// <summary>
    /// Sets the time zone id used for daylight-saving-aware offset conversions in this session.
    /// </summary>
    /// <remarks>
    /// Use this when session actions that rely on offset-based date conversion should resolve daylight-saving rules from a specific time zone.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder WithTimeZone(string timeZoneId)
    {
        TimeZoneId = timeZoneId;
        return this;
    }

    /// <summary>
    /// Adds the supplied consumer to the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder AddConsumer(ConsumerBuilder consumerBuilder)
    {
        Consumers = Consumers is null ? [consumerBuilder] : Consumers.Append(consumerBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured consumer entry on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder CreateConsumer(ConsumerBuilder consumerBuilder)
    {
        return AddConsumer(consumerBuilder);
    }

    /// <summary>
    /// Returns the configured consumers currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public IReadOnlyList<ConsumerBuilder> ReadConsumers()
    {
        return Consumers ?? [];
    }

    /// <summary>
    /// Returns the configured consumer currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public ConsumerBuilder? ReadConsumer(string name)
    {
        return ReadByName(Consumers, name, consumer => consumer.Name);
    }

    /// <summary>
    /// Updates the configured consumer stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateConsumer(string name, ConsumerBuilder consumerBuilder)
    {
        Consumers = UpdateByName(Consumers, name, consumerBuilder, consumer => consumer.Name);
        return this;
    }

    /// <summary>
    /// Updates the configured consumer stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateConsumer(string name, Func<ConsumerBuilder, ConsumerBuilder> update)
    {
        Consumers = UpdateByName(Consumers, name, update, consumer => consumer.Name);
        return this;
    }

    /// <summary>
    /// Removes the configured consumer from the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder RemoveConsumer(string name)
    {
        Consumers = RemoveByName(Consumers, name, consumer => consumer.Name);
        return this;
    }

    /// <summary>
    /// Adds the supplied publisher to the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder AddPublisher(PublisherBuilder publisherBuilder)
    {
        Publishers = Publishers is null ? [publisherBuilder] : Publishers.Append(publisherBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured publisher entry on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder CreatePublisher(PublisherBuilder publisherBuilder)
    {
        return AddPublisher(publisherBuilder);
    }

    /// <summary>
    /// Returns the configured publishers currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public IReadOnlyList<PublisherBuilder> ReadPublishers()
    {
        return Publishers ?? [];
    }

    /// <summary>
    /// Returns the configured publisher currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public PublisherBuilder? ReadPublisher(string name)
    {
        return ReadByName(Publishers, name, publisher => publisher.Name);
    }

    /// <summary>
    /// Updates the configured publisher stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdatePublisher(string name, PublisherBuilder publisherBuilder)
    {
        Publishers = UpdateByName(Publishers, name, publisherBuilder, publisher => publisher.Name);
        return this;
    }

    /// <summary>
    /// Updates the configured publisher stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdatePublisher(string name, Func<PublisherBuilder, PublisherBuilder> update)
    {
        Publishers = UpdateByName(Publishers, name, update, publisher => publisher.Name);
        return this;
    }

    /// <summary>
    /// Removes the configured publisher from the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder RemovePublisher(string name)
    {
        Publishers = RemoveByName(Publishers, name, publisher => publisher.Name);
        return this;
    }

    /// <summary>
    /// Adds the supplied transaction to the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder AddTransaction(TransactionBuilder transactionBuilder)
    {
        Transactions = Transactions is null ? [transactionBuilder] : Transactions.Append(transactionBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured transaction entry on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder CreateTransaction(TransactionBuilder transactionBuilder)
    {
        return AddTransaction(transactionBuilder);
    }

    /// <summary>
    /// Returns the configured transactions currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public IReadOnlyList<TransactionBuilder> ReadTransactions()
    {
        return Transactions ?? [];
    }

    /// <summary>
    /// Returns the configured transaction currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public TransactionBuilder? ReadTransaction(string name)
    {
        return ReadByName(Transactions, name, transaction => transaction.Name);
    }

    /// <summary>
    /// Updates the configured transaction stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateTransaction(string name, TransactionBuilder transactionBuilder)
    {
        Transactions = UpdateByName(Transactions, name, transactionBuilder, transaction => transaction.Name);
        return this;
    }

    /// <summary>
    /// Updates the configured transaction stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateTransaction(string name, Func<TransactionBuilder, TransactionBuilder> update)
    {
        Transactions = UpdateByName(Transactions, name, update, transaction => transaction.Name);
        return this;
    }

    /// <summary>
    /// Removes the configured transaction from the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder RemoveTransaction(string name)
    {
        Transactions = RemoveByName(Transactions, name, transaction => transaction.Name);
        return this;
    }

    /// <summary>
    /// Adds the supplied probe to the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder AddProbe(ProbeBuilder probeBuilder)
    {
        Probes = Probes is null ? [probeBuilder] : Probes.Append(probeBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured probe entry on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder CreateProbe(ProbeBuilder probeBuilder)
    {
        return AddProbe(probeBuilder);
    }

    /// <summary>
    /// Returns the configured probes currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public IReadOnlyList<ProbeBuilder> ReadProbes()
    {
        return Probes ?? [];
    }

    /// <summary>
    /// Returns the configured probe currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public ProbeBuilder? ReadProbe(string name)
    {
        return ReadByName(Probes, name, probe => probe.Name);
    }

    /// <summary>
    /// Updates the configured probe stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateProbe(string name, ProbeBuilder probeBuilder)
    {
        Probes = UpdateByName(Probes, name, probeBuilder, probe => probe.Name);
        return this;
    }

    /// <summary>
    /// Updates the configured probe stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateProbe(string name, Func<ProbeBuilder, ProbeBuilder> update)
    {
        Probes = UpdateByName(Probes, name, update, probe => probe.Name);
        return this;
    }

    /// <summary>
    /// Removes the configured probe from the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder RemoveProbe(string name)
    {
        Probes = RemoveByName(Probes, name, probe => probe.Name);
        return this;
    }

    /// <summary>
    /// Adds the supplied collector to the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder AddCollector(CollectorBuilder collectorBuilder)
    {
        Collectors = Collectors is null ? [collectorBuilder] : Collectors.Append(collectorBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured collector entry on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder CreateCollector(CollectorBuilder collectorBuilder)
    {
        return AddCollector(collectorBuilder);
    }

    /// <summary>
    /// Returns the configured collectors currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public IReadOnlyList<CollectorBuilder> ReadCollectors()
    {
        return Collectors ?? [];
    }

    /// <summary>
    /// Returns the configured collector currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public CollectorBuilder? ReadCollector(string name)
    {
        return ReadByName(Collectors, name, collector => collector.Name);
    }

    /// <summary>
    /// Updates the configured collector stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateCollector(string name, CollectorBuilder collectorBuilder)
    {
        Collectors = UpdateByName(Collectors, name, collectorBuilder, collector => collector.Name);
        return this;
    }

    /// <summary>
    /// Updates the configured collector stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateCollector(string name, Func<CollectorBuilder, CollectorBuilder> update)
    {
        Collectors = UpdateByName(Collectors, name, update, collector => collector.Name);
        return this;
    }

    /// <summary>
    /// Removes the configured collector from the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder RemoveCollector(string name)
    {
        Collectors = RemoveByName(Collectors, name, collector => collector.Name);
        return this;
    }

    /// <summary>
    /// Adds the supplied mocker command to the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder AddMockerCommand(MockerCommandBuilder mockerCommandBuilder)
    {
        MockerCommands = MockerCommands is null
            ? [mockerCommandBuilder]
            : MockerCommands.Append(mockerCommandBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured mocker command entry on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder CreateMockerCommand(MockerCommandBuilder mockerCommandBuilder)
    {
        return AddMockerCommand(mockerCommandBuilder);
    }

    /// <summary>
    /// Returns the configured mocker commands currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public IReadOnlyList<MockerCommandBuilder> ReadMockerCommands()
    {
        return MockerCommands ?? [];
    }

    /// <summary>
    /// Returns the configured mocker command currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public MockerCommandBuilder? ReadMockerCommand(string name)
    {
        return ReadByName(MockerCommands, name, command => command.Name);
    }

    /// <summary>
    /// Updates the configured mocker command stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateMockerCommand(string name, MockerCommandBuilder mockerCommandBuilder)
    {
        MockerCommands = UpdateByName(MockerCommands, name, mockerCommandBuilder, command => command.Name);
        return this;
    }

    /// <summary>
    /// Updates the configured mocker command stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder UpdateMockerCommand(string name, Func<MockerCommandBuilder, MockerCommandBuilder> update)
    {
        MockerCommands = UpdateByName(MockerCommands, name, update, command => command.Name);
        return this;
    }

    /// <summary>
    /// Removes the configured mocker command from the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder RemoveMockerCommand(string name)
    {
        MockerCommands = RemoveByName(MockerCommands, name, command => command.Name);
        return this;
    }

    /// <summary>
    /// Adds the supplied stage to the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder AddStage(StageConfig stage)
    {
        Stages = Stages.Append(stage).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured stage entry on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder CreateStage(StageConfig stage)
    {
        return AddStage(stage);
    }

    /// <summary>
    /// Returns the configured stages currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public IReadOnlyList<StageConfig> ReadStages()
    {
        return Stages;
    }

    /// <summary>
    /// Returns the configured stage currently stored on the Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public StageConfig? ReadStage(int stageNumber)
    {
        return Stages.FirstOrDefault(configuredStage => configuredStage.StageNumber == stageNumber);
    }

    /// <summary>
    /// Updates the configured stage stored on the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
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

    /// <summary>
    /// Removes the configured stage from the current Runner session builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner session builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Sessions" />
    public SessionBuilder RemoveStage(int stageNumber)
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

        var publishers = (Publishers ??= [])
            .Select(publisher => publisher.BuildWithTimeZone(context, actionFailures, Name!, TimeZoneId))
            .Where(publisher => publisher != null).ToArray();

        var transactions = (Transactions ??= []).Select(transaction => transaction.Build(context, actionFailures, Name!))
            .Where(transaction => transaction != null).ToArray();

        var consumers = (Consumers ??= [])
            .Select(consumer => consumer.BuildWithTimeZone(context, actionFailures, Name!, TimeZoneId))
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

    private static T[]? RemoveByName<T>(T[]? values, string name, Func<T, string?> nameSelector)
    {
        return values?.Where(value => nameSelector(value) != name).ToArray();
    }
}

