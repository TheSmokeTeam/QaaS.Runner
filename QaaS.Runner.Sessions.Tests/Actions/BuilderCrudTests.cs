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

        Assert.That(builder.ReadPolicies(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadConfiguration(), Is.TypeOf<KafkaTopicReaderConfig>());

        builder.DeleteConfiguration();
        Assert.That(builder.ReadConfiguration(), Is.Null);
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

        Assert.That(builder.ReadDataSources(), Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.ReadDataSourcePatterns(), Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.ReadPolicies(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadConfiguration(), Is.TypeOf<SocketSenderConfig>());

        builder.DeleteConfiguration();
        Assert.That(builder.ReadConfiguration(), Is.Null);
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

        Assert.That(builder.ReadPolicies(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadDataSources(), Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.ReadDataSourcePatterns(), Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.ReadConfiguration(), Is.TypeOf<GrpcTransactorConfig>());

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
    public void ProbeBuilder_ShouldSupportDataSourceCrud()
    {
        var builder = new ProbeBuilder()
            .CreateDataSourceName("source-a")
            .CreateDataSourcePattern("^source-.*$");

        builder.UpdateDataSourceName("source-a", "source-updated");
        builder.UpdateDataSourcePattern("^source-.*$", "^updated-.*$");

        Assert.That(builder.ReadDataSourceNames(), Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.ReadDataSourcePatterns(), Is.EquivalentTo(["^updated-.*$"]));

        builder.RemoveDataSourceName("source-updated")
            .RemoveDataSourcePattern("^updated-.*$");

        Assert.That(builder.ReadDataSourceNames(), Is.Empty);
        Assert.That(builder.ReadDataSourcePatterns(), Is.Empty);
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

        Assert.That(builder.ReadConfiguration(), Is.TypeOf<PrometheusFetcherConfig>());

        builder.DeleteConfiguration();
        Assert.That(builder.ReadConfiguration(), Is.Null);
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

        Assert.That(builder.ReadCommand(), Is.Not.Null);
        Assert.That(builder.ReadCommand()!.TriggerAction, Is.Not.Null);

        builder.DeleteCommand();
        Assert.That(builder.ReadCommand(), Is.Null);
    }
}

