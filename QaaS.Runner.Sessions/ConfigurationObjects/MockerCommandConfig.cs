using System.ComponentModel;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record MockerCommandConfig
{
    [Description("Mocker 'ChangeActionStub' command properties")]
    internal ChangeActionStub? ChangeActionStub { get; set; }

    [Description("Mocker 'TriggerAction' command properties")]
    internal TriggerAction? TriggerAction { get; set; }

    [Description("Mocker 'Consume' command properties")]
    internal ConsumeCommandConfig? Consume { get; set; }

    public ChangeActionStub? ReadChangeActionStub() => ChangeActionStub;

    public TriggerAction? ReadTriggerAction() => TriggerAction;

    public ConsumeCommandConfig? ReadConsume() => Consume;
}
