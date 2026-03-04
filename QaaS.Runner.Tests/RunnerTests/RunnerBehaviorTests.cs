using System.Reflection;
using Autofac;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.WrappedExternals;
using Allure.Commons;

namespace QaaS.Runner.Tests.RunnerTests;

[TestFixture]
public class RunnerBehaviorTests
{
    private sealed class ExposedRunner(
        ILifetimeScope scope,
        List<ExecutionBuilder> executionBuilders,
        Microsoft.Extensions.Logging.ILogger logger,
        Serilog.ILogger serilogLogger,
        bool emptyResults = false,
        bool serveResults = false) : Runner(scope, executionBuilders, logger, serilogLogger, emptyResults, serveResults)
    {
        public void InvokeSetup() => base.Setup();
        public void InvokeTeardown() => base.Teardown();
        public List<Execution> InvokeBuildExecutions() => base.BuildExecutions();
        public int InvokeStartExecutions(List<Execution> executions) => base.StartExecutions(executions);
    }

    [Test]
    public void Setup_WithEmptyResultsEnabled_CleansAllureResultsDirectory()
    {
        var markerFile = CreateAllureMarkerFile();
        using var scope = BuildScope();
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object, emptyResults: true);

        runner.InvokeSetup();

        Assert.That(File.Exists(markerFile), Is.False);
    }

    [Test]
    public void Setup_WithEmptyResultsDisabled_DoesNotCleanAllureResultsDirectory()
    {
        var markerFile = CreateAllureMarkerFile();
        using var scope = BuildScope();
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object, emptyResults: false);

        runner.InvokeSetup();

        Assert.That(File.Exists(markerFile), Is.True);
        File.Delete(markerFile);
    }

    [Test]
    public void Teardown_DisposesSerilogLoggerWhenDisposable()
    {
        using var scope = BuildScope();
        var serilogLogger = new Mock<Serilog.ILogger>();
        var disposableLogger = serilogLogger.As<IDisposable>();
        var runner = new ExposedRunner(scope, [], Globals.Logger, serilogLogger.Object);

        runner.InvokeTeardown();

        disposableLogger.Verify(logger => logger.Dispose(), Times.Once);
    }

    [Test]
    public void StartExecutions_ReturnsSumOfExecutionExitCodes()
    {
        using var scope = BuildScope();
        var runner = new ExposedRunner(scope, [], Globals.Logger, new Mock<Serilog.ILogger>().Object);
        var context = CreateContext();

        var firstExecution = new Mock<Execution>(ExecutionType.Run, context);
        firstExecution.Setup(execution => execution.Start()).Returns(1);

        var secondExecution = new Mock<Execution>(ExecutionType.Run, context);
        secondExecution.Setup(execution => execution.Start()).Returns(2);

        var result = runner.InvokeStartExecutions([firstExecution.Object, secondExecution.Object]);

        Assert.That(result, Is.EqualTo(3));
    }

    [Test]
    public void BuildExecutions_WithTemplateBuilders_BuildsExpectedNumberOfExecutions()
    {
        using var scope = BuildScope();
        var builders = new List<ExecutionBuilder>
        {
            CreateTemplateExecutionBuilder("case-a"),
            CreateTemplateExecutionBuilder("case-b")
        };

        var runner = new ExposedRunner(scope, builders, Globals.Logger, new Mock<Serilog.ILogger>().Object);

        var executions = runner.InvokeBuildExecutions();

        Assert.That(executions, Has.Count.EqualTo(2));

        var executionTypeProperty = typeof(Execution).GetProperty("Type", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(executions.Select(execution => executionTypeProperty!.GetValue(execution)),
            Is.All.EqualTo(ExecutionType.Template));
    }

    private static ILifetimeScope BuildScope()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<AllureWrapper>().SingleInstance();
        return builder.Build().BeginLifetimeScope();
    }

    private static string CreateAllureMarkerFile()
    {
        var resultsDirectory = AllureLifecycle.Instance.ResultsDirectory;
        Directory.CreateDirectory(resultsDirectory);
        var markerFile = Path.Combine(resultsDirectory, $"marker-{Guid.NewGuid():N}.txt");
        File.WriteAllText(markerFile, "marker");
        return markerFile;
    }

    private static ExecutionBuilder CreateTemplateExecutionBuilder(string caseName)
    {
        return new ExecutionBuilder()
            .ExecutionType(ExecutionType.Template)
            .SetExecutionId($"exec-{caseName}")
            .SetCase(caseName)
            .WithMetadata(new MetaDataConfig
            {
                Team = "Smoke",
                System = "QaaS"
            });
    }

    private static InternalContext CreateContext()
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            CaseName = "case",
            ExecutionId = "id",
            RootConfiguration = new ConfigurationBuilder().Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };
        context.InsertValueIntoGlobalDictionary([nameof(MetaDataConfig)], new MetaDataConfig
        {
            Team = "Smoke",
            System = "QaaS"
        });
        return context;
    }
}
