using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.Serialization;

namespace QaaS.Runner.Sessions.Actions;

/// <summary>
/// Internal action result that keeps the input-side communication data compatible with the
/// public <see cref="CommunicationData{TData}"/> contract while also carrying output-side data.
/// </summary>
public record InternalCommunicationData<TData> : CommunicationData<TData>
{
    /// <summary>
    /// Input-side communication data recorded by the action.
    /// This is backed by the inherited <see cref="CommunicationData{TData}.Data"/> property.
    /// </summary>
    public IList<DetailedData<TData>>? Input
    {
        get => Data;
        init => Data = value!;
    }

    /// <summary>
    /// Output-side communication data recorded by the action.
    /// </summary>
    public List<DetailedData<TData>?>? Output { get; set; }

    /// <summary>
    /// Input-side serialization type. This is backed by the inherited
    /// <see cref="BaseCommunicationData.SerializationType"/> property.
    /// </summary>
    public SerializationType? InputSerializationType
    {
        get => SerializationType;
        init => SerializationType = value;
    }

    /// <summary>
    /// Output-side serialization type.
    /// </summary>
    public SerializationType? OutputSerializationType { get; set; }
}
