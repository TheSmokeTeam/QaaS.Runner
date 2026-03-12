using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Session;
using QaaS.Runner.Sessions.Tests.Actions;
using QaaS.Runner.Sessions.Tests.Actions.Utils;

namespace QaaS.Runner.Sessions.Tests.Session;

[TestFixture]
public class SessionTests
{
    private const int ConsumeMessageCount = 20;

    private static Mock<IReader> _reader = null!;
    private static Mock<IChunkReader> _chunkReader = null!;

    private static Mock<ISender> _sender = null!;
    private static Mock<IChunkSender> _chunkSender = null!;
    private static List<Data<object>> _sentData = null!;

    private static Mock<ITransactor> _transactor = null!;
    private static List<Data<object>> _transactionSentData = null!;

    private string _messageToRead = "return message";
    
    private Sessions.Session.Session CreateSession(
        InternalContext context,
        string sessionName,
        int consumeMsgAmount,
        string dataReadersReturn,
        int publisherChunkSize,
        List<string> names,
        List<string> patterns)
    {
        var stage1 = new Stage(context, [], sessionName, 0, 0, 0);
        stage1.AddCommunication(
            CreationalFunctions.CreateConsumer(
                ref _reader!,
                _messageToRead,
                consumeMsgAmount));
        stage1.AddCommunication(
            CreationalFunctions.CreateChunkConsumer(
                ref _chunkReader!,
                _messageToRead,
                consumeMsgAmount));

        var stage2 = new Stage(context, [], sessionName, 1, 0, 0);
        stage2.AddCommunication(
            CreationalFunctions.CreatePublisherWithIterations(
                ref _sender!,
                ref _sentData,
                patterns.ToArray(),
                names.ToArray(),
                1));
        stage2.AddCommunication(
            CreationalFunctions.CreateChunkPublisherWithIterations(
                ref _chunkSender!,
                patterns.ToArray(),
                names.ToArray(),
                publisherChunkSize));


        var stage3 = new Stage(context, [], sessionName, 2, 0, 0);
        stage3.AddCommunication(
            CreationalFunctions.CreateTransactorWithLoop(
                ref _transactor!,
                dataReadersReturn,
                ref _transactionSentData,
                patterns.ToArray(),
                names.ToArray(),
                consumeMsgAmount));
        
        return new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            new Dictionary<int, Stage> { { 0, stage1 }, { 1, stage2 }, { 2, stage3 } },
            [], // add collector
            context,
            []);
    }

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void Run_WithAllActions_ShouldCallReadersCorrectAmountOfTimes(
        List<string> datasourceNames,
        List<string> dataSourcePatterns,
        List<DataSource> dataSources,
        List<Data<object>> expectedData)
    {
        // Arrange
        const int consumeMsgAmount = ConsumeMessageCount;
        const int pubChunkSize = 5;
        const string dataReadersReturn = "test";
        const string sessionName = "test session";
        var context = CreationalFunctions.CreateContext(sessionName, dataSources);

        var session = CreateSession(
            context,
            sessionName,
            consumeMsgAmount, dataReadersReturn,
            pubChunkSize,
            datasourceNames, dataSourcePatterns);

        // Act
        session.Run(context.ExecutionData);

        // Assert
        _reader!.Verify(r => r.Read(It.IsAny<TimeSpan>()), Times.Exactly(consumeMsgAmount));
        _chunkReader!.Verify(r => r.ReadChunk(It.IsAny<TimeSpan>()), Times.Once);
    }

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void Run_WithAllActions_ShouldCallSendersCorrectAmountOfTimes(
        List<string> datasourceNames,
        List<string> dataSourcePatterns,
        List<DataSource> dataSources,
        List<Data<object>> expectedData)
    {
        // Arrange
        const int consumeMsgAmount = ConsumeMessageCount;
        const int pubChunkSize = 5;
        const string dataReadersReturn = "test";
        const string sessionName = "test session";
        var context = CreationalFunctions.CreateContext(sessionName, dataSources);

        var session = CreateSession(
            context,
            sessionName,
            consumeMsgAmount, dataReadersReturn,
            pubChunkSize,
            datasourceNames, dataSourcePatterns);

        // Act
        session.Run(context.ExecutionData);

        // Assert
        var expectedSentData = expectedData.Select(d => d.Body).Order().ToList();
        var orderedReceivedData = _sentData.Select(d => d.Body).Order().ToList();
        CollectionAssert.AreEqual(expectedSentData, orderedReceivedData);

        int numberOfChunks = expectedData.Count / pubChunkSize;
        if (expectedData.Count % pubChunkSize != 0)
            numberOfChunks++;
        _chunkSender!.Verify(cs => cs.SendChunk(It.IsAny<IEnumerable<Data<object>>>()), Times.Exactly(numberOfChunks));
    }
    
    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void Run_WithAllActions_ShouldCallTransactorsCorrectAmountOfTimes(
        List<string> datasourceNames,
        List<string> dataSourcePatterns,
        List<DataSource> dataSources,
        List<Data<object>> expectedSentData)
    {
        // Arrange
        const int consumeMsgAmount = ConsumeMessageCount;
        const int pubChunkSize = 5;
        const string dataReadersReturn = "test";
        const string sessionName = "test session";
        var context = CreationalFunctions.CreateContext(sessionName, dataSources);

        var session = CreateSession(
            context,
            sessionName,
            consumeMsgAmount, dataReadersReturn,
            pubChunkSize,
            datasourceNames, dataSourcePatterns);

        // Act
        session.Run(context.ExecutionData);

        // Assert
        // the transaction reader returns the string you configured it to return for each data it gets
        _transactor!.Verify(t => t.Transact(It.IsAny<Data<object>>()), Times.Exactly(consumeMsgAmount));
    }
    
    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void Run_WithAllActions_ShouldLogAllCommunicationDataTOTheSessionDataObject(
        List<string> datasourceNames,
        List<string> dataSourcePatterns,
        List<DataSource> dataSources,
        List<Data<object>> expectedSentData)
    {
        // Arrange
        const int consumeMsgAmount = ConsumeMessageCount;
        const int pubChunkSize = 5;
        const string dataReadersReturn = "test";
        const string sessionName = "test session";
        var sentMsgAmount = expectedSentData.Count;
        var context = CreationalFunctions.CreateContext(sessionName, dataSources);

        var session = CreateSession(
            context,
            sessionName,
            consumeMsgAmount, dataReadersReturn,
            pubChunkSize,
            datasourceNames, dataSourcePatterns);

        // Act
        var sessionData = session.Run(context.ExecutionData);

        // Assert
        var inputCount = sessionData!.Inputs!
            .Select(cd => cd.Data)
            .SelectMany(d => d).Count(); 
        
        Assert.That(inputCount, Is.EqualTo(
            sentMsgAmount * 2 + consumeMsgAmount)); // number of messages sent + chunk sent + transaction sent 

        var outputCount = sessionData.Outputs!
            .Select(cd => cd.Data)
            .SelectMany(d => d).Count();
        
        Assert.That(outputCount, Is.EqualTo(
            consumeMsgAmount * 2 + 2)); } // number of messages read + chunk read + transaction read 

    [Test]
    public void Run_WhenSaveDataIsFalse_ReturnsNullAndRemovesRunningSessionEntry()
    {
        const string sessionName = "no-save-session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var stage = new Stage(context, [], sessionName, 0, 0, 0);
        var session = new Sessions.Session.Session(
            sessionName,
            0,
            false,
            0,
            0,
            new Dictionary<int, Stage> { { 0, stage } },
            [],
            context,
            []);

        var sessionData = session.Run(context.ExecutionData);

        Assert.That(sessionData, Is.Null);
        Assert.That(context.InternalRunningSessions.RunningSessionsDict.ContainsKey(sessionName), Is.False);
    }

    [Test]
    public void Run_WithMultipleStages_CompletesEarlierStageBeforeStartingNextStage()
    {
        const string sessionName = "ordered-session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var stage1Completed = 0;
        var stage2StartedBeforeStage1Completed = false;

        var stage1 = new Stage(context, [], sessionName, 0, 0, 0);
        stage1.AddCommunication(new RecordingAction("stage-1", 0, Globals.Logger, () =>
        {
            Thread.Sleep(75);
            Interlocked.Exchange(ref stage1Completed, 1);
        }));

        var stage2 = new Stage(context, [], sessionName, 1, 0, 0);
        stage2.AddCommunication(new RecordingAction("stage-2", 1, Globals.Logger, () =>
        {
            stage2StartedBeforeStage1Completed = Interlocked.CompareExchange(ref stage1Completed, 0, 0) == 0;
        }));

        var session = new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            new Dictionary<int, Stage> { { 0, stage1 }, { 1, stage2 } },
            [],
            context,
            []);

        session.Run(context.ExecutionData);

        Assert.That(stage2StartedBeforeStage1Completed, Is.False);
    }

    private sealed class RecordingAction(string name, int stage, Microsoft.Extensions.Logging.ILogger logger, System.Action callback)
        : StagedAction(name, stage, null, logger)
    {
        internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
        {
        }

        internal override InternalCommunicationData<object> Act()
        {
            callback();
            return new InternalCommunicationData<object>();
        }

        protected internal override void LogData(InternalCommunicationData<object> actData,
            DetailedData<object> itemBeforeSerialization, InputOutputState? saveAt = null)
        {
        }
    }
}
