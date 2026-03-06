using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.Protocols;
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
using QaaS.Framework.Serialization.Deserializers;
using QaaS.Framework.Serialization.Serializers;
using QaaS.Runner.Sessions.Extensions;

namespace QaaS.Runner.Sessions.Actions.Transactions;

public class Transaction : StagedAction
{
    private readonly string[]? _dataSourceNames;
    private readonly string[]? _dataSourcePatterns;
    private readonly SerializationType? _deserializationType;

    private readonly IDeserializer? _deserializer;
    private readonly Type? _deserializerSpecificType;
    private readonly DataFilter _inputDataFilter;
    private readonly int _iterations;
    private readonly bool _loop;
    private ulong _sleepTimeMs;
    private readonly DataFilter _outputDataFilter;

    private readonly SerializationType? _serializationType;
    private readonly ISerializer? _serializer;
    private readonly ITransactor _transactor;

    private IEnumerable<Data<object>>? _generatedData;
    private IterableSerializableDataIterator _iterableSerializableSaveIterator = default!;
    private RunningCommunicationData<object> _receivedRunningCommunicationData;
    private RunningCommunicationData<object> _sentRunningCommunicationData;

    public Transaction(string name,
        ITransactor transactor,
        int stage, DataFilter inputDataFilter, DataFilter outputDataFilter,
        Policy? policies, bool loop, int iterations, ulong sleepTimeMs,
        SerializationType? serializationType, SerializationType? deserializationType, Type? deserializerSpecificType,
        string[]? dataSourcePatterns, string[]? dataSourceNames, ILogger logger) :
        base(name, stage, policies, logger)
    {
        _transactor = transactor;
        _inputDataFilter = inputDataFilter;
        _outputDataFilter = outputDataFilter;
        _dataSourceNames = dataSourceNames;
        _sleepTimeMs = sleepTimeMs;
        _dataSourcePatterns = dataSourcePatterns;
        _loop = loop;
        _iterations = iterations;
        _serializationType = serializationType;
        _deserializationType = deserializationType;
        _deserializer = DeserializerFactory.BuildDeserializer(deserializationType);
        _deserializerSpecificType = deserializerSpecificType;
        _serializer = SerializerFactory.BuildSerializer(serializationType);
        Logger.LogInformation(
            "Initializing Transaction {Name} with transactor of type {TransactorType} with Input Serializer {Serializer} and Output Deserializer {Deserializer}",
            Name, transactor.GetType(), _serializer, _deserializer);
        _sentRunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetInputCommunicationSerializationType()
        };
        _receivedRunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetOutputCommunicationSerializationType()
        };
    }

    public void InitializeIterableSerializableSaveIterator(List<SessionData?> ranSessions, List<DataSource> dataSources)
    {
        _generatedData = EnumerableExtensions.GetFilteredConfigurationObjectList(dataSources.ToImmutableList(),
                _dataSourcePatterns,
                RegexFilters.DataSource,
                "DataSource List")
            .Union(EnumerableExtensions.GetFilteredConfigurationObjectList(dataSources.ToImmutableList(),
                _dataSourceNames,
                NameFilters.DataSource,
                "DataSource List")).SelectMany(ds =>
                ds.Retrieve(ranSessions.Where(sessionData => sessionData != null).ToImmutableList()!));
        _iterableSerializableSaveIterator = new IterableSerializableDataIterator(_generatedData, _serializer);
    }

    internal override InternalCommunicationData<object> Act()
    {
        // transaction initializes both input and output
        var data = new InternalCommunicationData<object>
        {
            Input = new List<DetailedData<object>>(),
            InputSerializationType = GetInputCommunicationSerializationType(),
            Output = new List<DetailedData<object>?>(),
            OutputSerializationType = GetOutputCommunicationSerializationType()
        };

        var shouldAct = true;
        Policies?.SetupChain();
        int iteration = 0;
        while (shouldAct)
        {
            Logger.LogDebug("Acting transacting action {ActionName} in {ActingType} in iteration number {Iteration}",
                Name, _loop ? "Loop" : "Iteration", iteration + 1);
            shouldAct = Transact(data) && (_loop || _iterations > ++iteration);
            Logger.LogDebug("Finished transacting action {ActionName} in {ActingType} in iteration number {Iteration}" +
                            " - sleeping for {SleepTimeMs} Milliseconds",
                Name, _loop ? "Loop" : "Iteration", iteration, _sleepTimeMs);
            Thread.Sleep((int)_sleepTimeMs);
        }

        _receivedRunningCommunicationData.Data.CompleteAdding();
        _sentRunningCommunicationData.Data.CompleteAdding();
        return data;
    }

    private SerializationType? GetInputCommunicationSerializationType()
    {
        return _transactor.GetInputCommunicationSerializationType() ?? _serializationType;
    }

    private SerializationType? GetOutputCommunicationSerializationType()
    {
        return _transactor.GetOutputCommunicationSerializationType() ?? _deserializationType;
    }

    /// <summary>
    /// Activate transact mechanism single time on generated data.
    /// </summary>
    /// <returns>True if should continue and false if not.</returns>
    private bool Transact(InternalCommunicationData<object> actData)
    {
        var dataToTransact = _iterableSerializableSaveIterator.IterateEnumerable();
        var pairIndex = 0;

        foreach (var data in dataToTransact)
        {
            var transactionData = _transactor.Transact(data);

            var iteratedDataItem = _iterableSerializableSaveIterator.GetDataBeforeSerialization(pairIndex);

            LogData(
                actData,
                iteratedDataItem
                    .CloneDetailed(transactionData.Item1.Timestamp)
                    .AddIoMatchIndexToDetailedData(pairIndex),
                InputOutputState.OnlyInput
            );
            var response = transactionData.Item2?.AddIoMatchIndexToDetailedData(pairIndex);
            if (response != null)
            {
                LogData(
                    actData,
                    response.CloneDetailed(transactionData.Item2?.Timestamp)
                        .AddIoMatchIndexToDetailedData(pairIndex),
                    InputOutputState.OnlyOutput
                );
            }

            pairIndex++;

            if (Policies?.RunChain() != false) continue;

            return false;
        }

        return true;
    }

    private DetailedData<object> GetDeserializedData(DetailedData<object> readData)
    {
        return new DetailedData<object>
        {
            Body = _deserializer!.Deserialize(readData.CastObjectData<byte[]>().Body, _deserializerSpecificType),
            MetaData = readData.MetaData,
            Timestamp = readData.Timestamp
        };
    }

    internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
    {
        _receivedRunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetOutputCommunicationSerializationType()
        };
        context.InternalRunningSessions.RunningSessionsDict[sessionName].Inputs!.Add(_sentRunningCommunicationData);
        context.InternalRunningSessions.RunningSessionsDict[sessionName].Outputs!
            .Add(_receivedRunningCommunicationData);
    }

    protected internal override void LogData(InternalCommunicationData<object> actData,
        DetailedData<object> itemBeforeSerialization, InputOutputState? saveData = null)
    {
        switch (saveData)
        {
            case InputOutputState.OnlyInput:
                LogInputData(actData, itemBeforeSerialization);
                break;
            case InputOutputState.OnlyOutput:
                LogOutputData(actData, itemBeforeSerialization);
                break;
        }
    }

    private void LogInputData(InternalCommunicationData<object> actData, DetailedData<object> itemBeforeSerialization)
    {
        itemBeforeSerialization = itemBeforeSerialization.FilterData(_inputDataFilter);
        actData.Input!.Add(itemBeforeSerialization);

        _sentRunningCommunicationData.Data.Add(itemBeforeSerialization);
        _sentRunningCommunicationData.Queue.Enqueue(itemBeforeSerialization);
    }

    private void LogOutputData(InternalCommunicationData<object> actData, DetailedData<object> itemBeforeSerialization)
    {
        itemBeforeSerialization =
            (_deserializer != null ? GetDeserializedData(itemBeforeSerialization) : itemBeforeSerialization)
            .FilterData(_outputDataFilter);
        actData.Output!.Add(itemBeforeSerialization);
        _receivedRunningCommunicationData.Data.Add(itemBeforeSerialization);
        _receivedRunningCommunicationData.Queue.Enqueue(itemBeforeSerialization);
    }
}
