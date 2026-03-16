using Microsoft.Extensions.Logging;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.ConfigurationObjects;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;

namespace QaaS.Runner.Sessions.RuntimeOverrides;

internal sealed record ConsumerFactoryRequest(string ActionName, IReaderConfig Configuration, ILogger Logger,
    DataFilter DataFilter);

internal sealed record PublisherFactoryRequest(string ActionName, ISenderConfig Configuration, bool UseChunks,
    ILogger Logger, DataFilter DataFilter);

internal sealed record TransactionFactoryRequest(string ActionName, ITransactorConfig Configuration, ILogger Logger,
    TimeSpan Timeout);

internal sealed record CollectorFactoryRequest(string ActionName, IFetcherConfig Configuration, ILogger Logger);

internal sealed record MockerCommandFactoryRequest(string ActionName, int Stage, object SupportedCommand,
    CommandConfig Command, RedisConfig Redis, string ServerName, int RequestDurationMs, int RequestRetries,
    ILogger Logger);

internal sealed class SessionActionFactoryOverrides
{
    public Func<ConsumerFactoryRequest, (IReader? Reader, IChunkReader? ChunkReader)>? ConsumerFactory { get; init; }

    public Func<PublisherFactoryRequest, (ISender? Sender, IChunkSender? ChunkSender)>? PublisherFactory
    {
        get;
        init;
    }

    public Func<TransactionFactoryRequest, ITransactor>? TransactionFactory { get; init; }

    public Func<CollectorFactoryRequest, IFetcher>? CollectorFactory { get; init; }

    public Func<MockerCommandFactoryRequest, StagedAction>? MockerCommandFactory { get; init; }
}

internal static class SessionActionFactoryOverrideExtensions
{
    private const string OverridesKey = "QaaS.Runner.Sessions.SessionActionFactoryOverrides";

    public static void SetSessionActionFactoryOverrides(this InternalContext context,
        SessionActionFactoryOverrides overrides)
    {
        context.InternalGlobalDict ??= new Dictionary<string, object?>();
        context.InternalGlobalDict[OverridesKey] = overrides;
    }

    public static SessionActionFactoryOverrides? GetSessionActionFactoryOverrides(this InternalContext context)
    {
        if (context.InternalGlobalDict == null)
        {
            return null;
        }

        return context.InternalGlobalDict.TryGetValue(OverridesKey, out var overrides)
            ? overrides as SessionActionFactoryOverrides
            : null;
    }
}
