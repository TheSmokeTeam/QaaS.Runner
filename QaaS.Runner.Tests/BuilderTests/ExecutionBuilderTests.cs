using System.Reflection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.Configurations.CustomExceptions;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Session.Builders;
using QaaS.Runner.Storage;
using QaaS.Runner.Storage.ConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs;
using QaaS.Runner.Tests.TestObjects;

namespace QaaS.Runner.Tests.BuilderTests;

public class ExecutionBuilderTests
{
    [SetUp]
    public void SetUp()
    {
        ProbeRunRecorder.Reset();
    }

    [Test]
    public void TestBuild_CallFunctionWithValidAndInvalidConfiguration_ShouldThrowErrorOnInvalid()
    {
        // Arrange
        var validBuilder = CreateValidExecutionBuilder();
        var invalidBuilder = CreateInvalidExecutionBuilder();

        // Act & Assert
        Assert.DoesNotThrow(() => validBuilder.Build());

        Assert.Throws<InvalidConfigurationsException>(() => invalidBuilder.Build());
    }

    [Test]
    public void TestBuild_CallFunctionWithDifferentConfiguration_ShouldBuildValidExecution()
    {
        // Arrange
        var builder = CreateValidExecutionBuilder();

        // Act
        var execution = builder.Build();

        // Assert - Verify the execution was built correctly
        Assert.That(execution, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(execution.AssertionLogic, Is.Not.Null);
            Assert.That(execution.ReportLogic, Is.Not.Null);
            Assert.That(execution.SessionLogic, Is.Not.Null);
            Assert.That(execution.TemplateLogic, Is.Not.Null);
            Assert.That(execution.DataSourceLogic, Is.Not.Null);
            Assert.That(execution.StorageLogic, Is.Not.Null);
            Assert.That(typeof(Execution)
                .GetProperty("Type", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(execution), Is.EqualTo(ExecutionType.Run));
            Assert.That(typeof(Execution)
                .GetProperty("Context", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(execution), Is.Not.Null);
        });
    }

    [Test]
    public void Build_WithSessionWithoutConfiguredStage_AssignsIndexAsDefaultStage()
    {
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "session-without-stage",
                Stage = null,
                Probes = []
            })
            .ExecutionType(ExecutionType.Template)
            .SetExecutionId("exec-stage")
            .SetCase("case-stage")
            .WithLogger(Globals.Logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" })
            .WithReportPortal(new ReportPortalConfig { Enabled = false });

        _ = builder.Build();

        Assert.That(builder.ReadSessions(), Has.Count.EqualTo(1));
        Assert.That(builder.ReadSessions()[0].Stage, Is.EqualTo(0));
    }

    [Test]
    public void CrudOperations_WithNullConfiguredArrays_DoNotThrowAndKeepArraysNull()
    {
        var builder = new ExecutionBuilder
        {
            Sessions = null,
            Assertions = null,
            Storages = null,
            DataSources = null,
            Links = null
        };

        Assert.DoesNotThrow(() =>
        {
            builder.UpdateSession("missing", new SessionBuilder());
            builder.DeleteSession("missing");
            builder.UpdateAssertion("missing", new AssertionBuilder
            {
                AssertionInstance = null!,
                Reporter = null!
            });
            builder.DeleteAssertion("missing");
            builder.UpdateStorageAt(0, new StorageBuilder());
            builder.DeleteStorageAt(0);
            builder.UpdateDataSource("missing", new DataSourceBuilder());
            builder.DeleteDataSource("missing");
            builder.UpdateLinkAt(0, new LinkBuilder());
            builder.DeleteLinkAt(0);
        });

        Assert.That(builder.Sessions, Is.Null);
        Assert.That(builder.Assertions, Is.Null);
        Assert.That(builder.Storages, Is.Null);
        Assert.That(builder.DataSources, Is.Null);
        Assert.That(builder.Links, Is.Null);
    }

    [Test]
    public void ReadStorages_WhenStoragesAreNull_ReturnsEmptyCollection()
    {
        var builder = new ExecutionBuilder
        {
            Storages = null
        };

        var storages = builder.ReadStorages();

        Assert.That(storages, Is.Empty);
    }

    [Test]
    public void Start_WithSameProbeNameAcrossDifferentSessions_UsesSessionScopedProbeConfiguration()
    {
        const string sharedProbeName = "shared-probe";
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "session-1",
                Stage = 0,
                Probes =
                [
                    new ProbeBuilder()
                        .Named(sharedProbeName)
                        .HookNamed(nameof(FirstTestProbe))
                        .Configure(new ProbeMarkerConfig { Marker = "first-config" })
                ]
            })
            .AddSession(new SessionBuilder
            {
                Name = "session-2",
                Stage = 1,
                Probes =
                [
                    new ProbeBuilder()
                        .Named(sharedProbeName)
                        .HookNamed(nameof(SecondTestProbe))
                        .Configure(new ProbeMarkerConfig { Marker = "second-config" })
                ]
            })
            .ExecutionType(ExecutionType.Run)
            .SetExecutionId("probe-scope")
            .SetCase("probe-scope-case")
            .WithLogger(Globals.Logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" })
            .WithReportPortal(new ReportPortalConfig { Enabled = false });

        var execution = builder.Build();
        var exitCode = execution.Start();
        var runs = ProbeRunRecorder.GetRuns();

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(runs, Has.Count.EqualTo(2));
        Assert.That(runs, Contains.Item((nameof(FirstTestProbe), "first-config")));
        Assert.That(runs, Contains.Item((nameof(SecondTestProbe), "second-config")));
    }

    [Test]
    public void Build_WithProbeMissingName_ThrowsInvalidConfigurationsException()
    {
        var builder = new ExecutionBuilder()
            .AddSession(new SessionBuilder
            {
                Name = "session-missing-probe-name",
                Stage = 0,
                Probes =
                [
                    new ProbeBuilder()
                        .HookNamed(nameof(FirstTestProbe))
                ]
            })
            .ExecutionType(ExecutionType.Template)
            .SetExecutionId("invalid-probe-name")
            .SetCase("invalid-probe-name-case")
            .WithLogger(Globals.Logger)
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" })
            .WithReportPortal(new ReportPortalConfig { Enabled = false });

        Assert.Throws<InvalidConfigurationsException>(() => builder.Build());
    }

    [Test]
    public void Build_WithLoadedContextWithoutMetadata_DoesNotThrow()
    {
        var context = CreateLoadedContext(new Dictionary<string, string?>
        {
            ["ReportPortal:Enabled"] = "false",
            ["Sessions:0:Name"] = "context-session"
        });
        var builder = new ExecutionBuilder(context, ExecutionType.Run, null, null, null, null);

        Assert.DoesNotThrow(() => builder.Build());
    }

    [Test]
    public void Build_WithLoadedContextWithPartialMetadata_RecordsEachValidationErrorOnce()
    {
        var context = CreateLoadedContext(new Dictionary<string, string?>
        {
            ["MetaData:System"] = "QaaS",
            ["ReportPortal:Enabled"] = "false",
            ["Sessions:0:Name"] = "context-session"
        });
        var builder = new ExecutionBuilder(context, ExecutionType.Run, null, null, null, null);

        Assert.Throws<InvalidConfigurationsException>(() => builder.Build());

        var validationResults = (IReadOnlyList<System.ComponentModel.DataAnnotations.ValidationResult>)typeof(ExecutionBuilder)
            .GetField("_validationResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(builder)!;
        var errorMessages = validationResults
            .Select(result => result.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList();

        Assert.That(errorMessages.Count(message => message == "MetaData - The Team field is required."), Is.EqualTo(1));
    }

    private ExecutionBuilder CreateValidExecutionBuilder()
    {
        var builder = new ExecutionBuilder();

        // Add valid session builder
        var sessionBuilder = new SessionBuilder
        {
            Name = "test-session",
            Stage = 0,
            Probes = []
        };
        builder.AddSession(sessionBuilder);

        // Add valid assertion builder
        var assertionBuilder = new AssertionBuilder
        {
            Name = "test-assertion",
            Assertion = "Equals",
            AssertionInstance = null,
            Reporter = null
        }.HookNamed(nameof(TestAssertion));
        builder.AddAssertion(assertionBuilder);

        // Add valid storage builder
        var storageBuilder = new StorageBuilder().Configure(new S3Config());
        builder.AddStorage(storageBuilder);

        // Add valid link builder
        var linkBuilder = new LinkBuilder()
            { Grafana = new GrafanaLinkConfig { DashboardId = "dash-id", Url = "https://grafa.com", Variables = [] } };
        builder.AddLink(linkBuilder);

        // Add valid data source builder
        var dataSourceBuilder = new DataSourceBuilder().Named("test-datasource").HookNamed("TestGenerator");
        builder.AddDataSource(dataSourceBuilder);

        return builder.SetExecutionId("test").SetCase("valid").WithLogger(Globals.Logger)
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" })
            .WithGlobalDict(new Dictionary<string, object?>())
            .WithReportPortal(new ReportPortalConfig { Enabled = false });
    }

    private ExecutionBuilder CreateInvalidExecutionBuilder()
    {
        var builder = new ExecutionBuilder();

        // Add session builder with duplicate name to make it invalid
        var sessionBuilder1 = new SessionBuilder
        {
            Name = "duplicate-name",
            Stage = 0,
            Probes = []
        };

        var sessionBuilder2 = new SessionBuilder
        {
            Name = "duplicate-name", // Same name as above - will cause validation error
            Stage = 1,
            Probes = []
        };

        builder.AddSession(sessionBuilder1);
        builder.AddSession(sessionBuilder2);

        // Add valid assertion builder
        var assertionBuilder = new AssertionBuilder
        {
            Name = "test-assertion",
            Assertion = "Equals",
            AssertionInstance = null,
            Reporter = null
        }.HookNamed(nameof(TestAssertion));
        builder.AddAssertion(assertionBuilder);

        return builder.ExecutionType(ExecutionType.Run).SetExecutionId("test").SetCase("invalid")
            .WithLogger(Globals.Logger).WithGlobalDict(new Dictionary<string, object?>())
            .WithMetadata(new MetaDataConfig { System = "QaaS", Team = "Smoke" })
            .WithReportPortal(new ReportPortalConfig { Enabled = false });
    }

    private static InternalContext CreateLoadedContext(IConfiguration configuration)
    {
        return new InternalContext
        {
            Logger = Globals.Logger,
            RootConfiguration = configuration,
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>()),
            InternalGlobalDict = new Dictionary<string, object?>()
        };
    }

    private static InternalContext CreateLoadedContext(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return CreateLoadedContext(configuration);
    }
}
