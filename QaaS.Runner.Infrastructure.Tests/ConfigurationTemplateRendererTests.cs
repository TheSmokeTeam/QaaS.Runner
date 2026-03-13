using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace QaaS.Runner.Infrastructure.Tests;

[TestFixture]
public class ConfigurationTemplateRendererTests
{
    [Test]
    public void Render_UsesMergedConfigurationValuesAndAugmentsAssertionStatuses()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storages:0:FileSystem:Path"] = "SessionDataStorage",
                ["DataSources:0:Name"] = "RabbitPayload",
                ["DataSources:0:Generator"] = "TestGenerator",
                ["DataSources:0:GeneratorConfiguration:Count"] = "1",
                ["Sessions:0:Name"] = "RabbitRoundTrip",
                ["Sessions:0:SaveData"] = "true",
                ["Sessions:0:Publishers:0:Name"] = "PublishToRabbit",
                ["Sessions:0:Publishers:0:DataSourceNames:0"] = "RabbitPayload",
                ["Sessions:0:Publishers:0:RabbitMq:Host"] = "localhost",
                ["Sessions:0:Publishers:0:RabbitMq:Port"] = "5672",
                ["Sessions:0:Publishers:0:RabbitMq:RoutingKey"] = "/",
                ["Sessions:0:Publishers:0:RabbitMq:ExchangeName"] = "test",
                ["Sessions:0:Consumers:0:Name"] = "ConsumeFromRabbit",
                ["Sessions:0:Consumers:0:TimeoutMs"] = "20000",
                ["Sessions:0:Consumers:0:RabbitMq:Host"] = "localhost",
                ["Sessions:0:Consumers:0:RabbitMq:Port"] = "5672",
                ["Sessions:0:Consumers:0:RabbitMq:RoutingKey"] = "/",
                ["Sessions:0:Consumers:0:RabbitMq:ExchangeName"] = "test",
                ["Assertions:0:Name"] = "RabbitRoundTripAssertion",
                ["Assertions:0:Assertion"] = "RabbitRoundTripAssertion",
                ["Assertions:0:SessionNames:0"] = "RabbitRoundTrip",
                ["MetaData:System"] = "QaaS",
                ["MetaData:Team"] = "Smoke"
            })
            .Build();

        var yaml = ConfigurationTemplateRenderer.Render(
            configuration,
            sectionOrder: Constants.ConfigurationSectionNames,
            includedSessionNames: new HashSet<string>(["RabbitRoundTrip"], StringComparer.Ordinal),
            assertionStatusesToReport: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["RabbitRoundTripAssertion"] = ["Passed", "Failed", "Broken", "Unknown", "Skipped"]
            });

        Assert.That(yaml, Does.Contain("Storages:"));
        Assert.That(yaml, Does.Contain("Path: SessionDataStorage"));
        Assert.That(yaml, Does.Contain("GeneratorConfiguration:"));
        Assert.That(yaml, Does.Contain("Count: 1"));
        Assert.That(yaml, Does.Contain("SaveData: true"));
        Assert.That(yaml, Does.Contain("Port: 5672"));
        Assert.That(yaml, Does.Contain("RoutingKey: /"));
        Assert.That(yaml, Does.Contain("StatusesToReport:"));
        Assert.That(yaml, Does.Contain("- Passed"));
        Assert.That(yaml, Does.Not.Contain("CreatedQueueTimeToExpireMs"));
    }

    [Test]
    public void Render_FiltersSessionsAndAssertionsToTheSelectedRuntimeSet()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sessions:0:Name"] = "RabbitRoundTrip",
                ["Sessions:1:Name"] = "ShouldBeFilteredOut",
                ["Assertions:0:Name"] = "RabbitRoundTripAssertion",
                ["Assertions:0:Assertion"] = "RabbitRoundTripAssertion",
                ["Assertions:1:Name"] = "FilteredAssertion",
                ["Assertions:1:Assertion"] = "FilteredAssertion"
            })
            .Build();

        var yaml = ConfigurationTemplateRenderer.Render(
            configuration,
            sectionOrder: Constants.ConfigurationSectionNames,
            includedSessionNames: new HashSet<string>(["RabbitRoundTrip"], StringComparer.Ordinal),
            assertionStatusesToReport: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["RabbitRoundTripAssertion"] = ["Passed"]
            });

        Assert.That(yaml, Does.Contain("RabbitRoundTrip"));
        Assert.That(yaml, Does.Contain("RabbitRoundTripAssertion"));
        Assert.That(yaml, Does.Not.Contain("ShouldBeFilteredOut"));
        Assert.That(yaml, Does.Not.Contain("FilteredAssertion"));
    }
}
