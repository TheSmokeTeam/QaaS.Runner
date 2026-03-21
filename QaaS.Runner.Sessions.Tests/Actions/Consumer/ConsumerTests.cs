using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.Actions.Consumers;
using QaaS.Runner.Sessions.Tests.Actions.Utils;

namespace QaaS.Runner.Sessions.Tests.Actions.Consumer;

[TestFixture]
public class ConsumerTests
{
    private static Mock<IReader>? _reader;
    private static Mock<IChunkReader>? _chunkReader;

    [SetUp]
    public void InitReaders()
    {
        CreationalFunctions.InitReader(ref _reader!, "test data");
        CreationalFunctions.InitChunkReader(ref _chunkReader!, "test data");
    }

    private BaseConsumer CreateConsumer(
        IReader? reader,
        IChunkReader? chunkReader,
        int numOfMsgToSend = 100)
    {
        return reader != null
            ? new Sessions.Actions.Consumers.Consumer("TestConsumer", reader, new TimeSpan(1), 1,
                new CountPolicy(numOfMsgToSend), new DataFilter { Body = true, MetaData = false, Timestamp = false },
                SerializationType.Binary, null, Globals.Logger)
            : chunkReader != null
                ? new ChunkConsumer("TestConsumer", chunkReader, new TimeSpan(1), 1,
                    new CountPolicy(numOfMsgToSend),
                    new DataFilter { Body = true, MetaData = false, Timestamp = false },
                    SerializationType.Binary, null, Globals.Logger)
                : throw new TestCanceledException("Test cancelled due to missing reader for Consumer initiation");
    }

    [Test]
    public void TestAct_ValidConsumerParamsAnd200MessageCountPolicy_CallsTheReadFunctionAnAppropriateAmountOfTimes()
    {
        // Arrange
        const int numOfMsgToSend = 200;
        var consumer = CreateConsumer(_reader!.Object, null, numOfMsgToSend);

        // Act
        consumer.Act();

        // Assert
        _reader.Verify(r => r.Read(It.IsAny<TimeSpan>()), Times.Exactly(numOfMsgToSend));
    }

    [Test]
    public void TestAct_ConsumerReturnsNull_BreaksAfterOneCall()
    {
        // Arrange
        _reader!.Setup(s => s.Read(It.IsAny<TimeSpan>()))
            .Returns((DetailedData<object>?)null);
        var consumer = CreateConsumer(_reader.Object, null);

        // Act
        consumer.Act();

        // Assert
        _reader.Verify(r => r.Read(It.IsAny<TimeSpan>()), Times.Exactly(1));
    }

    [Test]
    public void TestAct_ChunkReaderAnd400MessageCountPolicy_CallsTheReadFunctionAnAppropriateAmountOfTimes()
    {
        // Arrange
        const int numOfMsgToRead = 200;
        const int chunkSize = 2;
        var consumer = CreateConsumer(null, _chunkReader!.Object, numOfMsgToRead * chunkSize);
        _chunkReader.Setup(r => r.ReadChunk(It.IsAny<TimeSpan>()))
            .Returns(Enumerable.Repeat(new DetailedData<object>(), numOfMsgToRead * chunkSize));

        // Act
        var consumedData = consumer.Act();

        // Assert
        _chunkReader.Verify(r => r.ReadChunk(It.IsAny<TimeSpan>()), Times.Once);
        Assert.That(consumedData.Output!.Count, Is.EqualTo(numOfMsgToRead * chunkSize));
    }


    [Test]
    public void TestExportRunningCommunicationData_ReceivesRcdToExport_ExportTheGivenDataToTheRcd()
    {
        // Arrange
        const string sessionName = "test session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var consumer = CreateConsumer(_reader!.Object, null);

        // Act
        consumer.ExportRunningCommunicationData(context, sessionName);
        consumer.Act();

        // Arrange
        var receivedAmount = context.InternalRunningSessions.RunningSessionsDict[sessionName].Outputs![0].Data.Count;
        _reader.Verify(r => r.Read(It.IsAny<TimeSpan>()), Times.Exactly(receivedAmount));
    }

    [Test]
    public void Constructor_WithNullReader_UsesConfiguredSerializationTypeForRunningData()
    {
        var consumer = new Sessions.Actions.Consumers.Consumer(
            "TestConsumer",
            null,
            TimeSpan.FromMilliseconds(1),
            1,
            null,
            new DataFilter(),
            SerializationType.Json,
            null,
            Globals.Logger);

        var runningData = (RunningCommunicationData<object>)typeof(BaseConsumer)
            .GetField("RunningCommunicationData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(consumer)!;

        Assert.That(runningData.SerializationType, Is.EqualTo(SerializationType.Json));
    }

    [Test]
    public void Constructor_WithNullChunkReader_UsesConfiguredSerializationTypeForRunningData()
    {
        var consumer = new ChunkConsumer(
            "TestConsumer",
            null,
            TimeSpan.FromMilliseconds(1),
            1,
            null,
            new DataFilter(),
            SerializationType.Json,
            null,
            Globals.Logger);

        var runningData = (RunningCommunicationData<object>)typeof(BaseConsumer)
            .GetField("RunningCommunicationData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(consumer)!;

        Assert.That(runningData.SerializationType, Is.EqualTo(SerializationType.Json));
    }

    [Test]
    public void Constructor_LogsStructuredInitializationMessage()
    {
        var logger = new CapturingLogger();

        _ = new Sessions.Actions.Consumers.Consumer(
            "TestConsumer",
            new NamedReader(),
            TimeSpan.FromMilliseconds(1),
            1,
            null,
            new DataFilter(),
            SerializationType.Json,
            null,
            logger);

        Assert.That(logger.Messages,
            Contains.Item("Initializing Consumer TestConsumer with Reader type NamedReader and Deserializer Json"));
    }

    private sealed class NamedReader : IReader
    {
        public void Connect()
        {
        }

        public void Disconnect()
        {
        }

        public SerializationType? GetSerializationType()
        {
            return SerializationType.Json;
        }

        public DetailedData<object>? Read(TimeSpan timeout)
        {
            return null;
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NoOpScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
