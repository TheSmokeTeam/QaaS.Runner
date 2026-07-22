using System.ComponentModel.DataAnnotations;
using QaaS.Framework.Protocols.ConfigurationObjects.Http;

namespace QaaS.Runner.Sessions.Actions.Transactions.Builders;

/// <summary>
/// Adds protocol-aware input selection validation to transaction configuration.
/// </summary>
public partial class TransactionBuilder : IValidatableObject
{
    /// <summary>
    /// Validates that a transaction either selects input data or explicitly requests a source-free HTTP GET.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        var hasConfiguredDataSourceSelectors =
            DataSourceNames != null || DataSourcePatterns != null;
        var hasNonEmptyDataSourceSelectors =
            DataSourceNames is { Length: > 0 } || DataSourcePatterns is { Length: > 0 };

        if (!SendEmptyRequest)
        {
            return hasConfiguredDataSourceSelectors
                ? []
                :
                [
                    new ValidationResult(
                        $"At least one of {nameof(DataSourceNames)} or {nameof(DataSourcePatterns)} must be configured unless {nameof(SendEmptyRequest)} is enabled.",
                        [nameof(DataSourceNames), nameof(DataSourcePatterns)]
                    ),
                ];
        }

        var validationResults = new List<ValidationResult>();
        if (hasNonEmptyDataSourceSelectors)
        {
            validationResults.Add(
                new ValidationResult(
                    $"The {nameof(SendEmptyRequest)} field cannot be enabled when {nameof(DataSourceNames)} or {nameof(DataSourcePatterns)} contains entries.",
                    [nameof(SendEmptyRequest), nameof(DataSourceNames), nameof(DataSourcePatterns)]
                )
            );
        }

        if (Http?.Method != HttpMethods.Get || Grpc != null)
        {
            validationResults.Add(
                new ValidationResult(
                    $"The {nameof(SendEmptyRequest)} field can be enabled only for an HTTP GET transaction.",
                    [nameof(SendEmptyRequest), nameof(Http), nameof(Grpc)]
                )
            );
        }

        if (InputSerialize != null)
        {
            validationResults.Add(
                new ValidationResult(
                    $"The {nameof(InputSerialize)} field must be empty when {nameof(SendEmptyRequest)} is enabled because the request has no body.",
                    [nameof(SendEmptyRequest), nameof(InputSerialize)]
                )
            );
        }

        return validationResults;
    }
}
