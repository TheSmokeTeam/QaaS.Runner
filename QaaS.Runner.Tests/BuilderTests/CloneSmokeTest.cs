using NUnit.Framework;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Framework.Policies;
using QaaS.Framework.Policies.ConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using QaaS.Runner.Sessions.Session.Builders;
using QaaS.Runner.Storage;
using QaaS.Runner.Storage.ConfigurationObjects;

namespace QaaS.Runner.Tests.BuilderTests;

public class CloneSmokeTest
{
    [Test]
    public void Clone_AllBuilders_ProducesIndependentDeepCopies()
    {
        var policyBuilder = new PolicyBuilder().Configure(new CountPolicyConfig { Count = 3 });
        var clonedPolicy = policyBuilder.Clone();

        var dataSourceBuilder = new DataSourceBuilder().Named("ds1").HookNamed("MyGenerator");
        var clonedDataSource = dataSourceBuilder.Clone();

        var contextBuilder = new ContextBuilder("fake.yaml");
        var clonedContext = contextBuilder.Clone();

        var linkBuilder = new LinkBuilder().Named("link1");
        var clonedLink = linkBuilder.Clone();

        var assertionBuilder = new AssertionBuilder
        {
            AssertionInstance = null!,
            Reporter = null!,
            Name = "assertion1",
            Assertion = "MyAssertion",
            SessionNames = ["session1"],
        };
        assertionBuilder.AddLink(linkBuilder);
        var clonedAssertion = assertionBuilder.Clone();

        var consumerBuilder = new ConsumerBuilder
        {
            Name = "consumer1",
            TimeoutMs = 5000,
        };
        var clonedConsumer = consumerBuilder.Clone();

        var publisherBuilder = new PublisherBuilder
        {
            Name = "publisher1",
            Iterations = 3,
            DataSourceNames = ["ds1"],
        };
        var clonedPublisher = publisherBuilder.Clone();

        var probeBuilder = new ProbeBuilder
        {
            Name = "probe1",
            Probe = "MyProbe",
        };
        var clonedProbe = probeBuilder.Clone();

        var transactionBuilder = new TransactionBuilder
        {
            Name = "transaction1",
            TimeoutMs = 5000,
            DataSourceNames = ["ds1"],
        };
        var clonedTransaction = transactionBuilder.Clone();

        var collectorBuilder = new CollectorBuilder
        {
            Name = "collector1",
        };
        var clonedCollector = collectorBuilder.Clone();

        var mockerCommandBuilder = new MockerCommandBuilder
        {
            Name = "mocker1",
            ServerName = "server1",
        };
        var clonedMocker = mockerCommandBuilder.Clone();

        var storageBuilder = new StorageBuilder()
            .Configure(new FilesInFileSystemConfig { Path = "/tmp", SearchPattern = "*.json" })
            .WithJsonStorageFormat(Formatting.None);
        var clonedStorage = storageBuilder.Clone();

        var sessionBuilder = new SessionBuilder
        {
            Name = "session1",
            Stage = 1,
            Consumers = [consumerBuilder],
            Publishers = [publisherBuilder],
            Probes = [probeBuilder],
            Transactions = [transactionBuilder],
            Collectors = [collectorBuilder],
            MockerCommands = [mockerCommandBuilder],
        };
        var clonedSession = sessionBuilder.Clone();

        var executionBuilder = new ExecutionBuilder
        {
            Sessions = [sessionBuilder],
            Assertions = [assertionBuilder],
            Links = [linkBuilder],
            Storages = [storageBuilder],
        };
        var clonedExecution = executionBuilder.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clonedPolicy, Is.Not.SameAs(policyBuilder));
            Assert.That(clonedDataSource, Is.Not.SameAs(dataSourceBuilder));
            Assert.That(clonedContext, Is.Not.SameAs(contextBuilder));
            Assert.That(clonedLink, Is.Not.SameAs(linkBuilder));
            Assert.That(clonedAssertion, Is.Not.SameAs(assertionBuilder));
            Assert.That(clonedConsumer, Is.Not.SameAs(consumerBuilder));
            Assert.That(clonedPublisher, Is.Not.SameAs(publisherBuilder));
            Assert.That(clonedProbe, Is.Not.SameAs(probeBuilder));
            Assert.That(clonedTransaction, Is.Not.SameAs(transactionBuilder));
            Assert.That(clonedCollector, Is.Not.SameAs(collectorBuilder));
            Assert.That(clonedMocker, Is.Not.SameAs(mockerCommandBuilder));
            Assert.That(clonedStorage, Is.Not.SameAs(storageBuilder));
            Assert.That(clonedSession, Is.Not.SameAs(sessionBuilder));
            Assert.That(clonedExecution, Is.Not.SameAs(executionBuilder));

            Assert.That(clonedSession.Consumers, Is.Not.SameAs(sessionBuilder.Consumers));
            Assert.That(clonedSession.Consumers![0], Is.Not.SameAs(sessionBuilder.Consumers![0]));
            Assert.That(clonedExecution.Sessions![0], Is.Not.SameAs(executionBuilder.Sessions![0]));

            Assert.That(clonedPolicy.Count, Is.Not.SameAs(policyBuilder.Count));
            clonedPolicy.Count!.Count = 42;
            Assert.That(policyBuilder.Count!.Count, Is.EqualTo(3));
        });
    }
}
