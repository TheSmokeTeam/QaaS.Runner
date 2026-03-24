using System.Text.Json.Serialization;

namespace QaaS.Runner.E2ETests;

public sealed class PostgreSqlGeometryPayload
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("shape")]
    public string Shape { get; set; } = string.Empty;
}
