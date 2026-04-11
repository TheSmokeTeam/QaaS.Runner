using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.ConfigurationObjects.Grpc;
using QaaS.Framework.Protocols.ConfigurationObjects.Http;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace QaaS.Runner.Sessions.Tests.Actions.Transactions;

public class TransactionBuilderTests
{
    private InternalContext _context = null!;
    private IList<ActionFailure> _actionFailures = null!;
    private string _sessionName = null!;

    [SetUp]
    public void SetUp()
    {
        _actionFailures = new List<ActionFailure>();
        _sessionName = "TestSession";

        _context = new InternalContext
        {
            InternalRunningSessions = new RunningSessions(
                new Dictionary<string, RunningSessionData<object, object>>
                {
                    {
                        _sessionName, new RunningSessionData<object, object>
                        {
                            Inputs = [],
                            Outputs = []
                        }
                    }
                }),
            Logger = new SerilogLoggerFactory(new LoggerConfiguration().MinimumLevel
                .Is(LogEventLevel.Information).WriteTo
                .Console().CreateLogger()).CreateLogger("DefaultLogger")
        };
    }

    [Test]
    public void Named_Should_Set_Name()
    {
        var builder = new TransactionBuilder();
        builder.Named("TestName");
        builder.AddDataSource("Source1");

        Assert.That(builder.Name, Is.EqualTo("TestName"));
    }

    [Test]
    public void AtStage_Should_Set_Stage()
    {
        var builder = new TransactionBuilder();
        builder.AtStage(5);
        builder.AddDataSource("Source1");
        builder.Named("test");

        Assert.That(builder.Stage, Is.EqualTo(5));
    }

    [Test]
    public void WithTimeout_Should_Set_TimeoutMs()
    {
        var builder = new TransactionBuilder();
        builder.WithTimeout(3000);
        builder.AddDataSource("Source1");
        builder.Named("test");

        Assert.That(builder.TimeoutMs, Is.EqualTo(3000));
    }

    [Test]
    public void FilterInputData_Should_Set_InputDataFilter()
    {
        var filter = new DataFilter();
        var builder = new TransactionBuilder();
        builder.FilterInputData(filter);
        builder.AddDataSource("Source1");
        builder.Named("test");

        Assert.That(builder.InputDataFilter, Is.SameAs(filter));
    }

    [Test]
    public void FilterOutputData_Should_Set_OutputDataFilter()
    {
        var filter = new DataFilter();
        var builder = new TransactionBuilder();
        builder.FilterOutputData(filter);
        builder.AddDataSource("Source1");
        builder.Named("test");

        Assert.That(builder.OutputDataFilter, Is.SameAs(filter));
    }

    [Test]
    public void WithDeserializer_Should_Set_OutputDeserialize()
    {
        var config = new DeserializeConfig();
        var builder = new TransactionBuilder();
        builder.WithDeserializer(config);
        builder.AddDataSource("Source1");
        builder.Named("test");

        Assert.That(builder.OutputDeserialize, Is.SameAs(config));
    }

    [Test]
    public void WithSerializer_Should_Set_InputSerialize()
    {
        var config = new SerializeConfig();
        var builder = new TransactionBuilder();
        builder.WithSerializer(config);
        builder.AddDataSource("Source1");
        builder.Named("test");

        Assert.That(builder.InputSerialize, Is.SameAs(config));
    }

    [Test]
    public void WithIterations_Should_Set_Iterations()
    {
        var builder = new TransactionBuilder();
        builder.WithIterations(10);
        builder.AddDataSource("Source1");
        builder.Named("test");

        Assert.That(builder.Iterations, Is.EqualTo(10));
    }

    [Test]
    public void AddDataSource_Should_Add_To_DataSourceNames()
    {
        var builder = new TransactionBuilder();
        builder.AddDataSource("Source1");
        builder.AddDataSource("Source2");
        builder.Named("test");

        Assert.That(builder.DataSourceNames, Is.EqualTo(new[] { "Source1", "Source2" }));
    }

    [Test]
    public void AddDataSourcePattern_Should_Add_To_DataSourceNamePatterns()
    {
        var builder = new TransactionBuilder();
        builder.AddDataSourcePattern("Pattern1");
        builder.AddDataSourcePattern("Pattern2");
        builder.Named("test");

        Assert.That(builder.DataSourcePatterns, Is.EqualTo(new[] { "Pattern1", "Pattern2" }));
    }

    [Test]
    public void InLoops_Should_Set_Loop_True()
    {
        var builder = new TransactionBuilder();
        builder.InLoops();
        builder.Named("test");
        builder.AddDataSource("test");

        Assert.That(builder.Loop, Is.True);
    }

    [Test]
    public void WithSleep_Should_Set_SleepTimeMs()
    {
        var builder = new TransactionBuilder();
        builder.WithSleep(1000);
        builder.Named("test");
        builder.AddDataSource("test");

        Assert.That(builder.SleepTimeMs, Is.EqualTo(1000UL));
    }

    [Test]
    public void Configure_With_HttpTransactorConfig_Should_Set_Http()
    {
        var httpConfig = new HttpTransactorConfig();
        var builder = new TransactionBuilder();
        builder.Configure(httpConfig);
        builder.Named("test");
        builder.AddDataSource("test");

        Assert.That(builder.Http, Is.SameAs(httpConfig));
        Assert.That(builder.Grpc, Is.Null);
    }

    [Test]
    public void Configure_With_GrpcTransactorConfig_Should_Set_Grpc()
    {
        var grpcConfig = new GrpcTransactorConfig();
        var builder = new TransactionBuilder();
        builder.Configure(grpcConfig);
        builder.Named("test");
        builder.AddDataSource("test");

        Assert.That(builder.Grpc, Is.SameAs(grpcConfig));
        Assert.That(builder.Http, Is.Null);
    }

    [Test]
    public void UpdateConfiguration_WithConfigurationWithoutExistingConfiguration_ConfiguresIncomingType()
    {
        var builder = new TransactionBuilder();
        var config = new HttpTransactorConfig();

        builder.UpdateConfiguration(config);

        Assert.That(builder.Configuration, Is.SameAs(config));
    }

    [Test]
    public void Build_Throws_When_No_Transactor_Config()
    {
        var builder = new TransactionBuilder();
        builder.Named("Test");
        builder.AddDataSource("test");
        builder.WithTimeout(1000);

        var result = builder.Build(_context, _actionFailures, _sessionName);

        Assert.That(result, Is.Null);
        Assert.That(_sessionName, Is.Not.Empty);
    }

    [Test]
    public void Build_Throws_When_Multiple_Transactor_Configs()
    {
        var builder = new TransactionBuilder();
        builder.Named("Test");
        builder.AddDataSourcePattern("test");
        builder.WithTimeout(1000);
        builder.Configure(new HttpTransactorConfig());
        builder.Configure(new GrpcTransactorConfig()); // This overrides previous

        var result = builder.Build(_context, _actionFailures, _sessionName);

        Assert.That(result, Is.Null);
        Assert.That(_sessionName, Is.Not.Empty);
    }

    [Test]
    public void Build_Succeeds_With_Http_Config()
    {
        var builder = new TransactionBuilder();
        builder.Named("Test");
        builder.WithTimeout(1000);
        builder.AddDataSourcePattern("test");
        builder.Configure(new HttpTransactorConfig
        {
            Method = HttpMethods.Delete,
            BaseAddress = "https://test.com"
        });

        var result = builder.Build(_context, _actionFailures, _sessionName);

        Assert.That(result, Is.Not.Null);
        Assert.That(_actionFailures, Is.Empty);
    }

    [Test]
    public void TestRequiredName_ValidateTransactionBuilder_ShouldNotBuildAndHaveFailedValidationResults()
    {
        var builder = new TransactionBuilder();

        // Should fail because Name is required but not set
        var result = builder.Build(_context, _actionFailures, _sessionName);

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures, Is.Not.Empty);
    }

    [Test]
    public void TestRequiredIfAnyDataSourceNamePatterns_ValidateBuilder_ShouldHaveFailedValidationResults()
    {
        var builder = new TransactionBuilder();
        builder.Named("Test").WithTimeout(1000).Configure(new HttpTransactorConfig
        {
            Method = HttpMethods.Delete,
            BaseAddress = "https://test.com"
        });

        var validationResults = new List<ValidationResult>();
        ValidateMembers(builder, validationResults,
            "DataSourceNames", "DataSourcePatterns");

        Assert.That(validationResults, Is.Not.Empty);
    }

    [Test]
    public void Default_Values_Are_Set()
    {
        var builder = new TransactionBuilder();

        Assert.That(builder.Iterations, Is.EqualTo(1));
        Assert.That(builder.Loop, Is.False);
        Assert.That(builder.SleepTimeMs, Is.EqualTo(0UL));
        Assert.That(builder.Stage, Is.EqualTo(2));
        Assert.That(builder.InputDataFilter, Is.Not.Null);
        Assert.That(builder.OutputDataFilter, Is.Not.Null);
        Assert.That(builder.Policies, Is.Empty);
        Assert.That(builder.InputSerialize, Is.Null);
        Assert.That(builder.OutputDeserialize, Is.Null);
        Assert.That(builder.Http, Is.Null);
        Assert.That(builder.Grpc, Is.Null);
    }

    [Test]
    public void BuildWithMissingPropertiesException_FormatsMessageWithMissingPropertyName()
    {
        var exception = new BuildWithMissingPropertiesException("transactor");

        Assert.That(exception.Message, Does.Contain("no transactor set"));
    }

    [Test]
    public void UpdatePolicyAt_WithInvalidIndex_ThrowsArgumentOutOfRangeException()
    {
        var builder = new TransactionBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UpdatePolicyAt(0, new PolicyBuilder()));
    }

    [Test]
    public void RemovePolicyAt_WithInvalidIndex_ThrowsArgumentOutOfRangeException()
    {
        var builder = new TransactionBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.RemovePolicyAt(0));
    }

    [Test]
    public void UpdateDataSource_WhenCollectionIsNull_DoesNothing()
    {
        var builder = new TransactionBuilder();

        Assert.DoesNotThrow(() => builder.UpdateDataSource("a", "b"));
        Assert.That(builder.DataSourceNames, Is.Null);
    }

    [Test]
    public void UpdateDataSourcePattern_WhenCollectionIsNull_DoesNothing()
    {
        var builder = new TransactionBuilder();

        Assert.DoesNotThrow(() => builder.UpdateDataSourcePattern("a", "b"));
        Assert.That(builder.DataSourcePatterns, Is.Null);
    }

    [Test]
    public void Build_WithMultipleTransactorConfigsConfigured_ReturnsNullAndAddsActionFailure()
    {
        var builder = new TransactionBuilder()
            .Named("transaction-conflict")
            .WithTimeout(1000)
            .AddDataSource("source-a");

        typeof(TransactionBuilder).GetProperty("Http", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(builder, new HttpTransactorConfig { Method = HttpMethods.Get, BaseAddress = "https://test.com" });
        typeof(TransactionBuilder).GetProperty("Grpc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(builder, new GrpcTransactorConfig());

        var result = builder.Build(_context, _actionFailures, _sessionName);

        Assert.That(result, Is.Null);
        Assert.That(_actionFailures, Is.Not.Empty);
    }

    private static void ValidateMembers(object instance, ICollection<ValidationResult> validationResults,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames.Distinct(StringComparer.Ordinal))
        {
            var property = instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var validationContext = new ValidationContext(instance, null, null)
            {
                MemberName = property.Name
            };
            var getter = property.GetGetMethod(nonPublic: true);
            var value = getter?.Invoke(instance, null);
            foreach (var validationAttribute in property.GetCustomAttributes<ValidationAttribute>())
            {
                var result = validationAttribute.GetValidationResult(value, validationContext);
                if (result != null && result != ValidationResult.Success)
                {
                    validationResults.Add(result);
                }
            }
        }
    }
}


