using System.Collections.Concurrent;
using System.Text;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Stores rendered templates and session log artifacts inside the shared execution context.
/// </summary>
public static class ContextArtifactExtensions
{
    private const string ArtifactsRootKey = "__RunnerArtifacts";
    private const string RenderedTemplateKey = "RenderedTemplate";
    private const string SessionLogsKey = "SessionLogs";

    /// <summary>
    /// Saves the rendered configuration template for the current execution.
    /// </summary>
    public static void SetRenderedConfigurationTemplate(this Context context, string template)
    {
        context.InsertValueIntoGlobalDictionary([ArtifactsRootKey, RenderedTemplateKey], template);
    }

    /// <summary>
    /// Returns the rendered configuration template if it was captured during execution build.
    /// </summary>
    public static string? GetRenderedConfigurationTemplate(this Context context)
    {
        try
        {
            return context.GetValueFromGlobalDictionary([ArtifactsRootKey, RenderedTemplateKey]) as string;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
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
        var sessionLogs = GetOrCreateSessionLogStore(context);
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
        try
        {
            var existingStore = context.GetValueFromGlobalDictionary([ArtifactsRootKey, SessionLogsKey]);
            if (existingStore is ConcurrentDictionary<string, ConcurrentQueue<string>> typedStore)
            {
                return typedStore;
            }
        }
        catch (KeyNotFoundException)
        {
            // Create a new store below.
        }

        var newStore = new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.Ordinal);
        context.InsertValueIntoGlobalDictionary([ArtifactsRootKey, SessionLogsKey], newStore);
        return newStore;
    }
}
