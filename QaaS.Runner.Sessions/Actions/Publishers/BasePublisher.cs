using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Policies;
using QaaS.Framework.SDK.ConfigurationObjectFilters;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Framework.Serialization.Serializers;
using QaaS.Runner.Sessions.Extensions;

namespace QaaS.Runner.Sessions.Actions.Publishers;

public abstract class BasePublisher : StagedAction
{
    protected RunningCommunicationData<object> RunningCommunicationData = default!;
    protected readonly DataFilter DataFilter;
    private readonly string[]? _dataSourceNames;
    private readonly string[]? _dataSourcePatterns;
    private readonly int _iterations;
    protected readonly int? Parallelism;
    private readonly bool _loop;
    private ulong _sleepTimeMs;
    protected readonly SerializationType? SerializationType;
    private readonly ISerializer? _serializer;
    protected IEnumerable<Data<object>>? GeneratedData;
    protected IterableSerializableDataIterator IterableSerializableSaveIterator = default!;
    protected SemaphoreSlim? ParallelismSemaphore;

    protected BasePublisher(string name, int stage, DataFilter dataFilter, string[]? dataSourceNames,
        string[]? dataSourcePatterns, int? parallelism, int iterations, bool loop, ulong sleepTimeMs,
        SerializationType? serializationType, Policy? policies, ILogger logger)
        : base(name, stage, policies, logger)
    {
        DataFilter = dataFilter;
        _dataSourceNames = dataSourceNames;
        _dataSourcePatterns = dataSourcePatterns;
        _iterations = iterations;
        Parallelism = parallelism;
        if (Parallelism != null) InitializeSemaphore(Parallelism.Value);
        _loop = loop;
        _sleepTimeMs = sleepTimeMs;
        SerializationType = serializationType;
        _serializer = SerializerFactory.BuildSerializer(SerializationType);
    }

    /// <summary>
    /// Retrieves all Enumerable Generators and merges them into one Enumerable, to be used as the published data. 
    /// </summary>
    /// <param name="ranSessions"> previously ran session that some generators depend on </param>
    /// <param name="dataSources"> previously ran datasources that some generators depend on </param>
    public void InitializeIterableSerializableSaveIterator(List<SessionData?> ranSessions, List<DataSource> dataSources)
    {
        GeneratedData = EnumerableExtensions.GetFilteredConfigurationObjectList(dataSources.ToImmutableList(),
                _dataSourcePatterns,
                RegexFilters.DataSource,
                "DataSource List")
            .Union(EnumerableExtensions.GetFilteredConfigurationObjectList(dataSources.ToImmutableList(),
                _dataSourceNames,
                NameFilters.DataSource,
                "DataSource List")).SelectMany(ds =>
                ds.Retrieve(ranSessions.Where(sessionData => sessionData != null).ToImmutableList()!));
        IterableSerializableSaveIterator = new IterableSerializableDataIterator(GeneratedData, _serializer);
    }

    /// <summary>
    /// Initializes the semaphore used to control the number of concurrent connections.
    /// </summary>
    /// <param name="connectionAcceptanceValue">Base value for connection acceptance.</param>
    private void InitializeSemaphore(int connectionAcceptanceValue)
    {
        var maxConnections = connectionAcceptanceValue;
        ParallelismSemaphore = new SemaphoreSlim(maxConnections, maxConnections);
        Logger.LogDebug("Connection Acceptance Semaphore initiated with max parallelism of {MaxConnections}",
            maxConnections);
    }

    /// <summary>
    /// Should publish data generated using <see cref="GeneratedData"/> or <see cref="IterableSerializableSaveIterator"/>
    /// and save it to the actData.
    /// </summary>
    /// <param name="actData">Object to store the published data under the Input list.</param>
    /// <returns>Whether to keep the publishing repeatedly or stop.</returns>
    protected abstract bool Publish(InternalCommunicationData<object> actData);

    protected abstract SerializationType? GetCommunicationSerializationType();

    /// <summary>
    /// Acts any publishing class repeatedly according to instance's properties.
    /// Uses overridable <see cref="Publish"/> that represents the publishing mechanism of any derived class.
    /// </summary>
    internal override InternalCommunicationData<object> Act()
    {
        // publisher only initialize input
        var data = new InternalCommunicationData<object>
        {
            Input = new List<DetailedData<object>>(),
            InputSerializationType = GetCommunicationSerializationType()
        };

        var shouldAct = true;
        Policies?.SetupChain();
        int iteration = 0;
        while (shouldAct)
        {
            Logger.LogDebug("Acting publishing action {ActionName} in {ActingType} in iteration number {Iteration}",
                Name, _loop ? "Loop" : "Iteration", iteration + 1);
            shouldAct = Publish(data) && (_loop || _iterations > ++iteration);
            Logger.LogDebug("Finished publishing action {ActionName} in {ActingType} in iteration number {Iteration}" +
                            " - sleeping for {SleepTimeMs} Milliseconds",
                Name, _loop ? "Loop" : "Iteration", iteration, _sleepTimeMs);
            Thread.Sleep((int)_sleepTimeMs);
        }

        RunningCommunicationData.Data.CompleteAdding();
        return data;
    }

    /// <inheritdoc />
    protected internal override void LogData(InternalCommunicationData<object> actData,
        DetailedData<object> itemBeforeSerialization, InputOutputState? saveData = null)
    {
        var savedData = itemBeforeSerialization.FilterData(DataFilter);

        lock (actData.Input!)
        {
            actData.Input!.Add(savedData);
        }

        RunningCommunicationData.Data.Add(savedData);
        RunningCommunicationData.Queue.Enqueue(savedData);
    }

    internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
        => context.InternalRunningSessions.RunningSessionsDict[sessionName].Inputs!.Add(RunningCommunicationData);
}
