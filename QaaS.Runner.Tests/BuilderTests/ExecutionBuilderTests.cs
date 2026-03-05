using System.Reflection;
using NUnit.Framework;
using QaaS.Framework.Configurations.CustomExceptions;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Sessions.Session.Builders;
using QaaS.Runner.Storage;
using QaaS.Runner.Storage.ConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs;
using QaaS.Runner.Tests.TestObjects;

namespace QaaS.Runner.Tests.BuilderTests;

public class ExecutionBuilderTests
{
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
            .WithMetadata(new MetaDataConfig { Team = "Smoke", System = "QaaS" });

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
            .WithGlobalDict(new Dictionary<string, object?>());
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
            .WithMetadata(new MetaDataConfig { System = "QaaS", Team = "Smoke" });
    }
}
