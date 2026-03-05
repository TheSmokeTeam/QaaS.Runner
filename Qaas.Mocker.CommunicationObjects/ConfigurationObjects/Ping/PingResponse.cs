using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Ping;

[ExcludeFromCodeCoverage]
public record PingResponse
{ 
    public string Id { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string ServerInstanceId { get; set; } = string.Empty;
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InputOutputState ServerInputOutputState { get; set; }
}
