using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;

[ExcludeFromCodeCoverage]
public record CommandRequest
{
    public string Id { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CommandType Command { get; init; }

    public ChangeActionStub? ChangeActionStub { get; set; }

    public Consume? Consume { get; set; }

    public TriggerAction? TriggerAction { get; set; }

    public void AppendObjectToRelevantCommandConfig(object config)
    {
        switch (Command)
        {
            case CommandType.ChangeActionStub:
                ChangeActionStub = (ChangeActionStub)config;
                break;
            case CommandType.Consume:
                Consume = (Consume)config;
                break;
            case CommandType.TriggerAction:
                TriggerAction = (TriggerAction)config;
                break;
        }
    }
}
