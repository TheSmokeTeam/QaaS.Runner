using Microsoft.Extensions.Logging;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;

namespace QaaS.Runner.Sessions.Actions.Collectors;

public class Collector : Action
{
    private readonly int _collectionEndTimeOffset;

    private readonly int _collectionStartTimeOffset;
    private readonly DataFilter _dataFilter;
    private readonly uint _endTimeReachedCheckIntervalMs;
    private readonly IFetcher _fetcher;
    private DateTime _sessionEndTime;
    private DateTime _sessionStartTime;


    public Collector(string name, IFetcher fetcher, DataFilter dataFilter, int collectionStartTimeOffset,
        int collectionEndTimeOffset,
        uint endTimeReachedCheckIntervalMs, ILogger logger) : base(name, logger)
    {
        _fetcher = fetcher;
        _dataFilter = dataFilter;
        _collectionStartTimeOffset = collectionStartTimeOffset;
        _collectionEndTimeOffset = collectionEndTimeOffset;
        _endTimeReachedCheckIntervalMs = endTimeReachedCheckIntervalMs;
        Logger.LogDebug("Initializing Collector {Name} with fetcher of type {FetcherType}", Name, fetcher.GetType());
    }

    public SerializationType? GetCommunicationSerializationType()
    {
        return _fetcher.GetSerializationType();
    }

    protected virtual DateTime GetCurrentUtcTime()
    {
        return DateTime.UtcNow;
    }

    internal void SetCollectionTimes(DateTime sessionStartTime, DateTime sessionEndTime)
    {
        _sessionStartTime = sessionStartTime;
        _sessionEndTime = sessionEndTime;
    }

    internal override InternalCommunicationData<object> Act()
    {
        // collector only initialize output
        var data = new InternalCommunicationData<object>
        {
            Output = new List<DetailedData<object>?>(),
            OutputSerializationType = GetCommunicationSerializationType()
        };
        var collectionStartTimeUtc = _sessionStartTime + TimeSpan.FromMilliseconds(_collectionStartTimeOffset);
        var collectionEndTimeUtc = _sessionEndTime + TimeSpan.FromMilliseconds(_collectionEndTimeOffset);
        Logger.LogInformation("Collector {CollectorName} of type {CollectorType} will collect from" +
                              " {CollectionStartTimeUtc} UTC to {CollectionEndTimeUtc} UTC",
            Name, GetType().Name, collectionStartTimeUtc, collectionEndTimeUtc);
        if (collectionStartTimeUtc > collectionEndTimeUtc)
            throw new ArgumentException(
                $"The collection start time ({collectionStartTimeUtc}) is bigger than the collection end time ({collectionEndTimeUtc})," +
                $" check your collection range configurations.");

        var currentUtcTime = GetCurrentUtcTime();
        while (currentUtcTime < collectionEndTimeUtc)
        {
            Logger.LogDebug("Current UTC time is {CurrentUtcTime}, waiting for it to be bigger than" +
                            " collection end time UTC {CollectionEndTimeUtc}, sleeping for" +
                            " {EndTimeReachedCheckIntervalMs} milliseconds before checking again",
                currentUtcTime, collectionEndTimeUtc, _endTimeReachedCheckIntervalMs);
            Thread.Sleep(TimeSpan.FromMilliseconds(_endTimeReachedCheckIntervalMs));
            currentUtcTime = GetCurrentUtcTime();
        }

        data.Output = _fetcher.Collect(collectionStartTimeUtc, collectionEndTimeUtc)
            .Select(outputData => outputData.FilterData(_dataFilter)).ToList()!;
        return data;
    }
}