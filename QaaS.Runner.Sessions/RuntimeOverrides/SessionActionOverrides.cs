using Microsoft.Extensions.Logging;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.ConfigurationObjects;

namespace QaaS.Runner.Sessions.RuntimeOverrides;

internal sealed record ConsumerOverrideRequest(string ActionName, IReaderConfig Configuration, ILogger Logger,
    DataFilter DataFilter);

internal sealed record PublisherOverrideRequest(string ActionName, ISenderConfig Configuration, bool UseChunks,
    ILogger Logger, DataFilter DataFilter);

internal sealed record TransactionOverrideRequest(string ActionName, ITransactorConfig Configuration, ILogger Logger,
    TimeSpan Timeout);

internal sealed record CollectorOverrideRequest(string ActionName, IFetcherConfig Configuration, ILogger Logger);

internal sealed record MockerCommandOverrideRequest(string ActionName, int Stage, object SupportedCommand,
    MockerCommandConfig Command, RedisConfig Redis, string ServerName, int RequestDurationMs, int RequestRetries,
    ILogger Logger);

/// <summary>
/// Stores optional factory overrides for session actions in the runtime context.
/// Tests use these hooks to inject deterministic transports and action implementations without
/// mutating the production builders or protocol registration flow.
/// </summary>
internal sealed class SessionActionOverrides
{
    public Func<ConsumerOverrideRequest, (IReader? Reader, IChunkReader? ChunkReader)>? Consumer { get; init; }

    public Func<PublisherOverrideRequest, (ISender? Sender, IChunkSender? ChunkSender)>? Publisher { get; init; }

    public Func<TransactionOverrideRequest, ITransactor>? Transaction { get; init; }

    public Func<CollectorOverrideRequest, IFetcher>? Collector { get; init; }

    public Func<MockerCommandOverrideRequest, StagedAction>? MockerCommand { get; init; }
}

internal static class SessionActionOverrideExtensions
{
    private const string OverridesKey = "QaaS.Runner.Sessions.SessionActionOverrides";

    /// <summary>
    /// Persists the current session-action override set on the shared internal context so the
    /// builders can swap real runtime dependencies for test doubles on demand.
    /// </summary>
    public static void SetSessionActionOverrides(this InternalContext context, SessionActionOverrides overrides)
    {
        context.InternalGlobalDict ??= new Dictionary<string, object?>();
        context.InternalGlobalDict[OverridesKey] = overrides;
    }

    /// <summary>
    /// Retrieves the current session-action overrides if the context contains a correctly typed
    /// override payload. Missing or mismatched entries are treated as "no overrides configured".
    /// </summary>
    public static SessionActionOverrides? GetSessionActionOverrides(this InternalContext context)
    {
        if (context.InternalGlobalDict == null)
        {
            return null;
        }

        return context.InternalGlobalDict.TryGetValue(OverridesKey, out var overrides)
            ? overrides as SessionActionOverrides
            : null;
    }
}
