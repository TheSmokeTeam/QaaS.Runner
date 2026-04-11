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
            .AddPolicy(new PolicyBuilder())
            .AddPolicy(new PolicyBuilder());

        builder.UpdatePolicyAt(0, new PolicyBuilder());
        builder.RemovePolicyAt(1);
        builder.Configure(new RabbitMqReaderConfig());
        builder.UpdateConfiguration(_ => new KafkaTopicReaderConfig());
        builder.UpdateConfiguration(new SocketReaderConfig());

        Assert.That(builder.Policies, Has.Length.EqualTo(1));
        Assert.That(builder.Configuration, Is.TypeOf<SocketReaderConfig>());

        builder.Configure(new RabbitMqReaderConfig());
        Assert.That(builder.Configuration, Is.TypeOf<RabbitMqReaderConfig>());
    }

    [Test]
    public void ConsumerBuilder_UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new ConsumerBuilder()
            .Configure(new RabbitMqReaderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                RoutingKey = "created"
            });

        builder.UpdateConfiguration(new RabbitMqReaderConfig
        {
            RequestedConnectionTimeoutSeconds = 12
        });

        var mergedConfiguration = (RabbitMqReaderConfig)builder.Configuration!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Host, Is.EqualTo("rabbitmq.local"));
            Assert.That(mergedConfiguration.ExchangeName, Is.EqualTo("events"));
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("created"));
            Assert.That(mergedConfiguration.RequestedConnectionTimeoutSeconds, Is.EqualTo(12));
        });
    }

    [Test]
    public void ConsumerBuilder_UpdateConfiguration_WithSparseSameTypeUpdate_DoesNotClearExistingStringFields()
    {
        var builder = new ConsumerBuilder()
            .Configure(new RabbitMqReaderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                QueueName = "messages",
                RoutingKey = "created"
            });

        builder.UpdateConfiguration(new RabbitMqReaderConfig
        {
            HandshakeContinuationTimeoutSeconds = 7
        });

        var mergedConfiguration = (RabbitMqReaderConfig)builder.Configuration!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.ExchangeName, Is.EqualTo("events"));
            Assert.That(mergedConfiguration.QueueName, Is.EqualTo("messages"));
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("created"));
            Assert.That(mergedConfiguration.HandshakeContinuationTimeoutSeconds, Is.EqualTo(7));
        });
    }

    [Test]
    public void ConsumerBuilder_UpdateConfiguration_WithObjectPatch_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new ConsumerBuilder()
            .Configure(new RabbitMqReaderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                RoutingKey = "created"
            });

        builder.UpdateConfiguration(new
        {
            RequestedConnectionTimeoutSeconds = 12
        });

        var mergedConfiguration = (RabbitMqReaderConfig)builder.Configuration!;
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
            .AddDataSource("source-a")
            .AddDataSource("source-b")
            .AddDataSourcePattern("^source-.*$")
            .AddPolicy(new PolicyBuilder())
            .Configure(new RabbitMqSenderConfig());

        builder.UpdateDataSource("source-a", "source-updated");
        builder.RemoveDataSource("source-b");
        builder.UpdateDataSourcePattern("^source-.*$", "^updated-.*$");
        builder.UpdatePolicyAt(0, new PolicyBuilder());
        builder.UpdateConfiguration(_ => new SocketSenderConfig());
        builder.UpdateConfiguration(new KafkaTopicSenderConfig());

        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.Policies, Has.Length.EqualTo(1));
        Assert.That(builder.Configuration, Is.TypeOf<KafkaTopicSenderConfig>());

        builder.AddDataSource("source-indexed")
            .AddDataSourcePattern("^indexed-.*$");
        builder.RemoveDataSourceAt(1)
            .RemoveDataSourcePatternAt(1);

        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));

        builder.Configure(new SocketSenderConfig());
        Assert.That(builder.Configuration, Is.TypeOf<SocketSenderConfig>());
    }

    [Test]
    public void PublisherBuilder_UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new PublisherBuilder()
            .Configure(new RabbitMqSenderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                RoutingKey = "published"
            });

        builder.UpdateConfiguration(new RabbitMqSenderConfig
        {
            Expiration = "30000"
        });

        var mergedConfiguration = (RabbitMqSenderConfig)builder.Configuration!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Host, Is.EqualTo("rabbitmq.local"));
            Assert.That(mergedConfiguration.ExchangeName, Is.EqualTo("events"));
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("published"));
            Assert.That(mergedConfiguration.Expiration, Is.EqualTo("30000"));
        });
    }

    [Test]
    public void PublisherBuilder_UpdateConfiguration_WithSparseSameTypeUpdate_DoesNotClearExistingStringFields()
    {
        var builder = new PublisherBuilder()
            .Configure(new RabbitMqSenderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                QueueName = "messages",
                RoutingKey = "published"
            });

        builder.UpdateConfiguration(new RabbitMqSenderConfig
        {
            HandshakeContinuationTimeoutSeconds = 5
        });

        var mergedConfiguration = (RabbitMqSenderConfig)builder.Configuration!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.ExchangeName, Is.EqualTo("events"));
            Assert.That(mergedConfiguration.QueueName, Is.EqualTo("messages"));
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("published"));
            Assert.That(mergedConfiguration.HandshakeContinuationTimeoutSeconds, Is.EqualTo(5));
        });
    }

    [Test]
    public void PublisherBuilder_UpdateConfiguration_WithObjectPatch_MergesKafkaHeadersAndPreservesExistingFields()
    {
        var builder = new PublisherBuilder()
            .Configure(new KafkaTopicSenderConfig
            {
                HostNames = ["broker:9092"],
                Username = "runner",
                Password = "secret",
                TopicName = "events",
                DefaultKafkaKey = "default-key"
            });

        builder.UpdateConfiguration(new
        {
            Headers = new Dictionary<string, object?>
            {
                ["correlation-id"] = "123"
            }
        });

        var mergedConfiguration = (KafkaTopicSenderConfig)builder.Configuration!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.HostNames, Is.EqualTo(new[] { "broker:9092" }));
            Assert.That(mergedConfiguration.Username, Is.EqualTo("runner"));
            Assert.That(mergedConfiguration.Password, Is.EqualTo("secret"));
            Assert.That(mergedConfiguration.TopicName, Is.EqualTo("events"));
            Assert.That(mergedConfiguration.DefaultKafkaKey, Is.EqualTo("default-key"));
            Assert.That(mergedConfiguration.Headers, Does.ContainKey("correlation-id"));
            Assert.That(mergedConfiguration.Headers!["correlation-id"], Is.EqualTo("123"));
        });
    }

    [Test]
    public void TransactionBuilder_ShouldSupportPolicyDataSourceAndConfigurationCrud()
    {
        var builder = new TransactionBuilder()
            .AddPolicy(new PolicyBuilder())
            .AddDataSource("source-a")
            .AddDataSourcePattern("^source-.*$")
            .Configure(new HttpTransactorConfig());

        builder.UpdatePolicyAt(0, new PolicyBuilder());
        builder.UpdateDataSource("source-a", "source-updated");
        builder.UpdateDataSourcePattern("^source-.*$", "^updated-.*$");
        builder.UpdateConfiguration(_ => new GrpcTransactorConfig());
        builder.UpdateConfiguration(new HttpTransactorConfig());

        Assert.That(builder.Policies, Has.Length.EqualTo(1));
        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.Configuration, Is.TypeOf<HttpTransactorConfig>());

        builder.AddDataSource("source-indexed")
            .AddDataSourcePattern("^indexed-.*$");
        builder.RemoveDataSourceAt(1)
            .RemoveDataSourcePatternAt(1);

        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));

        builder.RemoveDataSource("source-updated")
            .RemoveDataSourcePattern("^updated-.*$")
            .RemovePolicyAt(0)
            .Configure(new GrpcTransactorConfig());

        Assert.That(builder.DataSourceNames, Is.Empty);
        Assert.That(builder.DataSourcePatterns, Is.Empty);
        Assert.That(builder.Policies, Is.Empty);
        Assert.That(builder.Configuration, Is.TypeOf<GrpcTransactorConfig>());
    }

    [Test]
    public void TransactionBuilder_UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new TransactionBuilder()
            .Configure(new HttpTransactorConfig
            {
                Method = HttpMethods.Put,
                BaseAddress = "https://service.local",
                Route = "/resource",
                Retries = 3
            });

        builder.UpdateConfiguration(new HttpTransactorConfig
        {
            MessageSendRetriesIntervalMs = 0
        });

        var mergedConfiguration = (HttpTransactorConfig)builder.Configuration!;
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
    public void TransactionBuilder_UpdateConfiguration_WithObjectPatch_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new TransactionBuilder()
            .Configure(new HttpTransactorConfig
            {
                Method = HttpMethods.Put,
                BaseAddress = "https://service.local",
                Route = "/resource",
                Retries = 3
            });

        builder.UpdateConfiguration(new
        {
            MessageSendRetriesIntervalMs = 0
        });

        var mergedConfiguration = (HttpTransactorConfig)builder.Configuration!;
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
            .AddDataSourceName("source-a")
            .AddDataSourcePattern("^source-.*$")
            .Configure(new { enabled = true });

        builder.RemoveDataSourceName("source-a");
        builder.AddDataSourceName("source-updated");
        builder.RemoveDataSourcePattern("^source-.*$");
        builder.AddDataSourcePattern("^updated-.*$");
        builder.UpdateConfiguration(new { threshold = 5 });
        builder.UpdateConfiguration(new { nested = new { value = "set" } });
        builder.AddDataSourceName("source-indexed")
            .AddDataSourcePattern("^indexed-.*$")
            .RemoveDataSourceNameAt(1)
            .RemoveDataSourcePatternAt(1);

        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.Configuration["enabled"], Is.EqualTo("True"));
        Assert.That(builder.Configuration["threshold"], Is.EqualTo("5"));
        Assert.That(builder.Configuration["nested:value"], Is.EqualTo("set"));

        builder.RemoveDataSourceName("source-updated")
            .RemoveDataSourcePattern("^updated-.*$")
            .RemoveConfiguration();

        Assert.That(builder.DataSourceNames, Is.Empty);
        Assert.That(builder.DataSourcePatterns, Is.Empty);
        Assert.That(builder.Configuration.AsEnumerable().Any(), Is.False);
    }

    [Test]
    public void CollectorBuilder_ShouldSupportConfigurationCrud()
    {
        var builder = new CollectorBuilder().Configure(new PrometheusFetcherConfig
        {
            Url = "https://prometheus",
            Expression = "up"
        });

        builder.UpdateConfiguration(_ => new PrometheusFetcherConfig
        {
            Url = "https://prometheus-updated",
            Expression = "sum(up)"
        });
        builder.UpdateConfiguration(new PrometheusFetcherConfig
        {
            Url = "https://prometheus-updated-again",
            Expression = "max(up)"
        });

        Assert.That(builder.Configuration, Is.TypeOf<PrometheusFetcherConfig>());
        Assert.That(((PrometheusFetcherConfig)builder.Configuration!).Expression, Is.EqualTo("max(up)"));

        builder.Configure(new PrometheusFetcherConfig
        {
            Url = "https://prometheus-latest",
            Expression = "up"
        });
        Assert.That(builder.Configuration, Is.TypeOf<PrometheusFetcherConfig>());
    }

    [Test]
    public void CollectorBuilder_UpdateConfiguration_WithObjectPatch_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new CollectorBuilder().Configure(new PrometheusFetcherConfig
        {
            Url = "https://prometheus",
            Expression = "up",
            SampleIntervalMs = 5000
        });

        builder.UpdateConfiguration(new
        {
            ApiKey = "api-key"
        });

        var mergedConfiguration = (PrometheusFetcherConfig)builder.Configuration!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Url, Is.EqualTo("https://prometheus"));
            Assert.That(mergedConfiguration.Expression, Is.EqualTo("up"));
            Assert.That(mergedConfiguration.SampleIntervalMs, Is.EqualTo(5000));
            Assert.That(mergedConfiguration.ApiKey, Is.EqualTo("api-key"));
        });
    }

    [Test]
    public void CollectorBuilder_UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new CollectorBuilder().Configure(new PrometheusFetcherConfig
        {
            Url = "https://prometheus",
            Expression = "up",
            SampleIntervalMs = 5000
        });

        builder.UpdateConfiguration(new PrometheusFetcherConfig
        {
            ApiKey = "api-key"
        });

        var mergedConfiguration = (PrometheusFetcherConfig)builder.Configuration!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Url, Is.EqualTo("https://prometheus"));
            Assert.That(mergedConfiguration.Expression, Is.EqualTo("up"));
            Assert.That(mergedConfiguration.SampleIntervalMs, Is.EqualTo(5000));
            Assert.That(mergedConfiguration.ApiKey, Is.EqualTo("api-key"));
        });
    }

    [Test]
    public void MockerCommandBuilder_ShouldSupportConfigurationCrud()
    {
        var builder = new MockerCommandBuilder().Configure(new MockerCommandConfig
        {
            Consume = new ConsumeCommandConfig()
        });

        builder.UpdateConfiguration(_ => new MockerCommandConfig
        {
            TriggerAction = new TriggerAction()
        });
        builder.UpdateConfiguration(new MockerCommandConfig
        {
            Consume = new ConsumeCommandConfig()
        });

        Assert.That(builder.Configuration, Is.Not.Null);
        Assert.That(builder.Configuration!.Consume, Is.Not.Null);

        builder.Configure(new MockerCommandConfig
        {
            TriggerAction = new TriggerAction()
        });
        Assert.That(builder.Configuration!.TriggerAction, Is.Not.Null);
    }

    [Test]
    public void MockerCommandBuilder_UpdateConfiguration_WithObjectPatch_MergesNestedCommandValues()
    {
        var builder = new MockerCommandBuilder().Configure(new MockerCommandConfig
        {
            TriggerAction = new TriggerAction
            {
                ActionName = "seed",
                TimeoutMs = 5
            }
        });

        builder.UpdateConfiguration(new
        {
            TriggerAction = new
            {
                TimeoutMs = 15
            }
        });

        var command = builder.Configuration!;
        Assert.Multiple(() =>
        {
            Assert.That(command.TriggerAction, Is.Not.Null);
            Assert.That(command.TriggerAction!.ActionName, Is.EqualTo("seed"));
            Assert.That(command.TriggerAction.TimeoutMs, Is.EqualTo(15));
        });
    }
}



