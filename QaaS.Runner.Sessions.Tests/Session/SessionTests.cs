using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
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
    public void Run_WithMultipleStages_StartsNextStageBeforeEarlierStageCompletesAndStillWaitsForCompletion()
    {
        const string sessionName = "ordered-session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        using var stage1Started = new ManualResetEventSlim(false);
        using var allowStage1ToFinish = new ManualResetEventSlim(false);
        var stage1Completed = 0;
        var stage2StartedBeforeStage1Completed = false;

        var stage1 = new Stage(context, [], sessionName, 0, 0, 0);
        stage1.AddCommunication(new RecordingAction("stage-1", 0, Globals.Logger, () =>
        {
            stage1Started.Set();
            allowStage1ToFinish.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Exchange(ref stage1Completed, 1);
        }));

        var stage2 = new Stage(context, [], sessionName, 1, 0, 0);
        stage2.AddCommunication(new RecordingAction("stage-2", 1, Globals.Logger, () =>
        {
            Assert.That(stage1Started.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "Stage 1 action never started before stage 2 was evaluated.");
            stage2StartedBeforeStage1Completed = Interlocked.CompareExchange(ref stage1Completed, 0, 0) == 0;
            allowStage1ToFinish.Set();
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

        Assert.That(stage2StartedBeforeStage1Completed, Is.True);
        Assert.That(stage1Completed, Is.EqualTo(1));
    }

    [Test]
    public void Run_WhenSessionCompletes_LogsSummaryCountsOnSeparateLines()
    {
        const string sessionName = "summary-session";
        var logger = new CapturingLogger();
        var context = new InternalContext
        {
            InternalRunningSessions = new RunningSessions(
                new Dictionary<string, RunningSessionData<object, object>>
                {
                    {
                        sessionName, new RunningSessionData<object, object>
                        {
                            Inputs = [],
                            Outputs = []
                        }
                    }
                }),
            ExecutionData = new QaaS.Framework.SDK.ExecutionObjects.ExecutionData { DataSources = [] },
            Logger = logger
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig());

        var stage = new Stage(context, [], sessionName, 0, 0, 0);
        var session = new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            new Dictionary<int, Stage> { { 0, stage } },
            [],
            context,
            []);

        session.Run(context.ExecutionData);

        Assert.That(logger.Messages, Has.Some.EqualTo($"Finished session {sessionName} stage 0"));
        Assert.That(logger.Messages, Has.Some.EqualTo($"Session {sessionName} Inputs=0"));
        Assert.That(logger.Messages, Has.Some.EqualTo($"Session {sessionName} Outputs=0"));
        Assert.That(logger.Messages, Has.Some.EqualTo($"Session {sessionName} Failures=0"));
        Assert.That(logger.Messages.Any(message =>
            message.Contains("completed. Inputs=", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void Run_WhenSessionCompletes_DisposesStageActions()
    {
        const string sessionName = "disposable-session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var disposed = false;

        var stage = new Stage(context, [], sessionName, 0, 0, 0);
        stage.AddCommunication(new DisposableAction("disposable-action", 0, Globals.Logger, () => disposed = true));

        var session = new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            new Dictionary<int, Stage> { { 0, stage } },
            [],
            context,
            []);

        session.Run(context.ExecutionData);

        Assert.That(disposed, Is.True);
    }

    [Test]
    public void Run_WhenInitializeSessionRunThrows_RemovesRunningSessionAndDisposesActionsOnce()
    {
        const string sessionName = "init-failure-session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var disposeCount = 0;

        var stage = new Stage(context, [], sessionName, 0, 0, 0);
        stage.AddCommunication(new ThrowingExportAction("throw-on-init", 0, Globals.Logger,
            new InvalidOperationException("init failed"), () => disposeCount++));

        var session = new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            new Dictionary<int, Stage> { { 0, stage } },
            [],
            context,
            []);

        var exception = Assert.Throws<InvalidOperationException>(() => session.Run(context.ExecutionData));

        Assert.That(exception!.Message, Is.EqualTo("init failed"));
        Assert.That(context.InternalRunningSessions.RunningSessionsDict.ContainsKey(sessionName), Is.False);
        Assert.That(disposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Run_WhenCollectorPostSessionSetupThrows_RemovesRunningSessionAndDisposesActionsAndCollectors()
    {
        const string sessionName = "collector-setup-session";
        var logger = new ThrowOnMessageLogger(message =>
            message.Contains("Running 1 collector task(s) after session collector-setup-session",
                StringComparison.Ordinal));
        var context = new InternalContext
        {
            Logger = logger,
            InternalRunningSessions = new RunningSessions(
                new Dictionary<string, RunningSessionData<object, object>>
                {
                    {
                        sessionName, new RunningSessionData<object, object>
                        {
                            Inputs = [],
                            Outputs = []
                        }
                    }
                }),
            ExecutionData = new QaaS.Framework.SDK.ExecutionObjects.ExecutionData { DataSources = [] }
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig());
        var actionDisposeCount = 0;
        var collectorDisposeCount = 0;

        var stage = new Stage(context, [], sessionName, 0, 0, 0);
        stage.AddCommunication(new DisposableAction("disposable-action", 0, logger, () => actionDisposeCount++));

        var collector = new DisposableCollector("collector", logger, () => collectorDisposeCount++);
        var session = new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            new Dictionary<int, Stage> { { 0, stage } },
            [collector],
            context,
            []);

        var exception = Assert.Throws<InvalidOperationException>(() => session.Run(context.ExecutionData));

        Assert.That(exception!.Message, Is.EqualTo("collector setup failed"));
        Assert.That(context.InternalRunningSessions.RunningSessionsDict.ContainsKey(sessionName), Is.False);
        Assert.That(actionDisposeCount, Is.EqualTo(1));
        Assert.That(collectorDisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Run_When_Action_Fails_Skips_Null_Task_Results_And_Returns_Remaining_Data()
    {
        const string sessionName = "partial-failure-session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var actionFailures = new ConcurrentBag<ActionFailure>();
        var stage = new Stage(context, actionFailures, sessionName, 0, 0, 0);
        stage.AddCommunication(new SuccessfulStageAction("success"));
        stage.AddCommunication(new ExceptionalStageAction("failure", new InvalidOperationException("boom")));

        var session = new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            new Dictionary<int, Stage> { { 0, stage } },
            [],
            context,
            actionFailures);

        var sessionData = session.Run(context.ExecutionData);

        Assert.That(sessionData, Is.Not.Null);
        Assert.That(sessionData!.Outputs, Has.Count.EqualTo(1));
        Assert.That(sessionData.SessionFailures, Has.Count.EqualTo(1));
        Assert.That(sessionData.SessionFailures[0].Reason.Message, Is.EqualTo("boom"));
    }

    [Test]
    public void LogSessionSummary_When_Collections_Are_Null_Logs_Zero_Counts()
    {
        const string sessionName = "summary-null-session";
        var logger = new CapturingLogger();
        var context = new InternalContext
        {
            Logger = logger,
            InternalGlobalDict = new Dictionary<string, object?>(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig());

        var session = new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            [],
            [],
            context,
            []);

        var sessionData = new SessionData
        {
            Name = sessionName,
            Inputs = null,
            Outputs = null,
            SessionFailures = [],
            UtcStartTime = DateTime.UtcNow,
            UtcEndTime = DateTime.UtcNow.AddSeconds(1)
        };

        typeof(global::QaaS.Runner.Sessions.Session.Session)
            .GetMethod("LogSessionSummary", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, [sessionData]);

        Assert.That(logger.Messages, Has.Some.EqualTo($"Session {sessionName} Inputs=0"));
        Assert.That(logger.Messages, Has.Some.EqualTo($"Session {sessionName} Outputs=0"));
        Assert.That(logger.Messages, Has.Some.EqualTo($"Session {sessionName} Failures=0"));
    }

    [Test]
    public void Run_WithNoStagesAndNullCollectors_ReturnsEmptySessionData_AndExposesSessionProperties()
    {
        const string sessionName = "empty-session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var session = new Sessions.Session.Session(
            sessionName,
            3,
            true,
            0,
            0,
            new Dictionary<int, Stage>(),
            null,
            context,
            [],
            runUntil: 7);

        var sessionData = session.Run(context.ExecutionData);

        Assert.Multiple(() =>
        {
            Assert.That(session.Name, Is.EqualTo(sessionName));
            Assert.That(session.SessionStage, Is.EqualTo(3));
            Assert.That(session.RunUntilStage, Is.EqualTo(7));
            Assert.That(sessionData, Is.Not.Null);
            Assert.That(sessionData!.Inputs, Is.Empty);
            Assert.That(sessionData.Outputs, Is.Empty);
            Assert.That(sessionData.SessionFailures, Is.Empty);
            Assert.That(context.InternalRunningSessions.RunningSessionsDict.ContainsKey(sessionName), Is.False);
        });
    }

    [Test]
    public void Run_WithMultipleInternalStages_LogsSingleStructuredSessionStartAndFinishAtInformationLevel()
    {
        const string sessionName = "structured-session";
        var logger = new CapturingLogger();
        var context = new InternalContext
        {
            Logger = logger,
            InternalGlobalDict = new Dictionary<string, object?>(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>()),
            ExecutionData = new QaaS.Framework.SDK.ExecutionObjects.ExecutionData { DataSources = [] }
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig());

        var stage0 = new Stage(context, [], sessionName, 0, 0, 0);
        stage0.AddCommunication(new RecordingAction("stage-0", 0, logger, () => { }));

        var stage1 = new Stage(context, [], sessionName, 1, 0, 0);
        stage1.AddCommunication(new RecordingAction("stage-1", 1, logger, () => { }));

        var stage3 = new Stage(context, [], sessionName, 3, 0, 0);
        stage3.AddCommunication(new RecordingAction("stage-3", 3, logger, () => { }));

        var session = new Sessions.Session.Session(
            sessionName,
            1,
            true,
            0,
            0,
            new Dictionary<int, Stage> { { 0, stage0 }, { 1, stage1 }, { 3, stage3 } },
            [],
            context,
            []);

        session.Run(context.ExecutionData);

        var informationMessages = logger.Entries
            .Where(entry => entry.LogLevel == LogLevel.Information)
            .Select(entry => entry.Message)
            .ToArray();

        Assert.That(informationMessages,
            Contains.Item($"Starting session {sessionName} on stage 0 with 1 action(s)"));
        Assert.That(informationMessages, Contains.Item($"Finished session {sessionName} stage 1"));
        Assert.That(informationMessages,
            Has.None.Matches<string>(message =>
                message == $"Starting session {sessionName} stage 1 with 1 action(s)" ||
                message == $"Starting session {sessionName} stage 3 with 1 action(s)" ||
                message == $"Finished session {sessionName} stage 0" ||
                message == $"Finished session {sessionName} stage 3"));
    }

    [Test]
    public void LogSessionSummary_WhenSessionFailuresIsNull_LogsZeroFailureCount()
    {
        const string sessionName = "summary-null-failures-session";
        var logger = new CapturingLogger();
        var context = new InternalContext
        {
            Logger = logger,
            InternalGlobalDict = new Dictionary<string, object?>(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig());

        var session = new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            [],
            [],
            context,
            []);

        var sessionData = new SessionData
        {
            Name = sessionName,
            Inputs = [],
            Outputs = [],
            SessionFailures = null!,
            UtcStartTime = DateTime.UtcNow,
            UtcEndTime = DateTime.UtcNow.AddSeconds(1)
        };

        typeof(global::QaaS.Runner.Sessions.Session.Session)
            .GetMethod("LogSessionSummary", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, [sessionData]);

        Assert.That(logger.Messages, Has.Some.EqualTo($"Session {sessionName} Failures=0"));
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

    private sealed class DisposableAction(string name, int stage, Microsoft.Extensions.Logging.ILogger logger, System.Action onDispose)
        : StagedAction(name, stage, null, logger)
    {
        internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
        {
        }

        internal override InternalCommunicationData<object> Act()
        {
            return new InternalCommunicationData<object>();
        }

        protected internal override void LogData(InternalCommunicationData<object> actData,
            DetailedData<object> itemBeforeSerialization, InputOutputState? saveAt = null)
        {
        }

        public override void Dispose()
        {
            onDispose();
        }
    }

    private sealed class ThrowingExportAction(
        string name,
        int stage,
        Microsoft.Extensions.Logging.ILogger logger,
        Exception exceptionToThrow,
        System.Action onDispose)
        : StagedAction(name, stage, null, logger)
    {
        internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
        {
            throw exceptionToThrow;
        }

        internal override InternalCommunicationData<object> Act()
        {
            return new InternalCommunicationData<object>();
        }

        protected internal override void LogData(InternalCommunicationData<object> actData,
            DetailedData<object> itemBeforeSerialization, InputOutputState? saveAt = null)
        {
        }

        public override void Dispose()
        {
            onDispose();
        }
    }

    private sealed class SuccessfulStageAction(string name) : StagedAction(name, 0, null, Globals.Logger)
    {
        internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
        {
        }

        internal override InternalCommunicationData<object> Act()
        {
            return new InternalCommunicationData<object>
            {
                Output = [new DetailedData<object> { Body = "ok" }]
            };
        }

        protected internal override void LogData(InternalCommunicationData<object> actData,
            DetailedData<object> itemBeforeSerialization, InputOutputState? saveAt = null)
        {
        }
    }

    private sealed class ExceptionalStageAction(string name, Exception exceptionToThrow)
        : StagedAction(name, 0, null, Globals.Logger)
    {
        internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
        {
        }

        internal override InternalCommunicationData<object> Act()
        {
            throw exceptionToThrow;
        }

        protected internal override void LogData(InternalCommunicationData<object> actData,
            DetailedData<object> itemBeforeSerialization, InputOutputState? saveAt = null)
        {
        }
    }

    private sealed class DisposableCollector(string name, ILogger logger, System.Action onDispose)
        : QaaS.Runner.Sessions.Actions.Collectors.Collector(name, Mock.Of<IFetcher>(), new DataFilter(), 0, 0, 1,
            logger)
    {
        internal override InternalCommunicationData<object> Act()
        {
            return new InternalCommunicationData<object> { Output = [] };
        }

        public override void Dispose()
        {
            onDispose();
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];
        public List<string> Messages => Entries.Select(entry => entry.Message).ToList();

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
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message);

    private sealed class ThrowOnMessageLogger(Func<string, bool> shouldThrow) : ILogger
    {
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
            var message = formatter(state, exception);
            if (shouldThrow(message))
                throw new InvalidOperationException("collector setup failed");
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
