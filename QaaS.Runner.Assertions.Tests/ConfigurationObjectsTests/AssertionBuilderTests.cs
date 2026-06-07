using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Reporters.Allure;
using QaaS.Runner.Assertions.Reporters.ReportPortal;
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
        Assert.That(builtAssertion.StatusesToReport, Is.EquivalentTo([AssertionStatus.Passed, AssertionStatus.Failed]));
        Assert.That(builtAssertion.Links, Has.Count.EqualTo(2));
    }

    [Test]
    public void ReporterBuilder_Build_AppliesRuntimePropertiesToAllureReporter()
    {
        var context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build()
        };
        var fileSystem = new System.IO.Abstractions.FileSystem();
        var startTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var reporters = new ReporterBuilder().Build(context, startTime, fileSystem: fileSystem);
        var allureReporter = reporters.OfType<AllureReporter>().Single();

        Assert.That(reporters, Has.Count.EqualTo(1));
        Assert.That(allureReporter.Context, Is.SameAs(context));
        Assert.That(allureReporter.SaveSessionData, Is.Null);
        Assert.That(allureReporter.SaveLogs, Is.Null);
        Assert.That(allureReporter.SaveAttachments, Is.Null);
        Assert.That(allureReporter.SaveTemplate, Is.Null);
        Assert.That(allureReporter.DisplayTrace, Is.Null);
        Assert.That(allureReporter.FileSystem, Is.SameAs(fileSystem));
        Assert.That(allureReporter.EpochTestSuiteStartTime,
            Is.EqualTo(new DateTimeOffset(startTime, TimeSpan.Zero).ToUnixTimeMilliseconds()));
    }

    [Test]
    public void ReporterBuilder_Build_WithoutReportPortalLaunchManager_ReturnsAllureReporterOnly()
    {
        var context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build()
        };
        var reporters = new ReporterBuilder()
            .ConfigureReportPortal(CreateReportPortalConfig(enabled: true))
            .Build(context, new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));

        Assert.That(reporters, Has.Count.EqualTo(1));
        Assert.That(reporters[0], Is.TypeOf<AllureReporter>());
    }

    [Test]
    public void ReporterBuilder_Build_WithDisabledReportPortal_ReturnsAllureReporterOnly()
    {
        var context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build()
        };
        using var reportPortalLaunchManager = new ReportPortalLaunchManager();

        var reporters = new ReporterBuilder()
            .ConfigureReportPortal(CreateReportPortalConfig(enabled: false))
            .WithReportPortalLaunchManager(reportPortalLaunchManager)
            .Build(context, new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));

        Assert.That(reporters, Has.Count.EqualTo(1));
        Assert.That(reporters[0], Is.TypeOf<AllureReporter>());
    }

    [Test]
    public void ReporterBuilder_Build_WithEnabledReportPortal_ReturnsAllureAndReportPortalReporters()
    {
        var context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build()
        };
        var runDescriptor = new ReportPortalLaunchDescriptor(
            "Smoke",
            "QaaS",
            ["Session A"],
            "run",
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero));
        using var reportPortalLaunchManager = new ReportPortalLaunchManager();

        var reporters = new ReporterBuilder()
            .ConfigureReportPortal(CreateReportPortalConfig(enabled: true))
            .WithReportPortalLaunchManager(reportPortalLaunchManager)
            .WithReportPortalRunDescriptor(runDescriptor)
            .Build(context, new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var reportPortalReporter = reporters.OfType<ReportPortalReporter>().Single();

        Assert.That(reporters, Has.Count.EqualTo(2));
        Assert.That(reporters.OfType<AllureReporter>(), Has.Exactly(1).Items);
        Assert.That(reportPortalReporter.Settings.RequestedProjectName, Is.EqualTo("Smoke"));
        Assert.That(reportPortalReporter.LaunchManager, Is.SameAs(reportPortalLaunchManager));
    }

    [Test]
    public void Read_Always_ThrowsNotSupportedException()
    {
        var builder = CreateBuilder();

        Assert.Throws<NotSupportedException>(() =>
            builder.Read(null!, typeof(AssertionBuilder), null!));
    }

    [Test]
    public void SessionCrudMethods_WhenCollectionsAreNull_InitializeAndReturnEmptyFallbacks()
    {
        var builder = CreateBuilder();
        builder.SessionNames = null;
        builder.SessionNamePatterns = null;

        Assert.Multiple(() =>
        {
            Assert.That(builder.SessionNames, Is.Null);
            Assert.That(builder.SessionNamePatterns, Is.Null);
        });

        builder.AddSessionName("session-a")
            .AddSessionPattern("^session-.*$");

        Assert.Multiple(() =>
        {
            Assert.That(builder.SessionNames, Is.EqualTo(new[] { "session-a" }));
            Assert.That(builder.SessionNamePatterns, Is.EqualTo(new[] { "^session-.*$" }));
        });
    }

    [Test]
    public void SessionCrudMethods_WhenCollectionsAreNullOrKeysMissing_LeaveBuilderUnchanged()
    {
        var builder = CreateBuilder();
        builder.SessionNames = null;
        builder.SessionNamePatterns = null;

        builder.RemoveSessionName("missing")
            .RemoveSessionPattern("missing");

        Assert.Multiple(() =>
        {
            Assert.That(builder.SessionNames, Is.Null);
            Assert.That(builder.SessionNamePatterns, Is.Null);
        });

        builder.AddSessionName("session-a")
            .AddSessionPattern("^session-.*$")
            .RemoveSessionName("other-session")
            .RemoveSessionPattern("^other$");

        Assert.Multiple(() =>
        {
            Assert.That(builder.SessionNames, Is.EqualTo(new[] { "session-a" }));
            Assert.That(builder.SessionNamePatterns, Is.EqualTo(new[] { "^session-.*$" }));
        });
    }

    [Test]
    public void Write_SerializesAssertionPayloadWithConfigurationDictionary()
    {
        var builder = CreateBuilder()
            .Named("assertion-display")
            .HookNamed("hook-type")
            .WithCategory("smoke")
            .ShouldSaveLogs(false)
            .Configure(new
            {
                Enabled = true,
                Threshold = 7
            });
        object? serialized = null;

        builder.Write(null!, (payload, _) => serialized = payload);

        Assert.That(serialized, Is.Not.Null);
        var serializedType = serialized!.GetType();
        var assertionConfiguration = serializedType.GetProperty("AssertionConfiguration")!.GetValue(serialized) as IDictionary;

        Assert.Multiple(() =>
        {
            Assert.That(builder.Category, Is.EqualTo("smoke"));
            Assert.That(serializedType.GetProperty("Assertion")!.GetValue(serialized), Is.EqualTo("hook-type"));
            Assert.That(serializedType.GetProperty("Name")!.GetValue(serialized), Is.EqualTo("assertion-display"));
            Assert.That(serializedType.GetProperty("SaveLogs")!.GetValue(serialized), Is.EqualTo(false));
        });
        Assert.That(assertionConfiguration, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(assertionConfiguration!["Enabled"], Is.EqualTo("True"));
            Assert.That(assertionConfiguration["Threshold"], Is.EqualTo("7"));
        });
    }

    [Test]
    public void Build_WithNullGlobalLinks_UsesOnlyLocalLinks()
    {
        var builder = CreateBuilder()
            .Named("assertion-display")
            .HookNamed("hook-type")
            .AddLink(new LinkBuilder().Named("local-link").Configure(new PrometheusLinkConfig
            {
                Url = "https://prometheus.local",
                Expressions = ["up"]
            }));
        var assertionHook = new AssertionHookMock();

        var builtAssertion = builder.Build(
            new List<KeyValuePair<string, IAssertion>>
            {
                new("assertion-display", assertionHook)
            },
            null);

        Assert.That(builtAssertion.AssertionHook, Is.SameAs(assertionHook));
        Assert.That(builtAssertion.Links, Has.Count.EqualTo(1));
        Assert.That(builtAssertion.Links, Is.Not.Null);
        Assert.That(builtAssertion.Links![0], Is.TypeOf<global::QaaS.Runner.Assertions.LinkBuilders.PrometheusLink>());
    }

    [Test]
    public void RemoveLinkAt_WhenLinksIsNull_ThrowsArgumentOutOfRangeExceptionInsteadOfNullReferenceException()
    {
        var builder = CreateBuilder();
        builder.Links = null!;

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.RemoveLinkAt(0));
    }

    [Test]
    public void UpdateConfiguration_WithIndexedInputNames_ReplacesExistingIndexes()
    {
        var builder = CreateBuilder()
            .Configure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["InputNames"] = "scalar-that-should-not-survive",
                    ["InputNames:0"] = "Name1",
                    ["InputNames:1"] = "StaleName"
                })
                .Build());

        builder.UpdateConfiguration(new
        {
            InputNames = new[] { "Name2" }
        });

        Assert.Multiple(() =>
        {
            Assert.That(builder.Configuration["InputNames:0"], Is.EqualTo("Name2"));
            Assert.That(builder.Configuration["InputNames:1"], Is.Null);
            Assert.That(builder.Configuration["InputNames"], Is.Null);
            Assert.That(builder.Configuration.AsEnumerable().Count(pair =>
                pair.Key.StartsWith("InputNames:", StringComparison.OrdinalIgnoreCase) &&
                pair.Value != null), Is.EqualTo(1));
        });
    }

    private static AssertionBuilder CreateBuilder()
    {
        return new AssertionBuilder
        {
            AssertionInstance = null!
        };
    }

    private static ReportPortalConfig CreateReportPortalConfig(bool enabled)
    {
        return new ReportPortalConfig { Enabled = enabled };
    }
}
