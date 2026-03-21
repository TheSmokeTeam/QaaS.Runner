using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

namespace QaaS.Runner.Assertions.Tests.ConfigurationObjectsTests;

[TestFixture]
public class BuilderCrudTests
{
    [Test]
    public void AssertionBuilder_ShouldSupportSessionDataSourceLinkAndConfigurationCrud()
    {
        var builder = new AssertionBuilder
        {
            AssertionInstance = null!,
            Reporter = null!
        };

        builder.CreateSessionName("session-a")
            .CreateSessionPattern("^session-.*$")
            .CreateDataSourceName("source-a")
            .CreateDataSourcePattern("^source-.*$")
            .CreateLink(new LinkBuilder().Named("link-a").Configure(new KibanaLinkConfig
            {
                Url = "https://kibana",
                DataViewId = "view"
            }))
            .Configure(new { key = "value" });

        builder.UpdateSessionName("session-a", "session-updated")
            .UpdateSessionPattern("^session-.*$", "^updated-.*$")
            .UpdateDataSourceName("source-a", "source-updated")
            .UpdateDataSourcePattern("^source-.*$", "^updated-source-.*$")
            .UpdateLink("link-a", new LinkBuilder().Named("link-updated").Configure(new PrometheusLinkConfig
            {
                Url = "https://prometheus",
                Expressions = ["up"]
            }))
            .UpdateConfiguration(new { changed = "yes" })
            .UpdateConfiguration(new { nested = new { enabled = true } });

        Assert.That(builder.ReadSessionNames(), Is.EquivalentTo(["session-updated"]));
        Assert.That(builder.ReadSessionPatterns(), Is.EquivalentTo(["^updated-.*$"]));
        Assert.That(builder.ReadDataSourceNames(), Is.EquivalentTo(["source-updated"]));
        Assert.That(builder.ReadDataSourcePatterns(), Is.EquivalentTo(["^updated-source-.*$"]));
        Assert.That(builder.ReadLinks(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadLinks()[0].Name, Is.EqualTo("link-updated"));
        Assert.That(builder.ReadConfiguration()["key"], Is.EqualTo("value"));
        Assert.That(builder.ReadConfiguration()["changed"], Is.EqualTo("yes"));
        Assert.That(builder.ReadConfiguration()["nested:enabled"], Is.EqualTo("True"));

        builder.DeleteSessionName("session-updated")
            .DeleteSessionPattern("^updated-.*$")
            .DeleteDataSourceName("source-updated")
            .DeleteDataSourcePattern("^updated-source-.*$")
            .DeleteLink("link-updated")
            .DeleteConfiguration();

        Assert.That(builder.ReadSessionNames(), Is.Empty);
        Assert.That(builder.ReadSessionPatterns(), Is.Empty);
        Assert.That(builder.ReadDataSourceNames(), Is.Empty);
        Assert.That(builder.ReadDataSourcePatterns(), Is.Empty);
        Assert.That(builder.ReadLinks(), Is.Empty);
        Assert.That(builder.ReadConfiguration().AsEnumerable().Any(), Is.False);
    }

    [Test]
    public void LinkBuilder_ShouldSupportConfigurationCrud()
    {
        var builder = new LinkBuilder()
            .CreateConfiguration(new KibanaLinkConfig { Url = "https://kibana", DataViewId = "view" });

        builder.UpdateConfiguration(_ => new GrafanaLinkConfig
        {
            Url = "https://grafana",
            DashboardId = "dash"
        });
        builder.UpdateConfiguration(new KibanaLinkConfig
        {
            Url = "https://kibana-updated",
            DataViewId = "view-2"
        });

        Assert.That(builder.ReadConfiguration(), Is.TypeOf<KibanaLinkConfig>());

        builder.DeleteConfiguration();
        Assert.That(builder.ReadConfiguration(), Is.Null);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void LinkBuilder_UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new LinkBuilder()
            .CreateConfiguration(new KibanaLinkConfig
            {
                Url = "https://kibana",
                DataViewId = "view",
                TimestampField = "custom-timestamp"
            });

        builder.UpdateConfiguration(new KibanaLinkConfig
        {
            KqlQuery = "service.name : api"
        });

        var mergedConfiguration = (KibanaLinkConfig)builder.ReadConfiguration()!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.Url, Is.EqualTo("https://kibana"));
            Assert.That(mergedConfiguration.DataViewId, Is.EqualTo("view"));
            Assert.That(mergedConfiguration.TimestampField, Is.EqualTo("custom-timestamp"));
            Assert.That(mergedConfiguration.KqlQuery, Is.EqualTo("service.name : api"));
        });
    }
}
