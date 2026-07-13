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
        const string s3ConsumerName = "S3Consumer";
        var expectedStorageMetadata = new List<StorageMetaData>
        {
            new()
            {
                Key = "runner-agent/first-message.bin",
                Headers = new Dictionary<string, string>
                {
                    ["correlation-id"] = "corr-123",
                    ["content-language"] = "en-US",
                },
            },
            new()
            {
                Key = "runner-agent/second-message.bin",
                Headers = new Dictionary<string, string>
                {
                    ["correlation-id"] = "corr-456",
                    ["content-type"] = "application/octet-stream",
                },
            },
        };
        var reader = new Mock<IChunkReader>();
        reader
            .Setup(instance => instance.ReadChunk(It.IsAny<TimeSpan>()))
            .Returns(
                expectedStorageMetadata
                    .Select(
                        (storage, index) =>
                            new DetailedData<object>
                            {
                                Body = new byte[] { (byte)(index + 1) },
                                MetaData = new MetaData { Storage = storage },
                            }
                    )
                    .ToList()
            );
        var unrelatedReader = new Mock<IChunkReader>();
        unrelatedReader
            .Setup(instance => instance.ReadChunk(It.IsAny<TimeSpan>()))
            .Returns([
                new DetailedData<object>
                {
                    Body = new byte[] { 0 },
                    MetaData = new MetaData
                    {
                        Storage = new StorageMetaData
                        {
                            Key = "unrelated/message.bin",
                            Headers = new Dictionary<string, string>
                            {
                                ["correlation-id"] = "unrelated",
                            },
                        },
                    },
                },
            ]);
        var consumer = new ChunkConsumer(
            s3ConsumerName,
            reader.Object,
            TimeSpan.FromMilliseconds(1),
            null,
            0,
            new CountPolicy(expectedStorageMetadata.Count),
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
        var unrelatedConsumer = new ChunkConsumer(
            "UnrelatedConsumer",
            unrelatedReader.Object,
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
        stage.AddCommunication(unrelatedConsumer);
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
        var assertionHook = new StorageHeaderAssertion(s3ConsumerName, expectedStorageMetadata);
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
            Assert.That(sessionData.Outputs, Has.Count.EqualTo(2));
            Assert.That(
                assertionHook.ObservedStorageMetadata,
                Has.Count.EqualTo(expectedStorageMetadata.Count)
            );
            foreach (var expectedStorage in expectedStorageMetadata)
            {
                var observedStorage = assertionHook.ObservedStorageMetadata.Single(storage =>
                    storage?.Key == expectedStorage.Key
                );
                Assert.That(observedStorage, Is.SameAs(expectedStorage));
                Assert.That(observedStorage!.Headers, Is.SameAs(expectedStorage.Headers));
            }
        });
    }

    private sealed class StorageHeaderAssertion : BaseAssertion<object>
    {
        private readonly string _consumerOutputName;
        private readonly IReadOnlyList<StorageMetaData> _expectedStorageMetadata;

        public StorageHeaderAssertion(
            string consumerOutputName,
            IReadOnlyList<StorageMetaData> expectedStorageMetadata
        )
        {
            _consumerOutputName = consumerOutputName;
            _expectedStorageMetadata = expectedStorageMetadata;
        }

        public IReadOnlyList<StorageMetaData?> ObservedStorageMetadata { get; private set; } = [];

        public override bool Assert(
            IImmutableList<SessionData> sessionDataList,
            IImmutableList<DataSource> dataSourceList
        )
        {
            var s3ConsumerOutput = sessionDataList
                .SelectMany(sessionData => sessionData.Outputs ?? [])
                .Single(output => output.Name == _consumerOutputName);
            var observedStorageMetadata = new List<StorageMetaData?>();
            foreach (var detailedData in s3ConsumerOutput.Data)
                observedStorageMetadata.Add(detailedData.MetaData?.Storage);

            ObservedStorageMetadata = observedStorageMetadata;
            return observedStorageMetadata.Count == _expectedStorageMetadata.Count
                && _expectedStorageMetadata.All(expectedStorage =>
                    observedStorageMetadata.SingleOrDefault(storage =>
                        storage?.Key == expectedStorage.Key
                    )
                        is { Headers: not null } observedStorage
                    && HeadersMatch(expectedStorage.Headers, observedStorage.Headers)
                );
        }

        private static bool HeadersMatch(
            IDictionary<string, string>? expected,
            IDictionary<string, string> observed
        ) =>
            expected is not null
            && expected.Count == observed.Count
            && expected.All(header =>
                observed.TryGetValue(header.Key, out var value) && value == header.Value
            );
    }
}
