using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Assertions.Tests.Mocks;

namespace QaaS.Runner.Assertions.Tests.ConfigurationObjectsTests;

[TestFixture]
public class AssertionBuilderTests
{
    [Test]
    public void Build_WithMissingAssertionHook_ThrowsArgumentException()
    {
        var builder = CreateBuilder()
            .Named("assertion-display")
            .HookNamed("hook-type");

        var assertionHooks = new List<KeyValuePair<string, IAssertion>>();
        var links = new List<LinkBuilder>();

        Assert.Throws<ArgumentException>(() =>
            builder.Build(assertionHooks, links));
    }

    [Test]
    public void Build_WithValidConfiguration_BuildsAssertionWithExpectedValues()
    {
        var builder = CreateBuilder()
            .Named("assertion-display")
            .HookNamed("hook-type")
            .AddSessionName("session-1")
            .AddSessionName("session-2")
            .AddSessionPattern("^session-.*$")
            .AddDataSourceName("source-1")
            .AddDataSourcePattern("^source-.*$")
            .AddLink(new LinkBuilder().Named("local-link").Configure(new PrometheusLinkConfig
            {
                Url = "https://prometheus.local",
                Expressions = ["up"]
            }))
            .ReportOnlyStatuses([AssertionStatus.Passed, AssertionStatus.Failed]);

        var globalLinks = new List<LinkBuilder>
        {
            new LinkBuilder()
                .Named("global-link")
                .Configure(new KibanaLinkConfig
                {
                    Url = "https://kibana.local",
                    DataViewId = "data-view"
                })
        };

        var assertionHook = new AssertionHookMock();
        var assertionHooks = new List<KeyValuePair<string, IAssertion>>
        {
            new KeyValuePair<string, IAssertion>("assertion-display", assertionHook)
        };
        var builtAssertion = builder.Build(assertionHooks, globalLinks);

        Assert.That(builtAssertion.Name, Is.EqualTo("assertion-display"));
        Assert.That(builtAssertion.AssertionName, Is.EqualTo("hook-type"));
        Assert.That(builtAssertion.AssertionHook, Is.SameAs(assertionHook));
        Assert.That(builtAssertion._sessionNames, Is.EquivalentTo(["session-1", "session-2"]));
        Assert.That(builtAssertion._sessionPatterns, Is.EquivalentTo(["^session-.*$"]));
        Assert.That(builtAssertion._dataSourceNames, Is.EquivalentTo(["source-1"]));
        Assert.That(builtAssertion._dataSourcePatterns, Is.EquivalentTo(["^source-.*$"]));
        Assert.That(builtAssertion.StatussesToReport, Is.EquivalentTo([AssertionStatus.Passed, AssertionStatus.Failed]));
        Assert.That(builtAssertion.Links, Has.Count.EqualTo(2));
    }

    [Test]
    public void BuildReporter_AppliesRuntimePropertiesToReporter()
    {
        var builder = CreateBuilder()
            .Named("assertion-display")
            .WeatherToSaveSessionData(false)
            .WeatherToSaveAttachments(false)
            .WeatherToSaveConfigurationTemplate(false)
            .WeatherToDisplayTrace(false)
            .WithSeverity(AssertionSeverity.Critical);

        var context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build()
        };
        var fileSystem = new System.IO.Abstractions.FileSystem();
        var startTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var reporter = builder.Build(context, startTime, fileSystem);
        var allureReporter = reporter as AllureReporter;

        Assert.That(allureReporter, Is.Not.Null);
        Assert.That(allureReporter!.Name, Is.EqualTo("assertion-display"));
        Assert.That(allureReporter.Context, Is.SameAs(context));
        Assert.That(allureReporter.SaveSessionData, Is.False);
        Assert.That(allureReporter.SaveAttachments, Is.False);
        Assert.That(allureReporter.SaveTemplate, Is.False);
        Assert.That(allureReporter.DisplayTrace, Is.False);
        Assert.That(allureReporter.Severity, Is.EqualTo(AssertionSeverity.Critical));
        Assert.That(allureReporter.FileSystem, Is.SameAs(fileSystem));
        Assert.That(allureReporter.EpochTestSuiteStartTime,
            Is.EqualTo(new DateTimeOffset(startTime, TimeSpan.Zero).ToUnixTimeMilliseconds()));
    }

    [Test]
    public void Read_Always_ThrowsNotSupportedException()
    {
        var builder = CreateBuilder();

        Assert.Throws<NotSupportedException>(() =>
            builder.Read(null!, typeof(AssertionBuilder), null!));
    }

    private static AssertionBuilder CreateBuilder()
    {
        return new AssertionBuilder
        {
            AssertionInstance = null!,
            Reporter = null!
        };
    }
}
