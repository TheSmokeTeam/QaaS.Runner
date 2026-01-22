using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Policies;
using QaaS.Framework.Policies.Exceptions;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;

[assembly: InternalsVisibleTo("QaaS.Runner.Sessions.Tests")]

namespace QaaS.Runner.Sessions.Actions.Publishers;

public sealed class ChunkPublisher : BasePublisher
{
    private readonly IChunkSender? _chunkSender;
    private readonly int _chunkSize;

    public ChunkPublisher(string name, IChunkSender? dataChunkSender, int stage, DataFilter dataFilter,
        Policy? policies, int? parallelism, int chunkSize, bool loop, int iterations, ulong sleepTimeMs,
        SerializationType? serializationType, string[]? dataSourcePatterns, string[]? dataSourceNames,
        ILogger logger) : base(name, stage, dataFilter, dataSourceNames, dataSourcePatterns, parallelism, iterations,
        loop, sleepTimeMs, serializationType, policies, logger)
    {
        _chunkSender = dataChunkSender;
        _chunkSize = chunkSize;
        Logger.LogInformation("Initializing {Publisher} {PublisherName} with Sender of type" +
                              " {SenderType} and Serializer {SerializerType}",
            GetType().Name, Name, _chunkSender?.GetType().Name, SerializationType);
        RunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetCommunicationSerializationType()
        };
    }

    protected override SerializationType? GetCommunicationSerializationType() =>
        _chunkSender?.GetSerializationType() ?? SerializationType;


    /// <inheritdoc />
    internal override InternalCommunicationData<object> Act()
    {
        _chunkSender?.Connect();
        var data = base.Act();
        _chunkSender?.Disconnect();
        return data;
    }

    /// <inheritdoc />
    protected override bool Publish(InternalCommunicationData<object> actData)
    {
        var dataToPublish = IterableSerializableSaveIterator.IterateEnumerable();
        var chunks = dataToPublish.Chunk(_chunkSize);
        var publishedItemIndex = 0;
        try
        {
            IterableSerializableSaveIterator.ApplyToAll(chunks, chunk =>
            {
                IEnumerable<DetailedData<object>>? sentData;
                try
                {
                    ParallelismSemaphore?.Wait();
                    sentData = _chunkSender!.SendChunk(chunk);
                }
                finally
                {
                    ParallelismSemaphore?.Release();
                }

                foreach (var sentItem in sentData)
                {
                    LogData(
                        actData,
                        IterableSerializableSaveIterator.GetDataBeforeSerialization(publishedItemIndex)
                            .CloneDetailed(sentItem.Timestamp));
                    Interlocked.Increment(ref publishedItemIndex);
                }

                if (Policies?.RunChain() == false)
                    throw new StopActionException("Policy ruled to be stopped");
            }, Parallelism != null);
        }
        catch (StopActionException)
        {
            return false;
        }

        return true;
    }
}