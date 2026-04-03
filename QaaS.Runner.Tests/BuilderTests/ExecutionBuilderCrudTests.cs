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
        var builder = new ExecutionBuilder().AddSession(session);

        var sessions = builder.Sessions ?? [];

        Assert.Multiple(() =>
        {
            Assert.That(sessions, Has.Length.EqualTo(1));
            Assert.That(sessions[0], Is.SameAs(session));
            Assert.That((builder.Sessions ?? []).FirstOrDefault(x => x.Name == "session-a"), Is.SameAs(session));
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

        builder.AddSession(session)
            .AddAssertion(assertion)
            .AddStorage(storage)
            .AddDataSource(dataSource)
            .AddLink(link);

        Assert.Multiple(() =>
        {
            Assert.That(builder.Sessions, Is.EqualTo(new[] { session }));
            Assert.That(builder.Assertions, Is.EqualTo(new[] { assertion }));
            Assert.That(builder.Storages, Is.EqualTo(new[] { storage }));
            Assert.That(builder.DataSources, Is.EqualTo(new[] { dataSource }));
            Assert.That(builder.Links, Is.EqualTo(new[] { link }));
            Assert.That((builder.Assertions ?? []).FirstOrDefault(x => x.Name == "assertion-a"), Is.SameAs(assertion));
            Assert.That((builder.Storages ?? []).ElementAtOrDefault(0), Is.SameAs(storage));
            Assert.That((builder.DataSources ?? []).FirstOrDefault(x => x.Name == "source-a"), Is.SameAs(dataSource));
            Assert.That((builder.Links ?? []).ElementAtOrDefault(0), Is.SameAs(link));
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
            Assert.That(builder.Assertions, Is.Null);
            Assert.That(builder.DataSources, Is.Null);
            Assert.That(builder.Links, Is.Null);
        });
    }

    [Test]
    public void UpdateSession_ShouldReplaceMatchingSessionByName()
    {
        var original = new SessionBuilder { Name = "session-a", Stage = 0, Probes = [] };
        var updated = new SessionBuilder { Name = "session-a", Stage = 1, Probes = [] };
        var builder = new ExecutionBuilder().AddSession(original);

        builder.UpdateSession("session-a", updated);

        Assert.That(builder.Sessions[0], Is.SameAs(updated));
    }

    [Test]
    public void DeleteSession_ShouldRemoveMatchingSessionByName()
    {
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder { Name = "session-a", Stage = 0, Probes = [] })
            .AddSession(new SessionBuilder { Name = "session-b", Stage = 1, Probes = [] });

        builder.RemoveSession("session-a");

        Assert.That(builder.Sessions ?? [], Has.Length.EqualTo(1));
        Assert.That(builder.Sessions[0].Name, Is.EqualTo("session-b"));
    }

    [Test]
    public void UpdateSession_WhenSessionNameNotFound_DoesNotChangeCollection()
    {
        var original = new SessionBuilder { Name = "session-a", Stage = 0, Probes = [] };
        var replacement = new SessionBuilder { Name = "session-b", Stage = 1, Probes = [] };
        var builder = new ExecutionBuilder().AddSession(original);

        builder.UpdateSession("does-not-exist", replacement);

        Assert.That(builder.Sessions ?? [], Has.Length.EqualTo(1));
        Assert.That(builder.Sessions[0], Is.SameAs(original));
    }

    [Test]
    public void UpdateStorageAt_WithInvalidIndex_ShouldThrowArgumentOutOfRangeException()
    {
        var builder = new ExecutionBuilder().AddStorage(new StorageBuilder().Configure(new S3Config()));

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UpdateStorageAt(3, new StorageBuilder()));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.RemoveStorageAt(-1));
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
            .AddAssertion(initialAssertion)
            .AddDataSource(new DataSourceBuilder().Named("source-a").HookNamed("GeneratorA"))
            .AddLink(new LinkBuilder().Configure(new KibanaLinkConfig { Url = "https://kibana", DataViewId = "view" }));

        builder.UpdateAssertion("assertion-a", updatedAssertion);
        builder.RemoveDataSource("source-a");
        builder.UpdateLinkAt(0, new LinkBuilder().Configure(new GrafanaLinkConfig
        {
            Url = "https://grafana",
            DashboardId = "dash",
            Variables = []
        }));

        Assert.Multiple(() =>
        {
            Assert.That(builder.Assertions ?? [], Has.Length.EqualTo(1));
            Assert.That(builder.Assertions[0], Is.SameAs(updatedAssertion));
            Assert.That((builder.Assertions ?? []).FirstOrDefault(x => x.Name == "assertion-a"), Is.SameAs(updatedAssertion));
            Assert.That(builder.DataSources, Has.Length.EqualTo(0));
            Assert.That((builder.DataSources ?? []).FirstOrDefault(x => x.Name == "source-a"), Is.Null);
            Assert.That(builder.Links ?? [], Has.Length.EqualTo(1));
            Assert.That((builder.Links ?? []).ElementAtOrDefault(0)?.Grafana, Is.Not.Null);
        });

        builder.RemoveLinkAt(0);
        Assert.Multiple(() =>
        {
            Assert.That(builder.Links, Has.Length.EqualTo(0));
            Assert.That((builder.Links ?? []).ElementAtOrDefault(0), Is.Null);
        });
    }
}




