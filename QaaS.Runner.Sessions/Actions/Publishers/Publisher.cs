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

public sealed class Publisher : BasePublisher
{
    private readonly ISender? _sender;

    public Publisher(
        string name,
        ISender? dataSender,
        int stage,
        DataFilter dataFilter,
        Policy? policies,
        bool loop,
        int? parallelism,
        int iterations,
        ulong sleepTimeMs,
        SerializationType? serializationType,
        string[]? dataSourcePatterns,
        string[]? dataSourceNames,
        ILogger logger
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
        _sender = dataSender;
        Logger.LogInformation(
            "Initializing {Publisher} {PublisherName} with Sender type"
                + " {SenderType} and Serializer {SerializerType}",
            GetType().Name,
            Name,
            _sender?.GetType().Name,
            SerializationType
        );
        RunningCommunicationData = new RunningCommunicationData<object>
        {
            Name = Name,
            SerializationType = GetCommunicationSerializationType(),
        };
    }

    protected override SerializationType? GetCommunicationSerializationType() =>
        _sender?.GetSerializationType() ?? SerializationType;

    /// <inheritdoc />
    internal override InternalCommunicationData<object> Act() =>
        ConnectedProtocolExecution.Run(_sender, () => base.Act(), Logger);

    /// <inheritdoc />
    protected override bool Publish(InternalCommunicationData<object> actData)
    {
        var dataToPublish = IterableSerializableSaveIterator.IterateWithOriginal();

        try
        {
            IterableSerializableSaveIterator.ApplyToAll(
                dataToPublish,
                dataPair =>
                {
                    DetailedData<object>? sentData;
                    try
                    {
                        ParallelismSemaphore?.Wait();
                        sentData = _sender!.Send(dataPair.Serialized);
                    }
                    finally
                    {
                        ParallelismSemaphore?.Release();
                    }

                    LogData(actData, dataPair.Original.CloneDetailed(sentData?.Timestamp));
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
            Logger.LogDebug("Policy ruled Publishing action to be stopped");
            return false;
        }

        return true;
    }
}
