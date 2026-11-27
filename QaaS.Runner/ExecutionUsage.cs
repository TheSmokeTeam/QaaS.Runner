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
    private const string Unknown = "Unknown";
    private static readonly string Version = typeof(Execution)
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
        var config = context.RootConfiguration;
        _team = Known(metadata?.Team, config?["MetaData:Team"]);
        _system = Known(metadata?.System, config?["MetaData:System"]);
        var project = metadata?.ExtraLabels?.GetValueOrDefault("Project") as string;
        _project = Known(project, config?["MetaData:ExtraLabels:Project"]);

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
        // The Elastic formatter writes both rendered text and structured properties. Render only the event and ID.
        var fields = new List<KeyValuePair<string, object?>>(start ? 14 : 19)
        {
            new("QaasEvent", start ? "RunnerExecutionStarted" : "RunnerExecutionFinished"),
            new("AttemptId", _attemptId),
            new("{OriginalFormat}", "{QaasEvent} {AttemptId}"),
            new("ExecutionType", _type),
            new("Team", _team ?? Unknown),
            new("TeamKnown", _team != null),
            new("Project", _project ?? Unknown),
            new("ProjectKnown", _project != null),
            new("System", _system ?? Unknown),
            new("SystemKnown", _system != null),
            new("Version", Version),
            new("Features", _features),
            new("IsCase", _isCase),
            new("IsBatch", _isBatch),
        };
        if (!start)
        {
            fields.Add(new("DurationMs", Stopwatch.GetElapsedTime(_started).TotalMilliseconds));
            fields.Add(new("Outcome", Outcome));
            fields.Add(new("Failed", Outcome == "success" ? 0 : 1));
            fields.Add(new("TestsExecuted", _type == ExecutionType.Run ? 1 : 0));
            fields.Add(new("AssertionsExecuted", AssertionsExecuted));
        }
        _logger.Log(LogLevel.Debug, default, fields, null, static (values, _) =>
            $"{values[0].Value} {values[1].Value}"
        );
    }

    private static string? Known(string? configured, string? fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        if (value is not { Length: > 0 and <= 128 } || value.Any(char.IsControl))
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
