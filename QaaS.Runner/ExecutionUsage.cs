using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Extensions;

namespace QaaS.Runner;

/// <summary>One invocation of an executable plan, including its original attribution.</summary>
internal sealed class ExecutionUsage
{
    private const string Dimensions =
        "{QaasEvent} {AttemptId} {ExecutionType} Team={Team} TeamKnown={TeamKnown} Project={Project} ProjectKnown={ProjectKnown} "
        + "System={System} SystemKnown={SystemKnown} Version={Version} Features={Features} IsCase={IsCase} IsBatch={IsBatch}";
    private const string Unknown = "Unknown";
    private static readonly string Version =
        typeof(Execution)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? Unknown;

    private readonly ILogger _logger;
    private readonly ExecutionType _type;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Guid _attemptId = Guid.NewGuid();
    private readonly string? _team;
    private readonly string? _project;
    private readonly string? _system;
    private readonly string[] _features;
    private readonly bool _isCase;
    private readonly bool _isBatch;

    internal string Outcome { get; set; } = "success";
    internal int? AssertionsExecuted { get; set; } = 0;
    internal bool HasBrokenAssertion { get; set; }

    private ExecutionUsage(Execution execution, Context context, ExecutionType type)
    {
        _logger = context.Logger;
        _type = type;
        _isCase = !string.IsNullOrWhiteSpace(context.CaseName);
        _isBatch = execution.IsBatch;
        var metadata = ReadMetadata(context);
        _team = Known(metadata?.Team);
        _system = Known(metadata?.System);
        if (metadata?.ExtraLabels?.TryGetValue("Project", out var project) == true)
            _project = Known(project as string);

        var features = new List<string>(5);
        if (execution.DataSourceLogic?.IsConfigured == true)
            features.Add("DataSources");
        if (type != ExecutionType.Assert && execution.SessionLogic?.IsConfigured == true)
            features.Add("Sessions");
        if (type != ExecutionType.Run && execution.StorageLogic?.IsConfigured == true)
            features.Add("Storage");
        if (type != ExecutionType.Act && execution.AssertionLogic?.IsConfigured == true)
            features.Add("Assertions");
        if (type != ExecutionType.Act && execution.ReportLogic?.Reporters.Count > 0)
            features.Add("Reporting");
        _features = features.ToArray();
    }

    internal static ExecutionUsage? Start(Execution execution, Context context, ExecutionType type)
    {
        try
        {
            if (
                type == ExecutionType.Template
                || !context.Logger.IsEnabled(LogLevel.Debug)
                || string.Equals(
                    Environment.GetEnvironmentVariable("QAAS_USAGE_ENABLED"),
                    "false",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return null;

            var attempt = new ExecutionUsage(execution, context, type);
            attempt.Write(start: true);
            return attempt;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // An unavailable provider must preserve the original execution result.
            return null;
        }
    }

    internal static bool IsCancellation(Exception exception) =>
        exception is OperationCanceledException
        || exception is AggregateException { InnerExceptions.Count: > 0 } aggregate
            && aggregate.InnerExceptions.All(IsCancellation);

    internal void Finish()
    {
        try
        {
            Write(start: false);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // A provider failure must not replace the original result or exception.
        }
    }

    private void Write(bool start)
    {
        if (start)
            _logger.LogDebug(
                Dimensions,
                "RunnerExecutionStarted",
                _attemptId,
                _type,
                _team ?? Unknown,
                _team != null,
                _project ?? Unknown,
                _project != null,
                _system ?? Unknown,
                _system != null,
                Version,
                _features,
                _isCase,
                _isBatch
            );
        else
            _logger.LogDebug(
                Dimensions
                    + " DurationMs={DurationMs} Outcome={Outcome} Failed={Failed} TestsExecuted={TestsExecuted} AssertionsExecuted={AssertionsExecuted}",
                "RunnerExecutionFinished",
                _attemptId,
                _type,
                _team ?? Unknown,
                _team != null,
                _project ?? Unknown,
                _project != null,
                _system ?? Unknown,
                _system != null,
                Version,
                _features,
                _isCase,
                _isBatch,
                Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                Outcome,
                Outcome == "success" ? 0 : 1,
                _type == ExecutionType.Run ? 1 : 0,
                AssertionsExecuted
            );
    }

    private static string? Known(string? value)
    {
        if (value is not { Length: > 0 and <= 128 })
            return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static MetaDataConfig? ReadMetadata(Context context)
    {
        try
        {
            return (context as InternalContext)?.GetMetaDataFromContext();
        }
        catch (Exception exception)
            when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not AccessViolationException;
}
