using System.Linq;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Grpc;
using QaaS.Framework.Protocols.ConfigurationObjects.Http;
using QaaS.Framework.Protocols.ConfigurationObjects.Kafka;
using QaaS.Framework.Protocols.ConfigurationObjects.Prometheus;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.Protocols.ConfigurationObjects.S3;
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
        builder.UpdateConfiguration(new KafkaTopicReaderConfig());
        builder.UpdateConfiguration(new SocketReaderConfig());

        Assert.That(builder.Policies, Has.Length.EqualTo(1));
        Assert.That(builder.Socket, Is.TypeOf<SocketReaderConfig>());

        builder.Configure(new RabbitMqReaderConfig());
        Assert.That(builder.RabbitMq, Is.TypeOf<RabbitMqReaderConfig>());
    }

    [Test]
    public void ConsumerBuilder_UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new ConsumerBuilder().Configure(
            new RabbitMqReaderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                RoutingKey = "created",
            }
        );

        builder.UpdateConfiguration(
            new RabbitMqReaderConfig { RequestedConnectionTimeoutSeconds = 12 }
        );

        var mergedConfiguration = builder.RabbitMq!;
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
        var builder = new ConsumerBuilder().Configure(
            new RabbitMqReaderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                QueueName = "messages",
                RoutingKey = "created",
            }
        );

        builder.UpdateConfiguration(
            new RabbitMqReaderConfig { HandshakeContinuationTimeoutSeconds = 7 }
        );

        var mergedConfiguration = builder.RabbitMq!;
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
        var builder = new ConsumerBuilder().Configure(
            new RabbitMqReaderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                RoutingKey = "created",
            }
        );

        builder.UpdateConfiguration(new { RequestedConnectionTimeoutSeconds = 12 });

        var mergedConfiguration = builder.RabbitMq!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Host, Is.EqualTo("rabbitmq.local"));
            Assert.That(mergedConfiguration.ExchangeName, Is.EqualTo("events"));
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("created"));
            Assert.That(mergedConfiguration.RequestedConnectionTimeoutSeconds, Is.EqualTo(12));
        });
    }

    [Test]
    public void ConsumerBuilder_UpdateConfiguration_WithConfiguration_MergesRabbitMqQueueParametersAndPreservesExistingFields()
    {
        var builder = new ConsumerBuilder().Configure(
            new RabbitMqReaderConfig
            {
                Host = "rabbitmq.local",
                QueueName = "messages",
                RoutingKey = "created",
                Durable = false,
                Exclusive = false,
                AutoDelete = false,
                Arguments = new Dictionary<string, object?> { ["x-message-ttl"] = 30000 },
            }
        );

        var customArgs = new Dictionary<string, object?>
        {
            ["x-message-ttl"] = 60000,
            ["x-max-length"] = 500,
            ["x-single-active-consumer"] = true,
        };

        builder.UpdateConfiguration(
            new RabbitMqReaderConfig
            {
                Durable = true,
                Exclusive = true,
                AutoDelete = true,
                Arguments = customArgs,
            }
        );

        var mergedConfiguration = builder.RabbitMq!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Host, Is.EqualTo("rabbitmq.local"));
            Assert.That(mergedConfiguration.QueueName, Is.EqualTo("messages"));
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("created"));
            Assert.That(mergedConfiguration.Durable, Is.True);
            Assert.That(mergedConfiguration.Exclusive, Is.True);
            Assert.That(mergedConfiguration.AutoDelete, Is.True);
            Assert.That(mergedConfiguration.Arguments, Is.Not.Null);
            Assert.That(mergedConfiguration.Arguments!["x-message-ttl"], Is.EqualTo("60000"));
            Assert.That(mergedConfiguration.Arguments["x-max-length"], Is.EqualTo("500"));
            Assert.That(mergedConfiguration.Arguments["x-single-active-consumer"], Is.EqualTo("True"));
        });
    }

    [Test]
    public void ConsumerBuilder_UpdateConfiguration_WithObjectPatch_MergesRabbitMqAllFieldsAndPreservesExistingFields()
    {
        var builder = new ConsumerBuilder().Configure(
            new RabbitMqReaderConfig
            {
                Host = "rabbitmq.local",
                Username = "guest",
                Password = "guest",
                Port = 5672,
                VirtualHost = "/",
                ContinuationTimeoutSeconds = 5,
                RequestedConnectionTimeoutSeconds = 5,
                HandshakeContinuationTimeoutSeconds = 10,
                QueueName = "messages",
                RoutingKey = "created",
                CreatedQueueTimeToExpireMs = 300000,
                Durable = false,
                Exclusive = false,
                AutoDelete = false,
                Arguments = new Dictionary<string, object?> { ["x-message-ttl"] = "30000" },
            }
        );

        builder.UpdateConfiguration(
            new
            {
                Host = "rabbitmq-updated.local",
                Username = "runner",
                Password = "secret",
                Port = 5673,
                VirtualHost = "/qaas",
                ContinuationTimeoutSeconds = 6,
                RequestedConnectionTimeoutSeconds = 7,
                HandshakeContinuationTimeoutSeconds = 8,
                QueueName = "messages-updated",
                RoutingKey = "updated",
                CreatedQueueTimeToExpireMs = 12345d,
                Durable = true,
                Exclusive = true,
                AutoDelete = true,
                Arguments = new Dictionary<string, object?> { ["x-message-ttl"] = "60000", ["x-max-length"] = "500" },
            }
        );

        var mergedConfiguration = builder.RabbitMq!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Host, Is.EqualTo("rabbitmq-updated.local"));
            Assert.That(mergedConfiguration.Username, Is.EqualTo("runner"));
            Assert.That(mergedConfiguration.Password, Is.EqualTo("secret"));
            Assert.That(mergedConfiguration.Port, Is.EqualTo(5673));
            Assert.That(mergedConfiguration.VirtualHost, Is.EqualTo("/qaas"));
            Assert.That(mergedConfiguration.ContinuationTimeoutSeconds, Is.EqualTo(6));
            Assert.That(mergedConfiguration.RequestedConnectionTimeoutSeconds, Is.EqualTo(7));
            Assert.That(mergedConfiguration.HandshakeContinuationTimeoutSeconds, Is.EqualTo(8));
            Assert.That(mergedConfiguration.ExchangeName, Is.Empty);
            Assert.That(mergedConfiguration.QueueName, Is.EqualTo("messages-updated"));
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("updated"));
            Assert.That(mergedConfiguration.CreatedQueueTimeToExpireMs, Is.EqualTo(12345d));
            Assert.That(mergedConfiguration.Durable, Is.True);
            Assert.That(mergedConfiguration.Exclusive, Is.True);
            Assert.That(mergedConfiguration.AutoDelete, Is.True);
            Assert.That(mergedConfiguration.Arguments, Does.ContainKey("x-message-ttl"));
            Assert.That(mergedConfiguration.Arguments!["x-message-ttl"], Is.EqualTo("60000"));
            Assert.That(mergedConfiguration.Arguments["x-max-length"], Is.EqualTo("500"));
        });
    }

    [Test]
    public void ConsumerBuilder_UpdateConfiguration_WithObjectPatch_MergesS3BucketAndPreservesExistingFields()
    {
        var builder = new ConsumerBuilder().Configure(
            new S3BucketReaderConfig
            {
                StorageBucket = "bucket",
                ServiceURL = "http://127.0.0.1:9000",
                AccessKey = "ak",
                SecretKey = "sk",
                ForcePathStyle = false,
                Prefix = "runs/",
                Delimiter = "|",
                MaximumRetryCount = 3,
                SkipEmptyObjects = true,
                ReadFromRunStartTime = false,
                ReadStorageHeaders = false,
            }
        );

        builder.UpdateConfiguration(
            new
            {
                StorageBucket = "bucket-updated",
                ServiceURL = "http://127.0.0.1:9001",
                AccessKey = "ak-updated",
                SecretKey = "sk-updated",
                ForcePathStyle = true,
                Prefix = "future/",
                Delimiter = "/",
                MaximumRetryCount = 9,
                SkipEmptyObjects = false,
                ReadFromRunStartTime = true,
                ReadStorageHeaders = true,
            }
        );

        var mergedConfiguration = builder.S3Bucket!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.StorageBucket, Is.EqualTo("bucket-updated"));
            Assert.That(mergedConfiguration.ServiceURL, Is.EqualTo("http://127.0.0.1:9001"));
            Assert.That(mergedConfiguration.AccessKey, Is.EqualTo("ak-updated"));
            Assert.That(mergedConfiguration.SecretKey, Is.EqualTo("sk-updated"));
            Assert.That(mergedConfiguration.ForcePathStyle, Is.True);
            Assert.That(mergedConfiguration.Prefix, Is.EqualTo("future/"));
            Assert.That(mergedConfiguration.Delimiter, Is.EqualTo("/"));
            Assert.That(mergedConfiguration.MaximumRetryCount, Is.EqualTo(9));
            Assert.That(mergedConfiguration.SkipEmptyObjects, Is.False);
            Assert.That(mergedConfiguration.ReadFromRunStartTime, Is.True);
            Assert.That(mergedConfiguration.ReadStorageHeaders, Is.True);
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
        builder.UpdateConfiguration(new SocketSenderConfig());
        builder.UpdateConfiguration(new KafkaTopicSenderConfig());

        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.Policies, Has.Length.EqualTo(1));
        Assert.That(builder.KafkaTopic, Is.TypeOf<KafkaTopicSenderConfig>());

        builder.AddDataSource("source-indexed").AddDataSourcePattern("^indexed-.*$");
        builder.RemoveDataSourceAt(1).RemoveDataSourcePatternAt(1);

        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));

        builder.Configure(new SocketSenderConfig());
        Assert.That(builder.Socket, Is.TypeOf<SocketSenderConfig>());
    }

    [Test]
    public void PublisherBuilder_UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new PublisherBuilder().Configure(
            new RabbitMqSenderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                RoutingKey = "published",
            }
        );

        builder.UpdateConfiguration(new RabbitMqSenderConfig { Expiration = "30000" });

        var mergedConfiguration = builder.RabbitMq!;
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
        var builder = new PublisherBuilder().Configure(
            new RabbitMqSenderConfig
            {
                Host = "rabbitmq.local",
                ExchangeName = "events",
                QueueName = "messages",
                RoutingKey = "published",
            }
        );

        builder.UpdateConfiguration(
            new RabbitMqSenderConfig { HandshakeContinuationTimeoutSeconds = 5 }
        );

        var mergedConfiguration = builder.RabbitMq!;
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
        var builder = new PublisherBuilder().Configure(
            new KafkaTopicSenderConfig
            {
                HostNames = ["broker:9092"],
                Username = "runner",
                Password = "secret",
                TopicName = "events",
                DefaultKafkaKey = "default-key",
            }
        );

        builder.UpdateConfiguration(
            new { Headers = new Dictionary<string, object?> { ["correlation-id"] = "123" } }
        );

        var mergedConfiguration = builder.KafkaTopic!;
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
    public void PublisherBuilder_UpdateConfiguration_WithObjectPatch_MergesRabbitMqAllFieldsAndPreservesExistingFields()
    {
        var builder = new PublisherBuilder().Configure(
            new RabbitMqSenderConfig
            {
                Host = "rabbitmq.local",
                Username = "guest",
                Password = "guest",
                Port = 5672,
                VirtualHost = "/",
                ContinuationTimeoutSeconds = 5,
                RequestedConnectionTimeoutSeconds = 5,
                HandshakeContinuationTimeoutSeconds = 10,
                ExchangeName = "events",
                RoutingKey = "published",
                ContentType = "application/json",
            }
        );

        builder.UpdateConfiguration(
            new
            {
                Host = "rabbitmq-updated.local",
                Username = "runner",
                Password = "secret",
                Port = 5673,
                VirtualHost = "/qaas",
                ContinuationTimeoutSeconds = 6,
                RequestedConnectionTimeoutSeconds = 7,
                HandshakeContinuationTimeoutSeconds = 8,
                ExchangeName = "events-updated",
                RoutingKey = "published-updated",
                Headers = new Dictionary<string, object?> { ["trace-id"] = "abc" },
                AppId = "runner",
                ClusterId = "cluster-a",
                ContentEncoding = "gzip",
                ContentType = "application/cloudevents+json",
                CorrelationId = "correlation-1",
                DeliveryMode = 2,
                Expiration = "30000",
                MessageId = "message-1",
                Persistent = true,
                Priority = 5,
                ReplyTo = "replies",
                TimestampUnixTime = 123456789L,
                Type = "event",
                UserId = "user-a",
            }
        );

        var mergedConfiguration = builder.RabbitMq!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Host, Is.EqualTo("rabbitmq-updated.local"));
            Assert.That(mergedConfiguration.Username, Is.EqualTo("runner"));
            Assert.That(mergedConfiguration.Password, Is.EqualTo("secret"));
            Assert.That(mergedConfiguration.Port, Is.EqualTo(5673));
            Assert.That(mergedConfiguration.VirtualHost, Is.EqualTo("/qaas"));
            Assert.That(mergedConfiguration.ContinuationTimeoutSeconds, Is.EqualTo(6));
            Assert.That(mergedConfiguration.RequestedConnectionTimeoutSeconds, Is.EqualTo(7));
            Assert.That(mergedConfiguration.HandshakeContinuationTimeoutSeconds, Is.EqualTo(8));
            Assert.That(mergedConfiguration.ExchangeName, Is.EqualTo("events-updated"));
            Assert.That(mergedConfiguration.QueueName, Is.Empty);
            Assert.That(mergedConfiguration.RoutingKey, Is.EqualTo("published-updated"));
            Assert.That(mergedConfiguration.Headers, Does.ContainKey("trace-id"));
            Assert.That(mergedConfiguration.Headers!["trace-id"], Is.EqualTo("abc"));
            Assert.That(mergedConfiguration.AppId, Is.EqualTo("runner"));
            Assert.That(mergedConfiguration.ClusterId, Is.EqualTo("cluster-a"));
            Assert.That(mergedConfiguration.ContentEncoding, Is.EqualTo("gzip"));
            Assert.That(
                mergedConfiguration.ContentType,
                Is.EqualTo("application/cloudevents+json")
            );
            Assert.That(mergedConfiguration.CorrelationId, Is.EqualTo("correlation-1"));
            Assert.That(mergedConfiguration.DeliveryMode, Is.EqualTo(2));
            Assert.That(mergedConfiguration.Expiration, Is.EqualTo("30000"));
            Assert.That(mergedConfiguration.MessageId, Is.EqualTo("message-1"));
            Assert.That(mergedConfiguration.Persistent, Is.True);
            Assert.That(mergedConfiguration.Priority, Is.EqualTo(5));
            Assert.That(mergedConfiguration.ReplyTo, Is.EqualTo("replies"));
            Assert.That(mergedConfiguration.TimestampUnixTime, Is.EqualTo(123456789L));
            Assert.That(mergedConfiguration.Type, Is.EqualTo("event"));
            Assert.That(mergedConfiguration.UserId, Is.EqualTo("user-a"));
        });
    }

    [Test]
    public void PublisherBuilder_UpdateConfiguration_WithObjectPatch_MergesS3BucketAndPreservesExistingFields()
    {
        var builder = new PublisherBuilder().Configure(
            new S3BucketSenderConfig
            {
                StorageBucket = "bucket",
                ServiceURL = "http://127.0.0.1:9000",
                AccessKey = "ak",
                SecretKey = "sk",
                ForcePathStyle = false,
                Prefix = "runs/",
                S3SentObjectsNaming = ObjectNamingGeneratorType.RandomGuid,
                Retries = 2,
                S3StorageClass = S3StorageClassEnum.Standard,
            }
        );

        builder.UpdateConfiguration(
            new
            {
                StorageBucket = "bucket-updated",
                ServiceURL = "http://127.0.0.1:9001",
                AccessKey = "ak-updated",
                SecretKey = "sk-updated",
                ForcePathStyle = true,
                Prefix = "future/",
                S3SentObjectsNaming = ObjectNamingGeneratorType.GrowingNumericalSeries,
                Retries = 7,
                S3StorageClass = S3StorageClassEnum.DeepArchive,
            }
        );

        var mergedConfiguration = builder.S3Bucket!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.StorageBucket, Is.EqualTo("bucket-updated"));
            Assert.That(mergedConfiguration.ServiceURL, Is.EqualTo("http://127.0.0.1:9001"));
            Assert.That(mergedConfiguration.AccessKey, Is.EqualTo("ak-updated"));
            Assert.That(mergedConfiguration.SecretKey, Is.EqualTo("sk-updated"));
            Assert.That(mergedConfiguration.ForcePathStyle, Is.True);
            Assert.That(
                mergedConfiguration.S3SentObjectsNaming,
                Is.EqualTo(ObjectNamingGeneratorType.GrowingNumericalSeries)
            );
            Assert.That(mergedConfiguration.Prefix, Is.EqualTo("future/"));
            Assert.That(mergedConfiguration.Retries, Is.EqualTo(7));
            Assert.That(
                mergedConfiguration.S3StorageClass,
                Is.EqualTo(S3StorageClassEnum.DeepArchive)
            );
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
        builder.UpdateConfiguration(new GrpcTransactorConfig());
        builder.UpdateConfiguration(new HttpTransactorConfig());

        Assert.That(builder.Policies, Has.Length.EqualTo(1));
        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.Http, Is.TypeOf<HttpTransactorConfig>());

        builder.AddDataSource("source-indexed").AddDataSourcePattern("^indexed-.*$");
        builder.RemoveDataSourceAt(1).RemoveDataSourcePatternAt(1);

        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));

        builder
            .RemoveDataSource("source-updated")
            .RemoveDataSourcePattern("^updated-.*$")
            .RemovePolicyAt(0)
            .Configure(new GrpcTransactorConfig());

        Assert.That(builder.DataSourceNames, Is.Empty);
        Assert.That(builder.DataSourcePatterns, Is.Empty);
        Assert.That(builder.Policies, Is.Empty);
        Assert.That(builder.Grpc, Is.TypeOf<GrpcTransactorConfig>());
    }

    [Test]
    public void TransactionBuilder_UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new TransactionBuilder().Configure(
            new HttpTransactorConfig
            {
                Method = HttpMethods.Put,
                BaseAddress = "https://service.local",
                Route = "/resource",
                Retries = 3,
            }
        );

        builder.UpdateConfiguration(new HttpTransactorConfig { MessageSendRetriesIntervalMs = 0 });

        var mergedConfiguration = builder.Http!;
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
        var builder = new TransactionBuilder().Configure(
            new HttpTransactorConfig
            {
                Method = HttpMethods.Put,
                BaseAddress = "https://service.local",
                Route = "/resource",
                Retries = 3,
            }
        );

        builder.UpdateConfiguration(new { MessageSendRetriesIntervalMs = 0 });

        var mergedConfiguration = builder.Http!;
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
        builder
            .AddDataSourceName("source-indexed")
            .AddDataSourcePattern("^indexed-.*$")
            .RemoveDataSourceNameAt(1)
            .RemoveDataSourcePatternAt(1);

        Assert.That(builder.DataSourceNames, Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.DataSourcePatterns, Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.ProbeConfiguration["enabled"], Is.EqualTo("True"));
        Assert.That(builder.ProbeConfiguration["threshold"], Is.EqualTo("5"));
        Assert.That(builder.ProbeConfiguration["nested:value"], Is.EqualTo("set"));

        builder
            .RemoveDataSourceName("source-updated")
            .RemoveDataSourcePattern("^updated-.*$")
            .RemoveConfiguration();

        Assert.That(builder.DataSourceNames, Is.Empty);
        Assert.That(builder.DataSourcePatterns, Is.Empty);
        Assert.That(builder.ProbeConfiguration.AsEnumerable().Any(), Is.False);
    }

    [Test]
    public void CollectorBuilder_ShouldSupportConfigurationCrud()
    {
        var builder = new CollectorBuilder().Configure(
            new PrometheusFetcherConfig { Url = "https://prometheus", Expression = "up" }
        );

        builder.UpdateConfiguration(
            new PrometheusFetcherConfig
            {
                Url = "https://prometheus-updated",
                Expression = "sum(up)",
            }
        );
        builder.UpdateConfiguration(
            new PrometheusFetcherConfig
            {
                Url = "https://prometheus-updated-again",
                Expression = "max(up)",
            }
        );

        Assert.That(builder.Prometheus, Is.TypeOf<PrometheusFetcherConfig>());
        Assert.That(builder.Prometheus!.Expression, Is.EqualTo("max(up)"));

        builder.Configure(
            new PrometheusFetcherConfig { Url = "https://prometheus-latest", Expression = "up" }
        );
        Assert.That(builder.Prometheus, Is.TypeOf<PrometheusFetcherConfig>());
    }

    [Test]
    public void CollectorBuilder_UpdateConfiguration_WithObjectPatch_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new CollectorBuilder().Configure(
            new PrometheusFetcherConfig
            {
                Url = "https://prometheus",
                Expression = "up",
                SampleIntervalMs = 5000,
            }
        );

        builder.UpdateConfiguration(new { ApiKey = "api-key" });

        var mergedConfiguration = builder.Prometheus!;
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
        var builder = new CollectorBuilder().Configure(
            new PrometheusFetcherConfig
            {
                Url = "https://prometheus",
                Expression = "up",
                SampleIntervalMs = 5000,
            }
        );

        builder.UpdateConfiguration(new PrometheusFetcherConfig { ApiKey = "api-key" });

        var mergedConfiguration = builder.Prometheus!;
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
        var builder = new MockerCommandBuilder().Configure(
            new MockerCommandConfig { Consume = new ConsumeCommandConfig() }
        );

        builder.UpdateConfiguration(
            new MockerCommandConfig { TriggerAction = new TriggerAction() }
        );
        builder.UpdateConfiguration(
            new MockerCommandConfig { Consume = new ConsumeCommandConfig() }
        );

        Assert.That(builder.Command, Is.Not.Null);
        Assert.That(builder.Command!.Consume, Is.Not.Null);

        builder.Configure(new MockerCommandConfig { TriggerAction = new TriggerAction() });
        Assert.That(builder.Command!.TriggerAction, Is.Not.Null);
    }

    [Test]
    public void MockerCommandBuilder_UpdateConfiguration_WithObjectPatch_MergesNestedCommandValues()
    {
        var builder = new MockerCommandBuilder().Configure(
            new MockerCommandConfig
            {
                TriggerAction = new TriggerAction { ActionName = "seed", TimeoutMs = 5 },
            }
        );

        builder.UpdateConfiguration(new { TriggerAction = new { TimeoutMs = 15 } });

        var command = builder.Command!;
        Assert.Multiple(() =>
        {
            Assert.That(command.TriggerAction, Is.Not.Null);
            Assert.That(command.TriggerAction!.ActionName, Is.EqualTo("seed"));
            Assert.That(command.TriggerAction.TimeoutMs, Is.EqualTo(15));
        });
    }
}
