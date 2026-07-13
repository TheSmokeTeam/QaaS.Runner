using System.Globalization;
using System.Reflection;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Sessions.Extensions;

/// <summary>
/// Converts exception trees into compact, deterministic action failures.
/// </summary>
internal static class ActionFailureNormalizer
{
    private const string S3FailurePrefix = "S3 operation failed:";

    private static readonly string[] S3SemanticPropertyNames = ["StatusCode", "ErrorCode"];

    private static readonly string[] S3DiagnosticPropertyNames = ["RequestId", "AmazonId2"];

    private static readonly string[] S3DescriptionPropertyNames =
    [
        "StatusCode",
        "ErrorCode",
        "RequestId",
        "AmazonId2",
    ];

    public static IReadOnlyList<ActionFailure> Create(
        Exception exception,
        string actionType,
        string actionRuntimeName,
        string? exceptionMessage = null
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        return NormalizeException(exception, exceptionMessage)
            .Select(normalizedCause => new ActionFailure
            {
                Name = actionRuntimeName,
                ActionType = actionType,
                Reason = normalizedCause.Reason,
            })
            .ToList();
    }

    /// <summary>
    /// Collapses semantic S3 duplicates and exact non-S3 duplicates, then imposes a stable order on failures collected
    /// concurrently.
    /// </summary>
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

    private static IReadOnlyList<NormalizedCause> NormalizeException(
        Exception exception,
        string? exceptionMessage
    )
    {
        var branches = new List<IReadOnlyList<Exception>>();
        ExpandExceptionBranches(exception, [], branches);

        return branches
            .Select(NormalizeBranch)
            .GroupBy(branch => branch.SemanticKey, StringComparer.Ordinal)
            .Select(group =>
                group
                    .OrderByDescending(branch => branch.DiagnosticCompleteness)
                    .ThenBy(branch => branch.DiagnosticKey, StringComparer.Ordinal)
                    .First()
            )
            .OrderBy(branch => branch.SemanticKey, StringComparer.Ordinal)
            .Select(branch => new NormalizedCause(
                new Reason
                {
                    Message = CreateFailureMessage(branch.Exceptions, exceptionMessage),
                    Description = CreateFailureDescription(branch.Exceptions),
                }
            ))
            .ToList();
    }

    private static void ExpandExceptionBranches(
        Exception exception,
        IReadOnlyList<Exception> prefix,
        ICollection<IReadOnlyList<Exception>> branches
    )
    {
        if (exception is AggregateException { InnerExceptions.Count: > 0 } aggregateException)
        {
            foreach (var innerException in aggregateException.InnerExceptions)
                ExpandExceptionBranches(innerException, prefix, branches);

            return;
        }

        var currentBranch = prefix.Append(exception).ToList();
        if (exception.InnerException is not null)
        {
            ExpandExceptionBranches(exception.InnerException, currentBranch, branches);
            return;
        }

        branches.Add(currentBranch);
    }

    private static ExceptionBranch NormalizeBranch(IReadOnlyList<Exception> exceptions)
    {
        var isS3Branch = exceptions.Any(IsS3Exception);
        var seenExceptions = new HashSet<string>(StringComparer.Ordinal);
        var distinctExceptions = exceptions
            .Where(exception =>
                seenExceptions.Add(CreateExceptionSemanticKey(exception, isS3Branch))
            )
            .ToList();
        var semanticKey = string.Join(
            "\u001e",
            distinctExceptions.Select(exception =>
                CreateExceptionSemanticKey(exception, isS3Branch)
            )
        );
        var diagnosticKey = string.Join("\u001e", exceptions.Select(CreateExceptionDiagnosticKey));
        var diagnosticCompleteness = exceptions
            .Where(IsS3Exception)
            .SelectMany(exception => GetS3Fields(exception, S3DiagnosticPropertyNames))
            .Select(field => field.Key)
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new ExceptionBranch(
            semanticKey,
            diagnosticCompleteness,
            diagnosticKey,
            distinctExceptions
        );
    }

    private static string CreateExceptionSemanticKey(Exception exception, bool omitStackTrace)
    {
        var type = exception.GetType();
        var fields = IsS3Exception(exception)
            ? GetS3Fields(exception, S3SemanticPropertyNames)
                .Select(field => $"{field.Key}={field.Value}")
            : [];
        return string.Join(
            "\u001f",
            [
                type.FullName ?? type.Name,
                IsS3Exception(exception)
                    ? string.Empty
                    : exception.HResult.ToString("X8", CultureInfo.InvariantCulture),
                NormalizeText(exception.Message),
                string.Join("\u001d", fields),
                omitStackTrace ? string.Empty : NormalizeText(exception.StackTrace),
            ]
        );
    }

    private static string CreateExceptionDiagnosticKey(Exception exception)
    {
        var type = exception.GetType();
        var fields = IsS3Exception(exception)
            ? GetS3Fields(exception, S3DescriptionPropertyNames)
                .Select(field => $"{field.Key}={field.Value}")
            : [];
        return string.Join(
            "\u001f",
            [
                type.FullName ?? type.Name,
                exception.HResult.ToString("X8", CultureInfo.InvariantCulture),
                NormalizeText(exception.Message),
                string.Join("\u001d", fields),
                NormalizeText(exception.StackTrace),
            ]
        );
    }

    private static string CreateFailureMessage(
        IReadOnlyList<Exception> exceptions,
        string? exceptionMessage
    )
    {
        if (!string.IsNullOrWhiteSpace(exceptionMessage))
            return NormalizeText(exceptionMessage);

        var s3Exception = exceptions.FirstOrDefault(IsS3Exception);
        if (s3Exception is not null)
            return CreateS3FailureMessage(s3Exception);

        var primaryException = exceptions[0];
        return string.IsNullOrWhiteSpace(primaryException.Message)
            ? primaryException.GetType().Name
            : NormalizeText(primaryException.Message);
    }

    private static string CreateFailureDescription(IReadOnlyList<Exception> exceptions)
    {
        var omitStackTraces = exceptions.Any(IsS3Exception);
        var lines = new List<string>();
        for (var index = 0; index < exceptions.Count; index++)
        {
            var exception = exceptions[index];
            if (index > 0)
                lines.Add("Caused by:");

            var type = exception.GetType();
            var message = NormalizeText(exception.Message);
            if (omitStackTraces)
            {
                lines.Add($"ExceptionType: {type.Name}");
                if (!string.IsNullOrWhiteSpace(message))
                    lines.Add($"Message: {message}");
            }
            else
            {
                lines.Add(
                    string.IsNullOrWhiteSpace(message)
                        ? type.FullName ?? type.Name
                        : $"{type.FullName ?? type.Name}: {message}"
                );
            }

            if (IsS3Exception(exception))
                lines.AddRange(
                    GetS3Fields(exception, S3DescriptionPropertyNames)
                        .Select(field => $"{field.Key}: {field.Value}")
                );

            if (!omitStackTraces && !string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                lines.Add("Stack trace:");
                lines.Add(NormalizeText(exception.StackTrace));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateS3FailureMessage(Exception exception)
    {
        var fields = GetS3Fields(exception, S3SemanticPropertyNames).ToList();
        if (!string.IsNullOrWhiteSpace(exception.Message))
        {
            fields.Add(
                new KeyValuePair<string, string>("Message", NormalizeText(exception.Message))
            );
        }

        return fields.Count == 0
            ? S3FailurePrefix
            : $"{S3FailurePrefix} {string.Join(", ", fields.Select(field => $"{field.Key}={field.Value}"))}";
    }

    private static bool IsS3Exception(Exception exception)
    {
        var type = exception.GetType();
        var typeName = type.FullName ?? type.Name;
        return typeName.Contains("AmazonS3Exception", StringComparison.Ordinal)
            || typeName.StartsWith("Amazon.S3.", StringComparison.Ordinal);
    }

    private static IEnumerable<KeyValuePair<string, string>> GetS3Fields(
        Exception exception,
        IEnumerable<string> propertyNames
    )
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetFailurePropertyValue(exception, propertyName);
            if (value is not null)
                yield return new KeyValuePair<string, string>(propertyName, value);
        }
    }

    private static string? GetFailurePropertyValue(Exception exception, string propertyName)
    {
        object? propertyValue;
        try
        {
            propertyValue = exception
                .GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(exception);
        }
        catch (Exception reflectionException)
            when (reflectionException
                    is AmbiguousMatchException
                        or MethodAccessException
                        or TargetInvocationException
            )
        {
            return $"<unavailable: {reflectionException.GetType().Name}>";
        }

        return propertyValue switch
        {
            null => null,
            string stringValue when string.IsNullOrWhiteSpace(stringValue) => null,
            string stringValue => NormalizeText(stringValue),
            Enum enumValue =>
                $"{Convert.ToInt64(enumValue, CultureInfo.InvariantCulture)} {enumValue}",
            _ => Convert.ToString(propertyValue, CultureInfo.InvariantCulture),
        };
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

    private sealed record ExceptionBranch(
        string SemanticKey,
        int DiagnosticCompleteness,
        string DiagnosticKey,
        IReadOnlyList<Exception> Exceptions
    );

    private sealed record NormalizedCause(Reason Reason);

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
