using System.Linq;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.ConfigurationObjects.Grpc;
using QaaS.Framework.Protocols.ConfigurationObjects.Http;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.Protocols.ConfigurationObjects.Prometheus;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.Socket;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using QaaS.Runner.Sessions.ConfigurationObjects;

namespace QaaS.Runner.Sessions.Tests.Actions;

[TestFixture]
public class BuilderCrudTests
{
    [Test]
    public void ConsumerBuilder_ShouldSupportPolicyAndConfigurationCrud()
    {
        var builder = new ConsumerBuilder()
            .CreatePolicy(new PolicyBuilder())
            .CreatePolicy(new PolicyBuilder());

        builder.UpdatePolicyAt(0, new PolicyBuilder());
        builder.DeletePolicyAt(1);
        builder.CreateConfiguration(new RabbitMqReaderConfig());
        builder.UpdateConfiguration(_ => new KafkaTopicReaderConfig());
        builder.UpsertConfiguration(new SocketReaderConfig());

        Assert.That(builder.ReadPolicies(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadConfiguration(), Is.TypeOf<SocketReaderConfig>());

        builder.DeleteConfiguration();
        Assert.That(builder.ReadConfiguration(), Is.Null);
    }

    [Test]
    public void ConsumerBuilder_UpsertConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new ConsumerBuilder()
            .CreateConfiguration(new RabbitMqReaderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                RoutingKey = "created"
            });

        builder.UpsertConfiguration(new RabbitMqReaderConfig
        {
            RequestedConnectionTimeoutSeconds = 12
        });

        var mergedConfiguration = (RabbitMqReaderConfig)builder.ReadConfiguration()!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Host, Is.EqualTo("rabbitmq.local"));
            Assert.That(mergedConfiguration.ExchangeName, Is.EqualTo("events"));
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("created"));
            Assert.That(mergedConfiguration.RequestedConnectionTimeoutSeconds, Is.EqualTo(12));
        });
    }

    [Test]
    public void PublisherBuilder_ShouldSupportDataSourcePolicyAndConfigurationCrud()
    {
        var builder = new PublisherBuilder()
            .CreateDataSource("source-a")
            .CreateDataSource("source-b")
            .CreateDataSourcePattern("^source-.*$")
            .CreatePolicy(new PolicyBuilder())
            .CreateConfiguration(new RabbitMqSenderConfig());

        builder.UpdateDataSource("source-a", "source-updated");
        builder.DeleteDataSource("source-b");
        builder.UpdateDataSourcePattern("^source-.*$", "^updated-.*$");
        builder.UpdatePolicyAt(0, new PolicyBuilder());
        builder.UpdateConfiguration(_ => new SocketSenderConfig());
        builder.UpsertConfiguration(new KafkaTopicSenderConfig());

        Assert.That(builder.ReadDataSources(), Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.ReadDataSourcePatterns(), Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.ReadPolicies(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadConfiguration(), Is.TypeOf<KafkaTopicSenderConfig>());

        builder.DeleteConfiguration();
        Assert.That(builder.ReadConfiguration(), Is.Null);
    }

    [Test]
    public void PublisherBuilder_UpsertConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new PublisherBuilder()
            .CreateConfiguration(new RabbitMqSenderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                RoutingKey = "published"
            });

        builder.UpsertConfiguration(new RabbitMqSenderConfig
        {
            Expiration = "30000"
        });

        var mergedConfiguration = (RabbitMqSenderConfig)builder.ReadConfiguration()!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Host, Is.EqualTo("rabbitmq.local"));
            Assert.That(mergedConfiguration.ExchangeName, Is.EqualTo("events"));
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("published"));
            Assert.That(mergedConfiguration.Expiration, Is.EqualTo("30000"));
        });
    }

    [Test]
    public void TransactionBuilder_ShouldSupportPolicyDataSourceAndConfigurationCrud()
    {
        var builder = new TransactionBuilder()
            .CreatePolicy(new PolicyBuilder())
            .CreateDataSource("source-a")
            .CreateDataSourcePattern("^source-.*$")
            .CreateConfiguration(new HttpTransactorConfig());

        builder.UpdatePolicyAt(0, new PolicyBuilder());
        builder.UpdateDataSource("source-a", "source-updated");
        builder.UpdateDataSourcePattern("^source-.*$", "^updated-.*$");
        builder.UpdateConfiguration(_ => new GrpcTransactorConfig());
        builder.UpsertConfiguration(new HttpTransactorConfig());

        Assert.That(builder.ReadPolicies(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadDataSources(), Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.ReadDataSourcePatterns(), Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.ReadConfiguration(), Is.TypeOf<HttpTransactorConfig>());

        builder.DeleteDataSource("source-updated")
            .DeleteDataSourcePattern("^updated-.*$")
            .DeletePolicyAt(0)
            .DeleteConfiguration();

        Assert.That(builder.ReadDataSources(), Is.Empty);
        Assert.That(builder.ReadDataSourcePatterns(), Is.Empty);
        Assert.That(builder.ReadPolicies(), Is.Empty);
        Assert.That(builder.ReadConfiguration(), Is.Null);
    }

    [Test]
    public void TransactionBuilder_UpsertConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new TransactionBuilder()
            .CreateConfiguration(new HttpTransactorConfig
            {
                Method = HttpMethods.Put,
                BaseAddress = "https://service.local",
                Route = "/resource",
                Retries = 3
            });

        builder.UpsertConfiguration(new HttpTransactorConfig
        {
            MessageSendRetriesIntervalMs = 0
        });

        var mergedConfiguration = (HttpTransactorConfig)builder.ReadConfiguration()!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Method, Is.EqualTo(HttpMethods.Put));
            Assert.That(mergedConfiguration.BaseAddress, Is.EqualTo("https://service.local"));
            Assert.That(mergedConfiguration.Route, Is.EqualTo("/resource"));
            Assert.That(mergedConfiguration.Retries, Is.EqualTo(3));
            Assert.That(mergedConfiguration.MessageSendRetriesIntervalMs, Is.Zero);
        });
    }

    [Test]
    public void ProbeBuilder_ShouldSupportDataSourceAndConfigurationCrud()
    {
        var builder = new ProbeBuilder()
            .CreateDataSourceName("source-a")
            .CreateDataSourcePattern("^source-.*$")
            .Configure(new { enabled = true });

        builder.UpdateDataSourceName("source-a", "source-updated");
        builder.UpdateDataSourcePattern("^source-.*$", "^updated-.*$");
        builder.UpdateConfiguration(new { threshold = 5 });
        builder.UpsertConfiguration(new { nested = new { value = "set" } });

        Assert.That(builder.ReadDataSourceNames(), Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.ReadDataSourcePatterns(), Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.ReadConfiguration()["enabled"], Is.EqualTo("True"));
        Assert.That(builder.ReadConfiguration()["threshold"], Is.EqualTo("5"));
        Assert.That(builder.ReadConfiguration()["nested:value"], Is.EqualTo("set"));

        builder.RemoveDataSourceName("source-updated")
            .RemoveDataSourcePattern("^updated-.*$")
            .DeleteConfiguration();

        Assert.That(builder.ReadDataSourceNames(), Is.Empty);
        Assert.That(builder.ReadDataSourcePatterns(), Is.Empty);
        Assert.That(builder.ReadConfiguration().AsEnumerable().Any(), Is.False);
    }

    [Test]
    public void CollectorBuilder_ShouldSupportConfigurationCrud()
    {
        var builder = new CollectorBuilder().Create(new PrometheusFetcherConfig
        {
            Url = "https://prometheus",
            Expression = "up"
        });

        builder.UpdateConfiguration(_ => new PrometheusFetcherConfig
        {
            Url = "https://prometheus-updated",
            Expression = "sum(up)"
        });
        builder.UpsertConfiguration(new PrometheusFetcherConfig
        {
            Url = "https://prometheus-upserted",
            Expression = "max(up)"
        });

        Assert.That(builder.ReadConfiguration(), Is.TypeOf<PrometheusFetcherConfig>());
        Assert.That(((PrometheusFetcherConfig)builder.ReadConfiguration()!).Expression, Is.EqualTo("max(up)"));

        builder.DeleteConfiguration();
        Assert.That(builder.ReadConfiguration(), Is.Null);
    }

    [Test]
    public void CollectorBuilder_UpsertConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new CollectorBuilder().Create(new PrometheusFetcherConfig
        {
            Url = "https://prometheus",
            Expression = "up",
            SampleIntervalMs = 5000
        });

        builder.UpsertConfiguration(new PrometheusFetcherConfig
        {
            ApiKey = "api-key"
        });

        var mergedConfiguration = (PrometheusFetcherConfig)builder.ReadConfiguration()!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Url, Is.EqualTo("https://prometheus"));
            Assert.That(mergedConfiguration.Expression, Is.EqualTo("up"));
            Assert.That(mergedConfiguration.SampleIntervalMs, Is.EqualTo(5000));
            Assert.That(mergedConfiguration.ApiKey, Is.EqualTo("api-key"));
        });
    }

    [Test]
    public void MockerCommandBuilder_ShouldSupportCommandCrud()
    {
        var builder = new MockerCommandBuilder().CreateCommand(new CommandConfig
        {
            Consume = new ConsumeConfig()
        });

        builder.UpdateCommand(_ => new CommandConfig
        {
            TriggerAction = new TriggerAction()
        });
        builder.UpsertCommand(new CommandConfig
        {
            Consume = new ConsumeConfig()
        });

        Assert.That(builder.ReadCommand(), Is.Not.Null);
        Assert.That(builder.ReadCommand()!.Consume, Is.Not.Null);

        builder.DeleteCommand();
        Assert.That(builder.ReadCommand(), Is.Null);
    }
}
