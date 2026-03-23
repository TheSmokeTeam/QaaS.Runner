using System.ComponentModel;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record MockerCommandConfig
{
    [Description("Mocker 'ChangeActionStub' command properties")]
    public ChangeActionStub? ChangeActionStub { get; internal set; }
    [Description("Mocker 'TriggerAction' command properties")]
    public TriggerAction? TriggerAction { get; internal set; }
    [Description("Mocker 'Consume' command properties")]
    public ConsumeCommandConfig? Consume { get; internal set; }
    public ChangeActionStub? ReadChangeActionStub() => ChangeActionStub;

    public TriggerAction? ReadTriggerAction() => TriggerAction;

    public ConsumeCommandConfig? ReadConsume() => Consume;
}
