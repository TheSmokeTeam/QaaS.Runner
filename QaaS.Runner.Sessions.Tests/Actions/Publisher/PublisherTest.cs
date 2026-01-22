using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Moq;
using MoreLinq.Extensions;
using NUnit.Framework;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.MetaDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.Tests.Actions.Utils;

namespace QaaS.Runner.Sessions.Tests.Actions.Publisher;

[TestFixture]
public class PublisherTest
{
    private static Mock<ISender>? _sender;
    private static Mock<IChunkSender>? _chunkSender;
    private static List<Data<object>> _infoSent = [];

    private static Sessions.Actions.Publishers.BasePublisher InitSingleMessageWithLoop(
        string[]? dsPatterns,
        string[]? dsNames,
        int maxAmountOfMessages,
        int msgPerSec = 100)
    {
        _infoSent = CreationalFunctions.InitSender(ref _sender!);

        return new Sessions.Actions.Publishers.Publisher(
            "TestPub", _sender!.Object, 0, new DataFilter() { Body = true, Timestamp = true, MetaData = true },
            new CountPolicy(maxAmountOfMessages).Add(new LoadBalancePolicy(msgPerSec, 1000)),
            true, null, 0, 0, null, dsPatterns, dsNames,
            Globals.Logger);
    }

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void
        TestActAndInitializeIterableSerializableSaveIterator_ReceivesValidDataSourceNamesAndAppropriateFiltersAndLoopPolicy_SendsTheProperDataToTheSenderObject(
            List<string> names,
            List<string> patterns,
            List<DataSource> dataSources,
            List<Data<object>> expectedData)
    {
        // Arrange
        var amountOfDataToSend = expectedData.Count;
        var publisher = InitSingleMessageWithLoop(patterns.ToArray(), names.ToArray(), amountOfDataToSend);

        // Act
        publisher.InitializeIterableSerializableSaveIterator([], dataSources);
        publisher.Act();

        // Assert
        var orderedExpectedData = expectedData.Select(d => d.Body).Order();
        var orderedReceivedData = _infoSent.Select(d => d.Body).Order();
        CollectionAssert.AreEqual(orderedExpectedData, orderedReceivedData);
    }

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void
        TestAct_ReceivesValidDataSourceNamesAndAppropriateFiltersWithGenerators_CallsTheSendFunctionAnAppropriateAmountOfTimes(
            List<string> names,
            List<string> patterns,
            List<DataSource> dataSources,
            List<Data<object>> expectedData)
    {
        // Arrange
        const int numberOfIterations = 2;
        var publisher = CreationalFunctions.CreatePublisherWithIterations(
            ref _sender!,
            ref _infoSent,
            patterns.ToArray(),
            names.ToArray(),
            numberOfIterations);

        // Act
        publisher.InitializeIterableSerializableSaveIterator([], dataSources);
        publisher.Act();

        // Assert
        _sender.Verify(s => s.Send(It.IsAny<Data<object>>()), Times.Exactly(expectedData.Count * numberOfIterations));
    }

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void
        TestAct_ReceivesValidDataSourceNamesAndAppropriateFiltersWithGeneratorsAndConfiguredToSendChunk_CallsTheSendFunctionAnAppropriateAmountOfTimes(
            List<string> names,
            List<string> patterns,
            List<DataSource> dataSources,
            List<Data<object>> expectedData)
    {
        // Arrange
        const int chunkSize = 5;
        var pub = CreationalFunctions.CreateChunkPublisherWithIterations(
            ref _chunkSender!,
            patterns.ToArray(),
            names.ToArray(),
            chunkSize);

        // Act
        pub.InitializeIterableSerializableSaveIterator([], dataSources);
        pub.Act();

        // Assert
        int numberOfChunks = expectedData.Count / chunkSize;
        if (expectedData.Count % chunkSize != 0)
            numberOfChunks++;
        _chunkSender!.Verify(cs => cs.SendChunk(It.IsAny<IEnumerable<Data<object>>>()), Times.Exactly(numberOfChunks));
    }

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void TestExportRunningCommunicationData_ReceivesRcdToExport_ExportTheGivenDataToTheRcd(
        List<string> names,
        List<string> patterns,
        List<DataSource> dataSources,
        List<Data<object>> expectedData)
    {
        // Arrange
        const string sessionName = "test session";
        var context = CreationalFunctions.CreateContext(sessionName, dataSources);
        var publisher = CreationalFunctions.CreatePublisherWithIterations(
            ref _sender!,
            ref _infoSent,
            patterns.ToArray(),
            names.ToArray(),
            1);

        // Act
        publisher.ExportRunningCommunicationData(context, sessionName);
        publisher.InitializeIterableSerializableSaveIterator([], dataSources);
        publisher.Act();

        // Arrange
        var expectedDataContent = expectedData.Select(data => data.Body);
        var receivedDataContent = context.InternalRunningSessions.RunningSessionsDict[sessionName].Inputs![0].GetData()
            .Select(data => data.Body);
        CollectionAssert.AreEquivalent(expectedDataContent, receivedDataContent);
    }

    private const int TimeoutMsForWork = 100;

    private readonly FieldInfo _iterableSerializableSaveIteratorField =
        typeof(Sessions.Actions.Publishers.Publisher).GetField("IterableSerializableSaveIterator",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Test,
     TestCase(1, 5, 5),
     TestCase(5, 5, 5),
     TestCase(10, 5, 5),
     TestCase(100, 5, 5),
     TestCase(10, 50, 5),
     TestCase(10, 50, 50),
     TestCase(10, 5, 50),
     TestCase(2, 5, 5)]
    public void TestPublish_CallChunkPublisherWithDifferentParallelism_ExpectToSendAllChunksInMatchingParallelism(
        int parallelism, int chunkSize, int numberOfChunks)
    {
        // arrange
        var activeThreads = 0;
        var maxActiveThreads = 0;
        var dataIterated = 0;
        var dataToPublish = Enumerable.Range(0, chunkSize * numberOfChunks)
            .Select(_ => new Data<object>
            {
                MetaData = new MetaData(),
                Body = "A body of a message that is being published in chunks"
            });
        var chunkSenderMock = new Mock<IChunkSender>();
        chunkSenderMock.Setup(chunkSender =>
                chunkSender.SendChunk(It.IsAny<IEnumerable<Data<object>>>()))
            .Callback(() =>
            {
                Interlocked.Increment(ref activeThreads);
                if (maxActiveThreads < activeThreads)
                    Interlocked.Exchange(ref maxActiveThreads, activeThreads);
                Thread.Sleep(TimeoutMsForWork);
                Interlocked.Decrement(ref activeThreads);
            })
            .Returns(() =>
            {
                var chunkSentTimestamp = DateTime.UtcNow;
                return dataToPublish.Slice(Interlocked.Add(ref dataIterated, chunkSize) - chunkSize, chunkSize)
                    .Select(data => data.CloneDetailed(chunkSentTimestamp));
            }).Verifiable();

        Sessions.Actions.Publishers.ChunkPublisher publisher = new("test", chunkSenderMock.Object, 0, new DataFilter(),
            null, parallelism, chunkSize, false, 1, 1, null, [], [], Globals.Logger);
        var testActData = new InternalCommunicationData<object>
        {
            Input = new List<DetailedData<object>>(),
            InputSerializationType = SerializationType.Json
        };
        _iterableSerializableSaveIteratorField.SetValue(publisher,
            new IterableSerializableDataIterator(dataToPublish,
                SerializerFactory.BuildSerializer(SerializationType.Json)));

        // act
        typeof(Sessions.Actions.Publishers.ChunkPublisher).GetMethod("Publish",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(publisher, [testActData]);

        // assert
        chunkSenderMock.Verify(
            chunkSender => chunkSender.SendChunk(It.IsAny<IEnumerable<Data<object>>>()),
            Times.Exactly(numberOfChunks));
        Assert.That(testActData.Input.Count, Is.EqualTo(numberOfChunks * chunkSize));
        if (numberOfChunks < parallelism)
            Assert.That(maxActiveThreads,
                Is.InRange(Math.Min(numberOfChunks, Environment.ProcessorCount),
                    Math.Max(numberOfChunks, Environment.ProcessorCount)));
        else
            Assert.That(maxActiveThreads, Is.InRange(Math.Min(parallelism, Environment.ProcessorCount),
                Math.Max(parallelism, Environment.ProcessorCount)));
    }

    [Test,
     TestCase(1, 5),
     TestCase(5, 5),
     TestCase(10, 5),
     TestCase(100, 5),
     TestCase(10, 50),
     TestCase(10, 100),
     TestCase(2, 5)]
    public void TestPublish_CallPublisherWithDifferentParallelism_ExpectToSendAllItemsInMatchingParallelism(
        int parallelism, int numberOfItems)
    {
        // arrange
        var activeThreads = 0;
        var maxActiveThreads = 0;
        var dataIterated = 0;
        var dataToPublish = Enumerable.Range(0, numberOfItems)
            .Select(_ => new Data<object>
            {
                MetaData = new MetaData(),
                Body = "A body of a message that is being published in chunks"
            }).ToArray();
        var senderMock = new Mock<ISender>();
        senderMock.Setup(sender => sender.Send(It.IsAny<Data<object>>()))
            .Callback(() =>
            {
                Interlocked.Increment(ref activeThreads);
                if (maxActiveThreads < activeThreads)
                    Interlocked.Exchange(ref maxActiveThreads, activeThreads);
                Thread.Sleep(TimeoutMsForWork); // some work..
                Interlocked.Decrement(ref activeThreads);
            })
            .Returns(() => dataToPublish[Interlocked.Increment(ref dataIterated) - 1].CloneDetailed())
            .Verifiable();

        Sessions.Actions.Publishers.Publisher publisher = new("test", senderMock.Object, 0, new DataFilter(), null,
            false, parallelism, 1, 0, null, [], [], Globals.Logger);
        var testActData = new InternalCommunicationData<object>
        {
            Input = [],
            InputSerializationType = SerializationType.Json
        };
        _iterableSerializableSaveIteratorField.SetValue(publisher,
            new IterableSerializableDataIterator(dataToPublish,
                SerializerFactory.BuildSerializer(SerializationType.Json)));

        // act
        typeof(Sessions.Actions.Publishers.Publisher).GetMethod("Publish",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(publisher, [testActData]);

        // assert
        senderMock.Verify(sender => sender.Send(It.IsAny<Data<object>>()),
            Times.Exactly(numberOfItems));
        Assert.That(testActData.Input.Count, Is.EqualTo(numberOfItems));
        // if parallelism configured not possible due to machine limitations:
        if (numberOfItems < parallelism)
            Assert.That(maxActiveThreads,
                Is.InRange(Math.Min(numberOfItems, Environment.ProcessorCount),
                    Math.Max(numberOfItems, Environment.ProcessorCount)));
        else
            Assert.That(maxActiveThreads, Is.InRange(Math.Min(parallelism, Environment.ProcessorCount),
                Math.Max(parallelism, Environment.ProcessorCount)));
    }
}