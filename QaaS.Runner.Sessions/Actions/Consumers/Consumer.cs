using Microsoft.Extensions.Logging;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;

namespace QaaS.Runner.Sessions.Actions.Consumers;

public sealed class Consumer : BaseConsumer
{
    private readonly IReader? _reader;

    public Consumer(string name, IReader? reader, TimeSpan timeoutMs, TimeSpan? initialTimeoutMs, int stage, Policy? policies,
        DataFilter dataFilter, SerializationType? serializationType, Type? deserializerSpecificType,
        ILogger logger) : base(name, timeoutMs, initialTimeoutMs, stage, policies, dataFilter, serializationType,
        deserializerSpecificType, logger)
    {
        _reader = reader;
        Logger.LogInformation(
            "Initializing {Consumer} {ConsumerName} with Reader type {ReaderType} and Deserializer {DeserializerType}",
            GetType().Name, Name, _reader?.GetType().Name, SerializationType);

        RunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetCommunicationSerializationType()
        };
    }

    protected override void Consume(InternalCommunicationData<object> actData)
    {
        do {
            var readData = _reader!.Read(TimeoutMs);
            if (readData == null) break;

            LogData(actData, readData);

        } while(Policies?.RunChain() != false);

        TerminateConsumer();
    }

    protected override bool InitialConsume(InternalCommunicationData<object> actData)
    {
        if (InitialTimeoutMs == null) return true;
        
        var readData = _reader!.Read((TimeSpan)InitialTimeoutMs);
        if (readData == null)
        {
            TerminateConsumer();
            return false;
        }

        LogData(actData, readData);

        if (Policies?.RunChain() != false)
            return true;

        TerminateConsumer();
        return false;
    }
    

    protected override SerializationType? GetCommunicationSerializationType() =>
        _reader?.GetSerializationType() ?? SerializationType;


    internal override InternalCommunicationData<object> Act()
    {
        _reader!.Connect();
        var resultedRunData = base.Act();
        _reader!.Disconnect();
        return resultedRunData;
    }
}
