using Microsoft.Extensions.Logging;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.Serialization;

namespace QaaS.Runner.Sessions.Actions.Consumers;

public sealed class ChunkConsumer : BaseConsumer
{
    private readonly IChunkReader? _chunkReader;

    public ChunkConsumer(string name, IChunkReader? chunkReader, TimeSpan timeoutMs, TimeSpan? initialTimeOutMs, int stage, Policy? policies,
        DataFilter dataFilter, SerializationType? serializationType, Type? deserializerSpecificType,
        ILogger logger) : base(name, timeoutMs, initialTimeOutMs, stage, policies, dataFilter, serializationType,
        deserializerSpecificType, logger)
    {
        _chunkReader = chunkReader;
        Logger.LogInformation(
            "Initializing {Consumer} {ConsumerName} of type {ReaderType} and Deserializer type - {DeserializerType}",
            GetType().Name, Name, _chunkReader?.GetType().Name, SerializationType);

        RunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetCommunicationSerializationType()
        };
    }

    internal override InternalCommunicationData<object> Act()
    {
        _chunkReader!.Connect();
        var resultedRunData = base.Act();
        _chunkReader!.Disconnect();
        return resultedRunData;
    }

    protected override void Consume(InternalCommunicationData<object> actData)
    {
        var chunk = _chunkReader!.ReadChunk(TimeoutMs);

        foreach (var singleItem in chunk)
        {
            LogData(actData, singleItem);
            if (Policies?.RunChain() != false) continue;
            break;
        }
        
        TerminateConsumer();
    }

    protected override bool InitialConsume(InternalCommunicationData<object> actData)
    {
        return true;
    }

    protected override SerializationType? GetCommunicationSerializationType() =>
        _chunkReader?.GetSerializationType() ?? SerializationType;
}