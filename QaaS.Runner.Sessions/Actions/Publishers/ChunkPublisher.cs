using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Policies;
using QaaS.Framework.Policies.Exceptions;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.Extensions;

[assembly: InternalsVisibleTo("QaaS.Runner.Sessions.Tests")]

namespace QaaS.Runner.Sessions.Actions.Publishers;

public sealed class ChunkPublisher : BasePublisher
{
    private readonly IChunkSender? _chunkSender;
    private readonly int _chunkSize;
    private readonly long? _maxChunkBytes;

    public ChunkPublisher(
        string name,
        IChunkSender? dataChunkSender,
        int stage,
        DataFilter dataFilter,
        Policy? policies,
        int? parallelism,
        int chunkSize,
        bool loop,
        int iterations,
        ulong sleepTimeMs,
        SerializationType? serializationType,
        string[]? dataSourcePatterns,
        string[]? dataSourceNames,
        ILogger logger
    )
        : this(
            name,
            dataChunkSender,
            stage,
            dataFilter,
            policies,
            parallelism,
            chunkSize,
            loop,
            iterations,
            sleepTimeMs,
            serializationType,
            dataSourcePatterns,
            dataSourceNames,
            logger,
            null
        ) { }

    public ChunkPublisher(
        string name,
        IChunkSender? dataChunkSender,
        int stage,
        DataFilter dataFilter,
        Policy? policies,
        int? parallelism,
        int chunkSize,
        bool loop,
        int iterations,
        ulong sleepTimeMs,
        SerializationType? serializationType,
        string[]? dataSourcePatterns,
        string[]? dataSourceNames,
        ILogger logger,
        long? maxChunkBytes
    )
        : base(
            name,
            stage,
            dataFilter,
            dataSourceNames,
            dataSourcePatterns,
            parallelism,
            iterations,
            loop,
            sleepTimeMs,
            serializationType,
            policies,
            logger
        )
    {
        _chunkSender = dataChunkSender;
        _chunkSize = chunkSize;
        _maxChunkBytes = maxChunkBytes;
        Logger.LogInformation(
            "Initializing {Publisher} {PublisherName} with Sender of type"
                + " {SenderType} and Serializer {SerializerType}",
            GetType().Name,
            Name,
            _chunkSender?.GetType().Name,
            SerializationType
        );
        RunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetCommunicationSerializationType(),
        };
    }

    protected override SerializationType? GetCommunicationSerializationType() =>
        _chunkSender?.GetSerializationType() ?? SerializationType;

    /// <inheritdoc />
    internal override InternalCommunicationData<object> Act() =>
        ConnectedProtocolExecution.Run(_chunkSender, () => base.Act(), Logger);

    /// <inheritdoc />
    protected override bool Publish(InternalCommunicationData<object> actData)
    {
        var dataToPublish = IterableSerializableSaveIterator.IterateWithOriginal();
        var chunks = ByteBoundedChunks.Create(dataToPublish, _chunkSize, _maxChunkBytes);

        try
        {
            IterableSerializableSaveIterator.ApplyToAll(
                chunks,
                chunk =>
                {
                    IReadOnlyList<DetailedData<object>> sentData;
                    try
                    {
                        ParallelismSemaphore?.Wait();
                        sentData = _chunkSender!
                            .SendChunk(chunk.Select(item => item.Serialized))
                            .ToArray();
                    }
                    finally
                    {
                        ParallelismSemaphore?.Release();
                    }

                    if (sentData.Count != chunk.Length)
                    {
                        throw new InvalidOperationException(
                            $"Chunk publisher {Name} sent {chunk.Length} items but received {sentData.Count} responses."
                        );
                    }

                    foreach (
                        var pair in chunk.Zip(
                            sentData,
                            (originalAndSerialized, sentItem) =>
                                new { originalAndSerialized.Original, SentItem = sentItem }
                        )
                    )
                    {
                        LogData(actData, pair.Original.CloneDetailed(pair.SentItem.Timestamp));
                    }

                    if (Policies != null)
                    {
                        lock (Policies)
                        {
                            if (!Policies.RunChain())
                                throw new StopActionException("Policy ruled to be stopped");
                        }
                    }
                },
                Parallelism != null,
                Parallelism ?? 1
            );
        }
        catch (StopActionException)
        {
            return false;
        }

        return true;
    }
}
