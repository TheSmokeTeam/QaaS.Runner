using System.ComponentModel;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using QaaS.Framework.Serialization;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record ConsumeCommandConfig : Consume
{
    [Description("The deserializer to use to deserialize the consumed input data received by the mocker")]
    [DefaultValue(null)]
    public DeserializeConfig? InputDeserialize { get; set; }

    [Description("The deserializer to use to deserialize the consumed output data published by the mocker")]
    [DefaultValue(null)]
    public DeserializeConfig? OutputDeserialize { get; set; }
}
