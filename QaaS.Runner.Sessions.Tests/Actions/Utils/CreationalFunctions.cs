using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.MetaDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Framework.Serialization;

namespace QaaS.Runner.Sessions.Tests.Actions.Utils;

public static class CreationalFunctions
{
    private const int DefaultTestMessagesPerSecond = 100_000;

    public static InternalContext CreateContext(string sessionName, List<DataSource> dataSources)
    {
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
            ExecutionData = new ExecutionData {DataSources = dataSources},
            Logger = NullLogger.Instance
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig());
        return context;
    }
    
    public static void InitReader(
        ref Mock<IReader> reader,
        string dataToReturn)
    {
        reader = new Mock<IReader>();
        reader.Setup(r => r.Read(It.IsAny<TimeSpan>()))
            .Returns(new DetailedData<object> {Body = SerialiseData(dataToReturn), MetaData = new MetaData {Kafka = new Kafka()}});
    }
    
    public static void InitChunkReader(
        ref Mock<IChunkReader> chunkReader,
        string dataToReturn)
    {
        chunkReader = new Mock<IChunkReader>();
        chunkReader.Setup(rc => rc.ReadChunk(It.IsAny<TimeSpan>()))
            .Returns(new[] {SerialiseData(dataToReturn), SerialiseData(dataToReturn)}
                .Select(body => new DetailedData<object>{Body = body, MetaData = new MetaData {Kafka = new Kafka()}}));
    }
    
    private static byte[] SerialiseData(string data)
    {
        var serializer = SerializerFactory.BuildSerializer(SerializationType.Binary);
        return serializer?.Serialize(data) as byte[]
               ?? throw new InvalidOperationException("Failed to serialize test data using binary serializer.");
    }
    
    public static List<Data<object>> InitSender(ref Mock<ISender> sender)
    {
        List<Data<object>> infoSent = [];
        sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<Data<object>>()))
            .Callback<Data<object>>(data =>
            {
                infoSent.Add(new Data<object> {Body = data.Body});
            })
            .Returns(new DetailedData<object> {Body = "test data"});
        return infoSent;
    }
    
    public static void InitChunkSender(ref Mock<IChunkSender> chunkSender)
    {
        chunkSender = new Mock<IChunkSender>();
        chunkSender.Setup(s => s.SendChunk(It.IsAny<IEnumerable<Data<object>>>()))
            .Returns((IEnumerable<Data<object>> data) => data.Select(d => new DetailedData<object>{Body = d.Body}));
    }
    
    public static List<Data<object>> InitTransactor(ref Mock<ITransactor> transactor, string dataToGet)
    {
        List<Data<object>> infoSent = [];
        transactor = new Mock<ITransactor>();
        transactor.Setup(s => s.Transact(It.IsAny<Data<object>>()))
            .Callback<Data<object>>(data =>
            {
                infoSent.Add(new Data<object> {Body = data.Body});
            })
            .Returns(new Tuple<DetailedData<object>, DetailedData<object>?>(
                new DetailedData<object> {Body = ""}, // not used too much in the transaction class
                new DetailedData<object> {Body = dataToGet}));
        return infoSent;
    }
    
    public static Sessions.Actions.Consumers.Consumer CreateConsumer(
        ref Mock<IReader> reader,
        string dataToReturn,
        int numOfMsgToRead)
    {
        InitReader(ref reader, dataToReturn);
        
        return new Sessions.Actions.Consumers.Consumer(
            "TestConsumer", reader.Object, new TimeSpan(1),
            null,
            1,
            new CountPolicy(numOfMsgToRead),
            new DataFilter {Body = true, MetaData = false, Timestamp = false},
            SerializationType.Binary,
            null,
            NullLogger.Instance);
    }
    
    public static Sessions.Actions.Consumers.ChunkConsumer CreateChunkConsumer(
        ref Mock<IChunkReader> reader,
        string dataToReturn,
        int numOfMsgToRead)
    {
        InitChunkReader(ref reader, dataToReturn);
        
        return new Sessions.Actions.Consumers.ChunkConsumer(
            "TestConsumer", reader.Object, new TimeSpan(1),
            null,
            1,
            new CountPolicy(numOfMsgToRead),
            new DataFilter {Body = true, MetaData = false, Timestamp = false},
            SerializationType.Binary,
            null,
            NullLogger.Instance);
    }
    
    public static Sessions.Actions.Publishers.Publisher CreatePublisherWithIterations(
        ref Mock<ISender> sender,
        ref List<Data<object>> infoSent,
        string[]? dsPatterns,
        string[]? dsNames,
        int numberOfIterations,
        int msgPerSec = DefaultTestMessagesPerSecond)
    {
        infoSent = InitSender(ref sender);
        
        return new Sessions.Actions.Publishers.Publisher(
            "TestPub", sender.Object, 0,
            new DataFilter() {Body = true, Timestamp = true, MetaData = true},
            new LoadBalancePolicy(msgPerSec, 1000),
            false,
            null,
            numberOfIterations,
            0,
            null, 
            dsPatterns, dsNames,
            NullLogger.Instance);
    }
    
    public static Sessions.Actions.Publishers.ChunkPublisher CreateChunkPublisherWithIterations(
        ref Mock<IChunkSender> chunkSender,
        string[]? dsPatterns,
        string[]? dsNames,
        int chunkSize,
        int iterations = 1,
        int msgPerSec = DefaultTestMessagesPerSecond)
    {
        InitChunkSender(ref chunkSender);
        
        return new Sessions.Actions.Publishers.ChunkPublisher(
            "TestPub", chunkSender.Object,
            0,
            new DataFilter() {Body = true, Timestamp = true, MetaData = true},
            new LoadBalancePolicy(msgPerSec, 1000),
            null, chunkSize, false, iterations,
            0, null,
            dsPatterns, dsNames,
            NullLogger.Instance);
    }
    
    public static Sessions.Actions.Transactions.Transaction CreateTransactorWithLoop(
        ref Mock<ITransactor> transactor,
        string dataToGet,
        ref List<Data<object>> infoSent,
        string[]? dsPatterns,
        string[]? dsNames,
        int maxAmountOfMessages,
        int msgPerSec = DefaultTestMessagesPerSecond)
    {
        infoSent = InitTransactor(ref transactor, dataToGet);
        
        return new Sessions.Actions.Transactions.Transaction(
            "TestPub", transactor.Object, 0,
            new DataFilter() {Body = true, Timestamp = true, MetaData = false},
            new DataFilter() {Body = true, Timestamp = true, MetaData = false},
            new CountPolicy(maxAmountOfMessages).Add(new LoadBalancePolicy(msgPerSec, 1000)),
            true, 0, 0,
            null, 
            null,
            null,
            dsPatterns, dsNames, 
            NullLogger.Instance);
    }
}
