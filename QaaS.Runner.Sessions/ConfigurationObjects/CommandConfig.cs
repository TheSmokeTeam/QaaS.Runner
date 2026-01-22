using System.ComponentModel;
using QaaS.Framework.SDK.MockerObjects.ConfigurationObjects.Command;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record CommandConfig
{
    [Description("Mocker 'ChangeActionStub' command properties")]
    public ChangeActionStub? ChangeActionStub { get; set; }

    [Description("Mocker 'TriggerAction' command properties")]
    public TriggerAction? TriggerAction { get; set; }

    [Description("Mocker 'Consume' command properties")]
    public ConsumeConfig? Consume { get; set; }
}