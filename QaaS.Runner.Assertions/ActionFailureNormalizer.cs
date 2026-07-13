using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Assertions;

/// <summary>
/// Provides deterministic action-failure projections for assertions and reporters, including restored legacy data.
/// </summary>
internal static class ActionFailureNormalizer
{
    private const string S3FailurePrefix = "S3 operation failed:";

    public static List<ActionFailure> Normalize(IEnumerable<ActionFailure> actionFailures)
    {
        ArgumentNullException.ThrowIfNull(actionFailures);

        return actionFailures
            .Select(NormalizeActionFailure)
            .GroupBy(normalizedFailure => normalizedFailure.SemanticKey)
            .Select(group =>
                group
                    .OrderByDescending(normalizedFailure =>
                        normalizedFailure.DiagnosticCompleteness
                    )
                    .ThenBy(
                        normalizedFailure => normalizedFailure.DiagnosticKey,
                        StringComparer.Ordinal
                    )
                    .First()
                    .Failure
            )
            .OrderBy(failure => failure.Action ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(failure => failure.ActionType, StringComparer.Ordinal)
            .ThenBy(failure => failure.Name, StringComparer.Ordinal)
            .ThenBy(failure => failure.Reason.Message, StringComparer.Ordinal)
            .ThenBy(failure => failure.Reason.Description, StringComparer.Ordinal)
            .ToList();
    }

    private static NormalizedActionFailure NormalizeActionFailure(ActionFailure actionFailure)
    {
        var message = NormalizeText(actionFailure.Reason.Message);
        var description = NormalizeText(actionFailure.Reason.Description);
        if (TryCreateStableS3FailureMessage(message, out var stableMessage))
        {
            var normalizedFailure = actionFailure with
            {
                Reason = actionFailure.Reason with { Message = stableMessage },
            };
            return new NormalizedActionFailure(
                new FailureKey(
                    actionFailure.Action ?? string.Empty,
                    actionFailure.ActionType,
                    actionFailure.Name,
                    stableMessage,
                    string.Empty
                ),
                GetS3DiagnosticCompleteness(message, description),
                $"{message}\u001f{description}",
                normalizedFailure
            );
        }

        return new NormalizedActionFailure(
            new FailureKey(
                actionFailure.Action ?? string.Empty,
                actionFailure.ActionType,
                actionFailure.Name,
                message,
                description
            ),
            0,
            $"{message}\u001f{description}",
            actionFailure
        );
    }

    private static bool TryCreateStableS3FailureMessage(string message, out string stableMessage)
    {
        stableMessage = message;
        if (!message.StartsWith(S3FailurePrefix, StringComparison.Ordinal))
            return false;

        var payload = message[S3FailurePrefix.Length..].Trim();
        if (payload.Length == 0)
            return true;

        var header = payload;
        string? failureMessage = null;
        const string messageFieldPrefix = "Message=";
        const string messageFieldSeparator = ", Message=";
        if (payload.StartsWith(messageFieldPrefix, StringComparison.Ordinal))
        {
            header = string.Empty;
            failureMessage = payload[messageFieldPrefix.Length..];
        }
        else
        {
            var messageIndex = payload.IndexOf(messageFieldSeparator, StringComparison.Ordinal);
            if (messageIndex >= 0)
            {
                header = payload[..messageIndex];
                failureMessage = payload[(messageIndex + messageFieldSeparator.Length)..];
            }
        }

        var fields = header
            .Split(", ", StringSplitOptions.RemoveEmptyEntries)
            .Select(field =>
            {
                var separatorIndex = field.IndexOf('=');
                return separatorIndex <= 0
                    ? new KeyValuePair<string, string>(string.Empty, field)
                    : new KeyValuePair<string, string>(
                        field[..separatorIndex],
                        field[(separatorIndex + 1)..]
                    );
            })
            .Where(field =>
                !field.Key.Equals("RequestId", StringComparison.Ordinal)
                && !field.Key.Equals("AmazonId2", StringComparison.Ordinal)
            )
            .OrderBy(field => GetS3FieldOrder(field.Key))
            .ThenBy(field => field.Key, StringComparer.Ordinal)
            .ThenBy(field => field.Value, StringComparer.Ordinal)
            .ToList();
        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            fields.Add(new KeyValuePair<string, string>("Message", NormalizeText(failureMessage)));
        }

        stableMessage =
            fields.Count == 0
                ? S3FailurePrefix
                : $"{S3FailurePrefix} {string.Join(", ", fields.Select(field => string.IsNullOrEmpty(field.Key) ? field.Value : $"{field.Key}={field.Value}"))}";
        return true;
    }

    private static int GetS3FieldOrder(string fieldName) =>
        fieldName switch
        {
            "StatusCode" => 0,
            "ErrorCode" => 1,
            _ => 2,
        };

    private static int GetS3DiagnosticCompleteness(string message, string description)
    {
        var diagnosticText = $"{message}\n{description}";
        var hasRequestId =
            diagnosticText.Contains("RequestId=", StringComparison.Ordinal)
            || diagnosticText.Contains("RequestId:", StringComparison.Ordinal);
        var hasAmazonId2 =
            diagnosticText.Contains("AmazonId2=", StringComparison.Ordinal)
            || diagnosticText.Contains("AmazonId2:", StringComparison.Ordinal);
        return (hasRequestId ? 1 : 0) + (hasAmazonId2 ? 1 : 0);
    }

    private static string NormalizeText(string? value) =>
        (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private sealed record NormalizedActionFailure(
        FailureKey SemanticKey,
        int DiagnosticCompleteness,
        string DiagnosticKey,
        ActionFailure Failure
    );

    private sealed record FailureKey(
        string Action,
        string ActionType,
        string Name,
        string Message,
        string Description
    );
}
