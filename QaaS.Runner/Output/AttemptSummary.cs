using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text.Json;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions.AssertionObjects;

namespace QaaS.Runner.Output;

internal enum AttemptOperation
{
    Run,
    Act,
    Assert
}

internal enum AttemptOutcome
{
    Completed,
    Pass,
    AssertionFailure,
    InfrastructureFailure,
    Cancelled,
    Unknown
}

/// <summary>
/// Owns the optional local handoff for one top-level invocation, independently of the regular log sinks.
/// </summary>
internal sealed class AttemptSummary(
    AttemptOperation operation,
    string directory,
    string attemptId,
    string projectKey)
{
    private const int MaximumEncodedBytes = 1024;
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _published;

    internal static AttemptSummary? FromEnvironment(AttemptOperation operation)
    {
        var directory = Environment.GetEnvironmentVariable("QAAS_ATTEMPT_SUMMARY_DIRECTORY");
        return directory is null
            ? null
            : TryStart(operation, directory, Environment.GetEnvironmentVariable("QAAS_ATTEMPT_ID"),
                Environment.GetEnvironmentVariable("QAAS_PROJECT_KEY"));
    }

    internal static AttemptSummary? TryStart(
        AttemptOperation operation, string? directory, string? attemptId, string? projectKey)
    {
        if (string.IsNullOrEmpty(directory) || !IsAbsoluteLocalPathSyntax(directory) ||
            !IsOpaqueKey(attemptId) || !IsOpaqueKey(projectKey))
            return null;

        return new AttemptSummary(operation, directory, attemptId!, projectKey!);
    }

    private static bool IsAbsoluteLocalPathSyntax(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            return false;
        // This is a syntax check, not proof that a drive/mount or symlink is local. Provisioning owns that boundary.
        return OperatingSystem.IsWindows()
            ? path.Length >= 3 && IsAsciiDriveLetter(path[0])
              && path[1] == ':' && (path[2] is '\\' or '/')
            : !path.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool IsAsciiDriveLetter(char character) => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsOpaqueKey(string? value) =>
        value is { Length: 32 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        value.Any(character => character != '0');

    internal AttemptOutcome Classify(int exitCode, IEnumerable<Execution> executions)
    {
        // An exit code alone cannot distinguish assertions from setup errors or custom runners.
        if (operation == AttemptOperation.Act)
            return exitCode == 0 ? AttemptOutcome.Completed : AttemptOutcome.Unknown;

        var observed = false;
        var failed = false;
        var unknown = false;
        foreach (var execution in executions)
        {
            foreach (var entry in execution.AttemptAssertionResults)
            {
                observed = true;
                switch (entry)
                {
                    case AssertionResult { AssertionStatus: AssertionStatus.Broken }:
                        return AttemptOutcome.InfrastructureFailure;
                    case AssertionResult { AssertionStatus: AssertionStatus.Failed }:
                        failed = true;
                        break;
                    case AssertionResult { AssertionStatus: AssertionStatus.Passed }:
                        break;
                    default:
                        unknown = true;
                        break;
                }
            }
        }

        if (unknown)
            return AttemptOutcome.Unknown;
        if (exitCode == 0 && !failed)
            return observed ? AttemptOutcome.Pass : AttemptOutcome.Completed;
        return exitCode != 0 && failed ? AttemptOutcome.AssertionFailure : AttemptOutcome.Unknown;
    }

    internal void Publish(AttemptOutcome outcome, int? exitCode)
    {
        if (Interlocked.Exchange(ref _published, 1) != 0)
            return;

        string? temporary = null;
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema_version = 1,
                attempt_id = attemptId,
                project_key = projectKey,
                operation = operation switch
                {
                    AttemptOperation.Act => "act",
                    AttemptOperation.Assert => "assert",
                    _ => "run"
                },
                outcome = outcome switch
                {
                    AttemptOutcome.Pass => "pass",
                    AttemptOutcome.AssertionFailure => "assertion_failure",
                    AttemptOutcome.InfrastructureFailure => "infra_failure",
                    AttemptOutcome.Cancelled => "cancelled",
                    AttemptOutcome.Completed => "completed",
                    _ => "unknown"
                },
                completed_at = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                duration_ms = (long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                exit_code = exitCode
            });

            if (payload.Length > MaximumEncodedBytes)
                return;
            // Same directory makes the completed file visible as a single publication. Never overwrite a
            // completed summary, and don't create a spool, follow a caller-supplied filename or retry here.
            var completed = Path.Combine(directory, attemptId + ".json");
            var candidate = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(candidate, options))
            {
                temporary = candidate;
                stream.Write(payload);
            }
            File.Move(temporary, completed, overwrite: false);
            temporary = null;
        }
        catch (Exception exception) when (IsHandoffFailure(exception))
        {
            // Optional evidence must not change the original result or dispatch any network logging.
        }
        finally
        {
            if (temporary is not null)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception exception) when (IsHandoffFailure(exception))
                {
                    // A collector never treats temporary files as attempts.
                }
            }
        }
    }

    private static bool IsHandoffFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or SecurityException or InvalidOperationException;
}
