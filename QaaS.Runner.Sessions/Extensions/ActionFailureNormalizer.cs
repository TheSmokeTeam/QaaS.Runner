using System.Globalization;
using System.Reflection;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Sessions.Extensions;

/// <summary>
/// Converts exception trees into compact, deterministic action failures.
/// </summary>
internal static class ActionFailureNormalizer
{
    private static readonly string[] S3PropertyNames =
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
    /// Removes exact duplicate action failures and imposes a stable order on failures collected concurrently.
    /// </summary>
    public static List<ActionFailure> Normalize(IEnumerable<ActionFailure> actionFailures)
    {
        ArgumentNullException.ThrowIfNull(actionFailures);

        return actionFailures
            .GroupBy(CreateFailureKey)
            .Select(group => group.First())
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
            .GroupBy(branch => branch.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(branch => branch.Key, StringComparer.Ordinal)
            .Select(branch => new NormalizedCause(
                branch.Key,
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
        var seenExceptions = new HashSet<string>(StringComparer.Ordinal);
        var distinctExceptions = exceptions
            .Where(exception => seenExceptions.Add(CreateExceptionKey(exception)))
            .ToList();
        var key = string.Join("\u001e", distinctExceptions.Select(CreateExceptionKey));
        return new ExceptionBranch(key, distinctExceptions);
    }

    private static string CreateExceptionKey(Exception exception)
    {
        var type = exception.GetType();
        var fields = IsS3Exception(exception)
            ? GetS3Fields(exception).Select(field => $"{field.Key}={field.Value}")
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
                    GetS3Fields(exception).Select(field => $"{field.Key}: {field.Value}")
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
        var fields = GetS3Fields(exception).ToList();
        if (!string.IsNullOrWhiteSpace(exception.Message))
        {
            fields.Add(
                new KeyValuePair<string, string>("Message", NormalizeText(exception.Message))
            );
        }

        return fields.Count == 0
            ? $"S3 operation failed: {NormalizeText(exception.Message)}"
            : $"S3 operation failed: {string.Join(", ", fields.Select(field => $"{field.Key}={field.Value}"))}";
    }

    private static bool IsS3Exception(Exception exception)
    {
        var type = exception.GetType();
        var typeName = type.FullName ?? type.Name;
        return typeName.Contains("AmazonS3Exception", StringComparison.Ordinal)
            || typeName.StartsWith("Amazon.S3.", StringComparison.Ordinal);
    }

    private static IEnumerable<KeyValuePair<string, string>> GetS3Fields(Exception exception)
    {
        foreach (var propertyName in S3PropertyNames)
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

    private static FailureKey CreateFailureKey(ActionFailure actionFailure) =>
        new(
            actionFailure.Action ?? string.Empty,
            actionFailure.ActionType,
            actionFailure.Name,
            actionFailure.Reason.Message,
            actionFailure.Reason.Description
        );

    private static string NormalizeText(string? value) =>
        (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private sealed record ExceptionBranch(string Key, IReadOnlyList<Exception> Exceptions);

    private sealed record NormalizedCause(string Key, Reason Reason);

    private sealed record FailureKey(
        string Action,
        string ActionType,
        string Name,
        string Message,
        string Description
    );
}
