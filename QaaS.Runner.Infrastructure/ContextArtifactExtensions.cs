using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Stores rendered templates and session log artifacts inside the shared execution context.
/// </summary>
public static class ContextArtifactExtensions
{
    private const string ArtifactsRootKey = "__RunnerArtifacts";
    private const string ScopedArtifactsKey = "Scoped";
    private const string RenderedTemplateKey = "RenderedTemplate";
    private const string SessionLogsKey = "SessionLogs";

    /// <summary>
    /// Saves the rendered configuration template for the current execution.
    /// </summary>
    public static void SetRenderedConfigurationTemplate(this Context context, string template)
    {
        context.InsertValueIntoGlobalDictionary(GetRenderedTemplatePath(context), template);
    }

    /// <summary>
    /// Returns the rendered configuration template if it was captured during execution build.
    /// </summary>
    public static string? GetRenderedConfigurationTemplate(this Context context)
    {
        return TryGetValue<string>(context, GetRenderedTemplatePath(context)) ??
               TryGetValue<string>(context, [ArtifactsRootKey, RenderedTemplateKey]);
    }

    /// <summary>
    /// Appends a log line for a specific session.
    /// </summary>
    public static void AppendSessionLog(this Context context, string sessionName, string message)
    {
        if (string.IsNullOrWhiteSpace(sessionName) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var sessionLogs = GetOrCreateSessionLogStore(context);
        var queue = sessionLogs.GetOrAdd(sessionName, _ => new ConcurrentQueue<string>());
        queue.Enqueue(message);
    }

    /// <summary>
    /// Returns the concatenated session log text, or null when no log lines were captured.
    /// </summary>
    public static string? GetSessionLog(this Context context, string sessionName)
    {
        if (!TryGetSessionLogStore(context, GetSessionLogsPath(context), out var sessionLogs) &&
            !TryGetSessionLogStore(context, [ArtifactsRootKey, SessionLogsKey], out sessionLogs))
        {
            return null;
        }

        if (!sessionLogs.TryGetValue(sessionName, out var queue))
        {
            return null;
        }

        var lines = queue.ToArray();
        if (lines.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static ConcurrentDictionary<string, ConcurrentQueue<string>> GetOrCreateSessionLogStore(Context context)
    {
        if (TryGetSessionLogStore(context, GetSessionLogsPath(context), out var existingStore))
        {
            return existingStore;
        }

        var newStore = new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.Ordinal);
        context.InsertValueIntoGlobalDictionary(GetSessionLogsPath(context), newStore);
        return newStore;
    }

    private static bool TryGetSessionLogStore(Context context, List<string> path,
        out ConcurrentDictionary<string, ConcurrentQueue<string>> sessionLogs)
    {
        sessionLogs = default!;
        var existingStore = TryGetValue<object>(context, path);
        if (existingStore is not ConcurrentDictionary<string, ConcurrentQueue<string>> typedStore)
        {
            return false;
        }

        sessionLogs = typedStore;
        return true;
    }

    private static T? TryGetValue<T>(Context context, List<string> path) where T : class
    {
        try
        {
            return context.GetValueFromGlobalDictionary(path) as T;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static List<string> GetRenderedTemplatePath(Context context)
    {
        return [ArtifactsRootKey, ScopedArtifactsKey, GetArtifactScopeKey(context), RenderedTemplateKey];
    }

    private static List<string> GetSessionLogsPath(Context context)
    {
        return [ArtifactsRootKey, ScopedArtifactsKey, GetArtifactScopeKey(context), SessionLogsKey];
    }

    private static string GetArtifactScopeKey(Context context)
    {
        if (!string.IsNullOrWhiteSpace(context.ExecutionId) || !string.IsNullOrWhiteSpace(context.CaseName))
        {
            return $"{context.ExecutionId ?? "<null>"}::{context.CaseName ?? "<null>"}";
        }

        return $"context::{RuntimeHelpers.GetHashCode(context):X8}";
    }
}
