using System;
using System.Collections.Generic;
using System.Reflection;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Tests.Actions.Utils;

namespace QaaS.Runner.Sessions.Tests.Actions.Probes;

[TestFixture]
public class ProbeBuilderTests
{
    private Mock<IProbe> _mockProbe = null!;
    private InternalContext _context = null!;
    private const string SessionName = "test session";

    [SetUp]
    public void SetUp()
    {
        _mockProbe = new Mock<IProbe>();
        _context = CreationalFunctions.CreateContext(SessionName, []);
    }

    [Test]
    public void Named_Should_Set_Name()
    {
        var builder = new ProbeBuilder();
        builder.Named("TestProbe");

        Assert.That(builder.Name, Is.EqualTo("TestProbe"));
    }

    [Test]
    public void AtStage_Should_Set_Stage()
    {
        var builder = new ProbeBuilder();
        builder.AtStage(5);

        Assert.That(builder.Stage, Is.EqualTo(5));
    }

    [Test]
    public void HookNamed_Should_Set_Probe()
    {
        var builder = new ProbeBuilder();
        builder.HookNamed("TestHook");

        Assert.That(builder.Probe, Is.EqualTo("TestHook"));
    }

    [Test]
    public void AddDataSourceName_Should_Add_To_Array()
    {
        var builder = new ProbeBuilder();
        builder.AddDataSourceName("DataSource1");

        Assert.That(builder.DataSourceNames, Contains.Item("DataSource1"));
    }

    [Test]
    public void AddDataSourcePattern_Should_Add_To_Array()
    {
        var builder = new ProbeBuilder();
        builder.AddDataSourcePattern(@"^\w+$");

        Assert.That(builder.DataSourcePatterns, Contains.Item(@"^\w+$"));
    }

    [Test]
    public void AddDataSourceFilters_When_Collections_Are_Null_Initializes_Them()
    {
        var builder = new ProbeBuilder();
        typeof(ProbeBuilder).GetProperty("DataSourceNames", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(builder, null);
        typeof(ProbeBuilder).GetProperty("DataSourcePatterns", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(builder, null);

        builder.AddDataSourceName("DataSource1")
            .AddDataSourcePattern(@"^\w+$");

        Assert.That(builder.DataSourceNames, Contains.Item("DataSource1"));
        Assert.That(builder.DataSourcePatterns, Contains.Item(@"^\w+$"));
    }

    [Test]
    public void Configure_Should_Set_Configuration()
    {
        var config = new { Key1 = "Value1", Key2 = 42 };
        var builder = new ProbeBuilder();

        builder.Configure(config);

        Assert.That(builder.ProbeConfiguration["Key1"], Is.EqualTo("Value1"));
        Assert.That(builder.ProbeConfiguration["Key2"], Is.EqualTo("42"));
    }

    [Test]
    public void Build_Successful_Should_Return_Probe()
    {
        var probes = new List<KeyValuePair<string, IProbe>>
        {
            new(ProbeBuilder.BuildScopedHookName("Session1", "TestProbe"), _mockProbe.Object)
        };

        var actionFailures = new List<ActionFailure>();
        var builder = new ProbeBuilder()
            .Named("TestProbe")
            .HookNamed("TestHook");

        var probe = builder.Build(_context, probes, actionFailures, "Session1");

        Assert.That(probe, Is.Not.Null);
    }

    [Test]
    public void Build_When_Probe_Not_Found_Should_Add_Failure_And_Return_Null()
    {
        var probes = new List<KeyValuePair<string, IProbe>>();
        var actionFailures = new List<ActionFailure>();

        var builder = new ProbeBuilder()
            .Named("NonExistentProbe")
            .HookNamed("SomeHook");

        var probe = builder.Build(_context, probes, actionFailures, "Session1");

        Assert.That(probe, Is.Null);
        Assert.That(actionFailures.Count, Is.EqualTo(1));
    }

    [Test]
    public void Build_WhenOnlyUnscopedProbeExists_Should_Add_Failure_And_Return_Null()
    {
        var probes = new List<KeyValuePair<string, IProbe>>
        {
            new("TestProbe", _mockProbe.Object)
        };
        var actionFailures = new List<ActionFailure>();
        var builder = new ProbeBuilder()
            .Named("TestProbe")
            .HookNamed("SomeHook");

        var probe = builder.Build(_context, probes, actionFailures, "Session1");

        Assert.That(probe, Is.Null);
        Assert.That(actionFailures.Count, Is.EqualTo(1));
    }

    [Test]
    public void Build_When_Name_Is_Missing_Should_Add_Failure_And_Return_Null()
    {
        var probes = new List<KeyValuePair<string, IProbe>>();
        var actionFailures = new List<ActionFailure>();
        var builder = new ProbeBuilder()
            .HookNamed("SomeHook");

        var probe = builder.Build(_context, probes, actionFailures, "Session1");

        Assert.That(probe, Is.Null);
        Assert.That(actionFailures, Has.Count.EqualTo(1));
        Assert.That(actionFailures[0].Reason.Message, Does.Contain("Probe Name is required"));
    }

    [Test]
    public void Read_Throws_NotSupportedException()
    {
        var builder = new ProbeBuilder();
        Assert.Throws<NotSupportedException>(() =>
            builder.Read(null!, typeof(ProbeBuilder), null!)
        );
    }
}
