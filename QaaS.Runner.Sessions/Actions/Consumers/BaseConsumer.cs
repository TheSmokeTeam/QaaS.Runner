using Microsoft.Extensions.Logging;
using QaaS.Framework.Policies;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;
using QaaS.Framework.Serialization.Deserializers;
using QaaS.Runner.Sessions.Extensions;

namespace QaaS.Runner.Sessions.Actions.Consumers;

public abstract class BaseConsumer : StagedAction
{
    protected readonly DataFilter DataFilter;
    private readonly IDeserializer? _deserializer;
    private readonly Type? _deserializerSpecificType;
    protected readonly SerializationType? SerializationType;
    protected readonly TimeSpan TimeoutMs;
    protected readonly TimeSpan? InitialTimeoutMs;
    protected RunningCommunicationData<object> RunningCommunicationData = default!;

    protected BaseConsumer(string name, TimeSpan timeoutMs, TimeSpan? initialTimeOutMs, int stage, Policy? policies, DataFilter dataFilter,
        SerializationType? serializationType, Type? deserializerSpecificType, ILogger logger) : base(name, stage,
        policies, logger)
    {
        TimeoutMs = timeoutMs;
        InitialTimeoutMs = initialTimeOutMs;
        DataFilter = dataFilter;
        SerializationType = serializationType;
        _deserializer = DeserializerFactory.BuildDeserializer(SerializationType);
        _deserializerSpecificType = deserializerSpecificType;
    }

    /// <summary>
    /// Should consume data using configured Reader and save it to the actData.
    /// </summary>
    /// <param name="actData">Object to store the consumed data under the Output list.</param>
    protected abstract void Consume(InternalCommunicationData<object> actData);
    
    /// <summary>
    /// Should consume data initially with specialized timeout using configured Reader and save it to the actData.
    /// </summary>
    /// <param name="actData">Object to store the consumed data under the Output list.</param>
    protected abstract bool InitialConsume(InternalCommunicationData<object> actData);

    protected abstract SerializationType? GetCommunicationSerializationType();

    /// <summary>
    /// Executes the consumer according to the instance configuration.
    /// Uses overridable <see cref="Consume"/> that represents the consumption mechanism of any derived class.
    /// </summary>
    internal override InternalCommunicationData<object> Act()
    {
        // consumer only initialize output
        var data = new InternalCommunicationData<object>
        {
            Output = [],
            OutputSerializationType = GetCommunicationSerializationType()
        };

        Policies?.SetupChain();
        Logger.LogDebug(
            "Starting consumer {ActionName}. InitialTimeoutMs={InitialTimeoutMs}, TimeoutMs={TimeoutMs}, SerializationType={SerializationType}",
            Name, InitialTimeoutMs?.TotalMilliseconds, TimeoutMs.TotalMilliseconds, SerializationType);
        
        if (InitialConsume(data))
            Consume(data);
        TerminateConsumer();
        
        Logger.LogDebug("Finished consumer {ActionName}. CollectedOutputCount={OutputCount}",
            Name, data.Output?.Count ?? 0);
        return data;
    }

    /// <inheritdoc />
    protected internal override void LogData(InternalCommunicationData<object> actData,
        DetailedData<object> itemBeforeSerialization, InputOutputState? saveData = null)
    {
        var readData = _deserializer != null
            ? GetDeserializedData(itemBeforeSerialization).FilterData(DataFilter)
            : itemBeforeSerialization.FilterData(DataFilter);

        lock (actData.Output!)
            actData.Output!.Add(readData);

        RunningCommunicationData.Data.Add(readData);
        RunningCommunicationData.Queue.Enqueue(readData);
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
        => context.GetRunningSession(sessionName).Outputs!.Add(RunningCommunicationData);
    
    protected void TerminateConsumer() => RunningCommunicationData.Data.CompleteAdding();
}
