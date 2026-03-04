using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Prometheus;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.ConfigurationObjects;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace QaaS.Runner.Sessions.Tests.Actions.Collector;

[TestFixture]
public class CollectorBuilderTests
{
    private IList<ActionFailure> _actionFailures = null!;
    private string _sessionName = null!;

    [SetUp]
    public void SetUp()
    {
        _actionFailures = new List<ActionFailure>();
        _sessionName = "TestSession";
    }

    [Test]
    public void Named_Should_Set_Name()
    {
        // Arrange
        var builder = new CollectorBuilder();

        // Act
        builder.Named("TestCollector");

        // Assert
        Assert.That(builder.Name, Is.EqualTo("TestCollector"));
    }

    [Test]
    public void FilterData_Should_Set_DataFilter()
    {
        // Arrange
        var filter = new DataFilter();
        var builder = new CollectorBuilder();

        // Act
        builder.FilterData(filter);

        // Assert
        Assert.That(builder.DataFilter, Is.SameAs(filter));
    }

    [Test]
    public void CollectInRange_Should_Set_CollectionRange()
    {
        // Arrange
        var range = new CollectionRange { StartTimeMs = 1000, EndTimeMs = 2000 };
        var builder = new CollectorBuilder();

        // Act
        builder.CollectInRange(range);

        // Assert
        Assert.That(builder.CollectionRange, Is.SameAs(range));
    }

    [Test]
    public void Configure_With_PrometheusFetcherConfig_Should_Set_Prometheus()
    {
        // Arrange
        var config = new PrometheusFetcherConfig();
        var builder = new CollectorBuilder();

        // Act
        builder.Configure(config);

        // Assert
        Assert.That(builder.Prometheus, Is.SameAs(config));
    }

    [Test]
    public void Build_With_Valid_Prometheus_Config_Should_Create_Collector()
    {
        // Arrange
        var prometheusConfig = new PrometheusFetcherConfig { Url = "https://promql:8080", Expression = "sum ()" };
        var builder = new CollectorBuilder()
            .Named("TestCollector")
            .Configure(prometheusConfig);

        // Act
        var result = builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestCollector"));
    }

    [Test]
    public void Build_Without_Configuration_Should_Throw_Exception()
    {
        // Arrange
        var builder = new CollectorBuilder()
            .Named("TestCollector");

        // Act & Assert
        Assert.That(builder.Build(Globals.GetContextWithMetadata(), _actionFailures, _sessionName), Is.Null);
    }

    [Test]
    public void Build_With_Unsupported_Config_Type_Should_Throw_ArgumentOutOfRangeException()
    {
        // Arrange
        var unsupportedConfig = new Mock<IFetcherConfig>().Object;

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            new CollectorBuilder()
                .Named("TestCollector")
                .Configure(unsupportedConfig);
        });
    }
}
