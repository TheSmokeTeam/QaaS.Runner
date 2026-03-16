using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.MetaDataObjects;

namespace QaaS.Runner.Sessions.Tests.Actions.Collector;

[TestFixture]
public class CollectorTests
{
    private Mock<IFetcher>? _fetcher;
    private static readonly string[] expectedBody = ["test1", "test2"];

    private void InitFetcher()
    {
        _fetcher = new Mock<IFetcher>();
        _fetcher.Setup(f => f.Collect(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns(expectedBody.Select(d => new DetailedData<object>{Body = d, Timestamp = DateTime.UtcNow, MetaData = new MetaData()}));
    }
    
    private Sessions.Actions.Collectors.Collector InitCollector(
        IFetcher fetcher,
        int endTimeOffset,
        DateTime sessionTime,
        int sessionLength)
    {
        var collector = new Sessions.Actions.Collectors.Collector(
            "test collector",
            fetcher,
            new DataFilter {Body = true, Timestamp = false, MetaData = false},
            0,
            endTimeOffset,
            1,
            NullLogger.Instance);
        collector.SetCollectionTimes(sessionTime, sessionTime.AddSeconds(sessionLength));
        return collector;
    }

    [Test]
    public void TestAct_WaitsUntilCollectionEndTime_AndReturnsFilteredBodies()
    {
        // Arrange
        InitFetcher();
        const int endTimeOffset = 50;
        var sw = Stopwatch.StartNew();
        var sessionStartTime = DateTime.UtcNow;
        const int sessionLength = 0;
        var collector = InitCollector(_fetcher!.Object, endTimeOffset, sessionStartTime , sessionLength);

        // Act
        var collectedData = collector.Act();
        sw.Stop();

        var receivedBody =
            collectedData.Output!.Select(cd => cd!.Body).Order();
        var allMetadataFiltered =
            collectedData.Output!.All(cd => cd!.MetaData == null);
        var allTimestampFiltered =
            collectedData.Output!.All(cd => cd!.Timestamp == new DateTime?());
        
        // Assert
        Assert.That(sessionStartTime.AddMilliseconds(sw.ElapsedMilliseconds) >= sessionStartTime.AddSeconds(sessionLength).AddMilliseconds(endTimeOffset));
        CollectionAssert.AreEqual(receivedBody, expectedBody);
        Assert.That(allMetadataFiltered, Is.True);
        Assert.That(allTimestampFiltered, Is.True);
    }

    [Test]
    public void Act_WhenCollectionStartIsAfterCollectionEnd_ThrowsArgumentException()
    {
        InitFetcher();
        var collector = new Sessions.Actions.Collectors.Collector(
            "test collector",
            _fetcher!.Object,
            new DataFilter(),
            100,
            0,
            1,
            NullLogger.Instance);
        var now = DateTime.UtcNow;
        collector.SetCollectionTimes(now, now);

        Assert.Throws<ArgumentException>(() => collector.Act());
    }
}
