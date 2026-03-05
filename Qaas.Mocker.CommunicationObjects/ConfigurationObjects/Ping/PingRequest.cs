using System.Diagnostics.CodeAnalysis;

namespace Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Ping;

[ExcludeFromCodeCoverage]
public record PingRequest
{
    public string Id { get; set; } = string.Empty;
}
