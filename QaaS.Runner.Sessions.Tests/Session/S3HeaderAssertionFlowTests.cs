using System.Collections.Concurrent;
using System.Collections.Immutable;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.MetaDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Sessions.Actions.Consumers;
using QaaS.Runner.Sessions.Tests.Actions.Utils;
using StorageMetaData = QaaS.Framework.SDK.Session.MetaDataObjects.Storage;

namespace QaaS.Runner.Sessions.Tests.Session;

[TestFixture]
public class S3HeaderAssertionFlowTests
{
    [Test]
    public void ChunkConsumer_S3StorageHeadersSurviveIntoAssertionSessionData()
    {
        const string sessionName = "s3-session";
        var expectedHeaders = new Dictionary<string, string>
        {
            ["correlation-id"] = "corr-123",
            ["content-language"] = "en-US",
        };
        var reader = new Mock<IChunkReader>();
        reader
            .Setup(instance => instance.ReadChunk(It.IsAny<TimeSpan>()))
            .Returns([
                new DetailedData<object>
                {
                    Body = new byte[] { 1, 2, 3 },
                    MetaData = new MetaData
                    {
                        Storage = new StorageMetaData
                        {
                            Key = "runner-agent/message.bin",
                            Headers = expectedHeaders,
                        },
                    },
                },
            ]);
        var consumer = new ChunkConsumer(
            "S3Consumer",
            reader.Object,
            TimeSpan.FromMilliseconds(1),
            null,
            0,
            new CountPolicy(1),
            new DataFilter
            {
                Body = true,
                MetaData = true,
                Timestamp = true,
            },
            null,
            null,
            Globals.Logger
        );
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var actionFailures = new ConcurrentBag<ActionFailure>();
        var stage = new Sessions.Session.Stage(context, actionFailures, sessionName, 0, 0, 0);
        stage.AddCommunication(consumer);
        var session = new Sessions.Session.Session(
            sessionName,
            0,
            true,
            0,
            0,
            new Dictionary<int, Sessions.Session.Stage> { [0] = stage },
            [],
            context,
            actionFailures
        );

        var sessionData = session.Run(context.ExecutionData)!;
        var assertionHook = new StorageHeaderAssertion();
        var assertion = new Assertion
        {
            Name = "S3HeaderAssertion",
            AssertionName = nameof(StorageHeaderAssertion),
            AssertionHook = assertionHook,
            _dataSourceNames = [],
            _dataSourcePatterns = [],
            _sessionNames = [sessionName],
            _sessionPatterns = [],
        };

        var assertionResult = assertion.Execute(
            new List<SessionData?> { sessionData }.ToImmutableList(),
            ImmutableList<DataSource>.Empty
        );

        Assert.Multiple(() =>
        {
            Assert.That(assertionResult.AssertionStatus, Is.EqualTo(AssertionStatus.Passed));
            Assert.That(assertionHook.ObservedHeaders, Is.EqualTo(expectedHeaders));
            Assert.That(
                sessionData.Outputs!.Single().Data.Single().MetaData!.Storage!.Headers,
                Is.SameAs(expectedHeaders)
            );
        });
    }

    private sealed class StorageHeaderAssertion : BaseAssertion<object>
    {
        public IDictionary<string, string>? ObservedHeaders { get; private set; }

        public override bool Assert(
            IImmutableList<SessionData> sessionDataList,
            IImmutableList<DataSource> dataSourceList
        )
        {
            ObservedHeaders = sessionDataList
                .Single()
                .Outputs!.Single()
                .Data.Single()
                .MetaData!.Storage!.Headers;
            return ObservedHeaders is not null;
        }
    }
}
