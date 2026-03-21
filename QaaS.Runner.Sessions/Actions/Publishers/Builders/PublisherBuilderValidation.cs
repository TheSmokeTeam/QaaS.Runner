using System.ComponentModel.DataAnnotations;
using QaaS.Runner.Sessions.Actions;

namespace QaaS.Runner.Sessions.Actions.Publishers.Builders;

/// <summary>
/// Adds protocol-aware chunk validation to publisher configuration.
/// </summary>
public partial class PublisherBuilder : IValidatableObject
{
    /// <summary>
    /// Validates that the configured sender protocol and the optional <c>Chunk</c> section agree on the same
    /// communication mode.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        var configuredProtocol = ReadConfiguration();
        if (configuredProtocol == null)
        {
            return [];
        }

        var chunkMode = ProtocolChunkSupport.ResolveSenderMode(configuredProtocol);
        var propertyName = ProtocolChunkSupport.GetSenderConfigurationPropertyName(configuredProtocol);
        if (Chunk == null && chunkMode == ProtocolChunkMode.ChunkOnly)
        {
            return
            [
                new ValidationResult(
                    $"The {nameof(Chunk)} field is required when {propertyName} is configured.",
                    [nameof(Chunk)])
            ];
        }

        if (Chunk != null && chunkMode == ProtocolChunkMode.SingleOnly)
        {
            return
            [
                new ValidationResult(
                    $"The {nameof(Chunk)} field must be empty when {propertyName} is configured.",
                    [nameof(Chunk)])
            ];
        }

        return [];
    }
}
