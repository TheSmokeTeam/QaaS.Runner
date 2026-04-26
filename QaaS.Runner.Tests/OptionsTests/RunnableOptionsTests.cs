using NUnit.Framework;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Options;

namespace QaaS.Runner.Tests.OptionsTests;

[TestFixture]
public class RunnableOptionsTests
{
    [TestCase(typeof(ActOptions), ExecutionType.Act)]
    [TestCase(typeof(AssertOptions), ExecutionType.Assert)]
    [TestCase(typeof(RunOptions), ExecutionType.Run)]
    [TestCase(typeof(TemplateOptions), ExecutionType.Template)]
    public void GetExecutionType_ReturnsExpectedExecutionType(Type optionType, ExecutionType expectedExecutionType)
    {
        var option = (BaseOptions)Activator.CreateInstance(optionType)!;

        Assert.That(option.GetExecutionType(), Is.EqualTo(expectedExecutionType));
    }

    [Test]
    public void AssertableOptions_DefaultFlags_AreDisabled()
    {
        var options = new RunOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.AutoServeTestResults, Is.False);
            Assert.That(options.ServeResultsFolder, Is.Null);
            Assert.That(options.EmptyAllureDirectory, Is.False);
            Assert.That(options.GetReporterModeOrDefault(), Is.EqualTo(ReporterMode.Both));
        });
    }

    [Test]
    public void ExecuteOptions_DefaultFlags_AreDisabled()
    {
        var options = new ExecuteOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.AutoServeTestResults, Is.False);
            Assert.That(options.ServeResultsFolder, Is.Null);
            Assert.That(options.EmptyAllureDirectory, Is.False);
            Assert.That(options.NoProcessExit, Is.False);
            Assert.That(options.CommandIdsToRun, Is.Empty);
            Assert.That(options.GetReporterModeOrDefault(), Is.EqualTo(ReporterMode.Both));
        });
    }

    [TestCase("allure", ReporterMode.Allure)]
    [TestCase("reportportal", ReporterMode.ReportPortal)]
    [TestCase("both", ReporterMode.Both)]
    [TestCase("ALLURE", ReporterMode.Allure)]
    [TestCase("ReportPortal", ReporterMode.ReportPortal)]
    [TestCase("BoTh", ReporterMode.Both)]
    public void AssertableOptions_GetReporterModeOrDefault_ParsesSupportedValues(string reporter,
        ReporterMode expectedMode)
    {
        var options = new RunOptions
        {
            Reporter = reporter
        };

        Assert.That(options.GetReporterModeOrDefault(), Is.EqualTo(expectedMode));
    }

    [TestCase("allure", ReporterMode.Allure)]
    [TestCase("reportportal", ReporterMode.ReportPortal)]
    [TestCase("both", ReporterMode.Both)]
    [TestCase("ALLURE", ReporterMode.Allure)]
    [TestCase("ReportPortal", ReporterMode.ReportPortal)]
    [TestCase("BoTh", ReporterMode.Both)]
    public void ExecuteOptions_GetReporterModeOrDefault_ParsesSupportedValues(string reporter,
        ReporterMode expectedMode)
    {
        var options = new ExecuteOptions
        {
            Reporter = reporter
        };

        Assert.That(options.GetReporterModeOrDefault(), Is.EqualTo(expectedMode));
    }

    [TestCase("invalid")]
    [TestCase("allure,reportportal")]
    public void AssertableOptions_GetReporterModeOrDefault_WithInvalidValue_ThrowsArgumentException(string reporter)
    {
        var options = new RunOptions
        {
            Reporter = reporter
        };

        var exception = Assert.Throws<ArgumentException>(() => options.GetReporterModeOrDefault());

        Assert.That(exception!.Message, Does.Contain("Unsupported reporter mode"));
    }

    [TestCase("invalid")]
    [TestCase("allure,reportportal")]
    public void ExecuteOptions_GetReporterModeOrDefault_WithInvalidValue_ThrowsArgumentException(string reporter)
    {
        var options = new ExecuteOptions
        {
            Reporter = reporter
        };

        var exception = Assert.Throws<ArgumentException>(() => options.GetReporterModeOrDefault());

        Assert.That(exception!.Message, Does.Contain("Unsupported reporter mode"));
    }

    [Test]
    public void AssertableOptions_WhenAutoServeIsEnabled_UsesDefaultResultsFolder()
    {
        var options = new RunOptions
        {
            AutoServeTestResults = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(options.AutoServeTestResults, Is.True);
            Assert.That(options.ServeResultsFolder, Is.EqualTo(AssertableOptions.DefaultServeResultsFolder));
            Assert.That(options.GetServeResultsFolderOrDefault(), Is.EqualTo(AssertableOptions.DefaultServeResultsFolder));
        });
    }

    [Test]
    public void ExecuteOptions_WhenCustomServeResultsFolderIsProvided_PreservesIt()
    {
        var options = new ExecuteOptions
        {
            ServeResultsFolder = "allure-report"
        };

        Assert.Multiple(() =>
        {
            Assert.That(options.AutoServeTestResults, Is.True);
            Assert.That(options.GetServeResultsFolderOrDefault(), Is.EqualTo("allure-report"));
        });
    }
}
