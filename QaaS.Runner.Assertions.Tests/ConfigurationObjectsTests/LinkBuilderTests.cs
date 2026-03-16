using System;
using System.Collections.Generic;
using NUnit.Framework;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Assertions.LinkBuilders;

namespace QaaS.Runner.Assertions.Tests.ConfigurationObjectsTests;

[TestFixture]
public class LinkBuilderTests
{
    [Test]
    public void ReadConfiguration_WithoutConfiguredType_ReturnsNull()
    {
        var builder = new LinkBuilder();

        Assert.That(builder.ReadConfiguration(), Is.Null);
    }

    [Test]
    public void ReadConfiguration_WithKibanaConfig_ReturnsKibanaConfig()
    {
        var config = new KibanaLinkConfig
        {
            Url = "https://kibana.local",
            DataViewId = "data-view"
        };
        var builder = new LinkBuilder()
            .Configure(config);

        Assert.That(builder.ReadConfiguration(), Is.SameAs(config));
    }

    [Test]
    public void ReadConfiguration_WithPrometheusConfig_ReturnsPrometheusConfig()
    {
        var config = new PrometheusLinkConfig
        {
            Url = "https://prometheus.local",
            Expressions = ["up"]
        };
        var builder = new LinkBuilder()
            .Configure(config);

        Assert.That(builder.ReadConfiguration(), Is.SameAs(config));
    }

    [Test]
    public void ReadConfiguration_WithGrafanaConfig_ReturnsGrafanaConfig()
    {
        var config = new GrafanaLinkConfig
        {
            Url = "https://grafana.local",
            DashboardId = "dashboard-id"
        };
        var builder = new LinkBuilder()
            .Configure(config);

        Assert.That(builder.ReadConfiguration(), Is.SameAs(config));
    }

    [Test]
    public void UpdateConfiguration_WithoutExistingConfiguration_ThrowsInvalidOperationException()
    {
        var builder = new LinkBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.UpdateConfiguration(config => config));
    }

    [Test]
    public void Build_WithoutConfiguredType_ThrowsInvalidOperationException()
    {
        var builder = new LinkBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void Build_WithKibanaConfig_ReturnsKibanaLinkWithConfiguredName()
    {
        var builder = new LinkBuilder()
            .Named("kibana-link")
            .Configure(new KibanaLinkConfig
            {
                Url = "https://kibana.local",
                DataViewId = "data-view"
            });

        var result = builder.Build();
        var link = result.GetLink([new KeyValuePair<DateTime, DateTime>(DateTime.UtcNow, DateTime.UtcNow)]);

        Assert.That(result, Is.TypeOf<KibanaLink>());
        Assert.That(link.Key, Is.EqualTo("kibana-link"));
    }

    [Test]
    public void Build_WithPrometheusConfig_ReturnsPrometheusLink()
    {
        var builder = new LinkBuilder()
            .Configure(new PrometheusLinkConfig
            {
                Url = "https://prometheus.local",
                Expressions = ["up"]
            });

        var result = builder.Build();

        Assert.That(result, Is.TypeOf<PrometheusLink>());
    }

    [Test]
    public void Build_WithGrafanaConfig_ReturnsGrafanaLink()
    {
        var builder = new LinkBuilder()
            .Configure(new GrafanaLinkConfig
            {
                Url = "https://grafana.local",
                DashboardId = "dashboard-id"
            });

        var result = builder.Build();

        Assert.That(result, Is.TypeOf<GrafanaLink>());
    }

    [Test]
    public void Build_WithoutConfiguredName_UsesConfigTypeName()
    {
        var builder = new LinkBuilder()
            .Configure(new KibanaLinkConfig
            {
                Url = "https://kibana.local",
                DataViewId = "data-view"
            });

        var link = builder.Build()
            .GetLink([new KeyValuePair<DateTime, DateTime>(DateTime.UtcNow, DateTime.UtcNow)]);

        Assert.That(link.Key, Does.Contain(nameof(KibanaLinkConfig)));
    }

    [Test]
    public void Build_WithMultipleConfiguredTypes_ThrowsInvalidOperationException()
    {
        var builder = new LinkBuilder
        {
            Kibana = new KibanaLinkConfig
            {
                Url = "https://kibana.local",
                DataViewId = "data-view"
            },
            Prometheus = new PrometheusLinkConfig
            {
                Url = "https://prometheus.local",
                Expressions = ["up"]
            }
        };

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void Configure_WhenCalledMultipleTimes_ResetsPreviousConfiguration()
    {
        var builder = new LinkBuilder()
            .Configure(new KibanaLinkConfig
            {
                Url = "https://kibana.local",
                DataViewId = "data-view"
            })
            .Configure(new GrafanaLinkConfig
            {
                Url = "https://grafana.local",
                DashboardId = "dashboard-id"
            });

        var result = builder.Build();

        Assert.That(result, Is.TypeOf<GrafanaLink>());
    }
}
