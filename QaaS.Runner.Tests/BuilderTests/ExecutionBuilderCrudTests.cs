using NUnit.Framework;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Sessions.Session.Builders;
using QaaS.Runner.Storage;
using QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs;

namespace QaaS.Runner.Tests.BuilderTests;

[TestFixture]
public class ExecutionBuilderCrudTests
{
    [Test]
    public void CreateAndReadSession_ShouldStoreConfiguredSession()
    {
        var session = new SessionBuilder { Name = "session-a", Stage = 0, Probes = [] };
        var builder = new ExecutionBuilder().CreateSession(session);

        var sessions = builder.ReadSessions();

        Assert.Multiple(() =>
        {
            Assert.That(sessions, Has.Count.EqualTo(1));
            Assert.That(sessions[0], Is.SameAs(session));
            Assert.That(builder.ReadSession("session-a"), Is.SameAs(session));
        });
    }

    [Test]
    public void AddOperations_WhenCollectionsAreNull_InitializeConfiguredArrays()
    {
        var builder = new ExecutionBuilder
        {
            Sessions = null,
            Assertions = null,
            Storages = null,
            DataSources = null,
            Links = null
        };
        var session = new SessionBuilder { Name = "session-a", Stage = 0, Probes = [] };
        var assertion = new AssertionBuilder
        {
            Name = "assertion-a",
            Assertion = "Equals",
            AssertionInstance = null!,
            Reporter = null!
        }.HookNamed("AssertionHook");
        var storage = new StorageBuilder().Configure(new S3Config());
        var dataSource = new DataSourceBuilder().Named("source-a").HookNamed("GeneratorHook");
        var link = new LinkBuilder().Configure(new KibanaLinkConfig
        {
            Url = "https://kibana",
            DataViewId = "view"
        });

        builder.CreateSession(session)
            .CreateAssertion(assertion)
            .CreateStorage(storage)
            .CreateDataSource(dataSource)
            .CreateLink(link);

        Assert.Multiple(() =>
        {
            Assert.That(builder.ReadSessions(), Is.EqualTo(new[] { session }));
            Assert.That(builder.ReadAssertions(), Is.EqualTo(new[] { assertion }));
            Assert.That(builder.ReadStorages(), Is.EqualTo(new[] { storage }));
            Assert.That(builder.ReadDataSources(), Is.EqualTo(new[] { dataSource }));
            Assert.That(builder.ReadLinks(), Is.EqualTo(new[] { link }));
            Assert.That(builder.ReadAssertion("assertion-a"), Is.SameAs(assertion));
            Assert.That(builder.ReadStorageAt(0), Is.SameAs(storage));
            Assert.That(builder.ReadDataSource("source-a"), Is.SameAs(dataSource));
            Assert.That(builder.ReadLinkAt(0), Is.SameAs(link));
        });
    }

    [Test]
    public void ReadOperations_WhenCollectionsAreNull_ReturnEmptyCollections()
    {
        var builder = new ExecutionBuilder
        {
            Assertions = null,
            DataSources = null,
            Links = null
        };

        Assert.Multiple(() =>
        {
            Assert.That(builder.ReadAssertions(), Is.Empty);
            Assert.That(builder.ReadDataSources(), Is.Empty);
            Assert.That(builder.ReadLinks(), Is.Empty);
        });
    }

    [Test]
    public void UpdateSession_ShouldReplaceMatchingSessionByName()
    {
        var original = new SessionBuilder { Name = "session-a", Stage = 0, Probes = [] };
        var updated = new SessionBuilder { Name = "session-a", Stage = 1, Probes = [] };
        var builder = new ExecutionBuilder().CreateSession(original);

        builder.UpdateSession("session-a", updated);

        Assert.That(builder.ReadSessions()[0], Is.SameAs(updated));
    }

    [Test]
    public void DeleteSession_ShouldRemoveMatchingSessionByName()
    {
        var builder = new ExecutionBuilder()
            .CreateSession(new SessionBuilder { Name = "session-a", Stage = 0, Probes = [] })
            .CreateSession(new SessionBuilder { Name = "session-b", Stage = 1, Probes = [] });

        builder.DeleteSession("session-a");

        Assert.That(builder.ReadSessions(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadSessions()[0].Name, Is.EqualTo("session-b"));
    }

    [Test]
    public void UpdateSession_WhenSessionNameNotFound_DoesNotChangeCollection()
    {
        var original = new SessionBuilder { Name = "session-a", Stage = 0, Probes = [] };
        var replacement = new SessionBuilder { Name = "session-b", Stage = 1, Probes = [] };
        var builder = new ExecutionBuilder().CreateSession(original);

        builder.UpdateSession("does-not-exist", replacement);

        Assert.That(builder.ReadSessions(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadSessions()[0], Is.SameAs(original));
    }

    [Test]
    public void UpdateStorageAt_WithInvalidIndex_ShouldThrowArgumentOutOfRangeException()
    {
        var builder = new ExecutionBuilder().CreateStorage(new StorageBuilder().Configure(new S3Config()));

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UpdateStorageAt(3, new StorageBuilder()));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.DeleteStorageAt(-1));
    }

    [Test]
    public void AssertionsDataSourcesAndLinks_ShouldSupportCrudOperations()
    {
        var initialAssertion = new AssertionBuilder
        {
            Name = "assertion-a",
            Assertion = "Equals",
            AssertionInstance = null!,
            Reporter = null!
        }.HookNamed("HookA");
        var updatedAssertion = new AssertionBuilder
        {
            Name = "assertion-a",
            Assertion = "NotEquals",
            AssertionInstance = null!,
            Reporter = null!
        }.HookNamed("HookB");

        var builder = new ExecutionBuilder()
            .CreateAssertion(initialAssertion)
            .CreateDataSource(new DataSourceBuilder().Named("source-a").HookNamed("GeneratorA"))
            .CreateLink(new LinkBuilder().Configure(new KibanaLinkConfig { Url = "https://kibana", DataViewId = "view" }));

        builder.UpdateAssertion("assertion-a", updatedAssertion);
        builder.DeleteDataSource("source-a");
        builder.UpdateLinkAt(0, new LinkBuilder().Configure(new GrafanaLinkConfig
        {
            Url = "https://grafana",
            DashboardId = "dash",
            Variables = []
        }));

        Assert.Multiple(() =>
        {
            Assert.That(builder.ReadAssertions(), Has.Count.EqualTo(1));
            Assert.That(builder.ReadAssertions()[0], Is.SameAs(updatedAssertion));
            Assert.That(builder.ReadAssertion("assertion-a"), Is.SameAs(updatedAssertion));
            Assert.That(builder.ReadDataSources(), Is.Empty);
            Assert.That(builder.ReadDataSource("source-a"), Is.Null);
            Assert.That(builder.ReadLinks(), Has.Count.EqualTo(1));
            Assert.That(builder.ReadLinkAt(0)?.Grafana, Is.Not.Null);
        });

        builder.DeleteLinkAt(0);
        Assert.Multiple(() =>
        {
            Assert.That(builder.ReadLinks(), Is.Empty);
            Assert.That(builder.ReadLinkAt(0), Is.Null);
        });
    }
}

