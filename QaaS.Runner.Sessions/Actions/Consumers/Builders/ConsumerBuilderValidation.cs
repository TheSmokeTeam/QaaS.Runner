using System.ComponentModel.DataAnnotations;
using QaaS.Runner.Sessions.Actions;

namespace QaaS.Runner.Sessions.Actions.Consumers.Builders;

/// <summary>
/// Adds protocol-aware chunk validation to consumer configuration.
/// </summary>
public partial class ConsumerBuilder : IValidatableObject
{
    /// <summary>
    /// Validates that the configured reader protocol resolves to a deterministic consumer mode.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        var configuredProtocol = Configuration;
        if (configuredProtocol == null)
        {
            return [];
        }

        var chunkMode = ProtocolChunkSupport.ResolveReaderMode(configuredProtocol);
        if (chunkMode != ProtocolChunkMode.SingleOrChunk)
        {
            return [];
        }

        var propertyName = ProtocolChunkSupport.GetReaderConfigurationPropertyName(configuredProtocol);
        return
        [
            new ValidationResult(
                $"The {propertyName} field is ambiguous because the configured protocol supports both single and chunk reading, but consumer configuration does not expose a chunk selection option.",
                [propertyName])
        ];
    }
}
