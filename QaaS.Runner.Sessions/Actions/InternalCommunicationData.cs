using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;

namespace QaaS.Runner.Sessions.Actions;

public record InternalCommunicationData<TData>
{
    public IList<DetailedData<TData>>? Input { get; set; }

    public List<DetailedData<object>?>? Output { get; set; }

    public SerializationType? InputSerializationType { get; set; }

    public SerializationType? OutputSerializationType { get; set; }
}