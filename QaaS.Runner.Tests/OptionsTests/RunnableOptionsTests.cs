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
            Assert.That(options.EmptyAllureDirectory, Is.False);
        });
    }

    [Test]
    public void ExecuteOptions_DefaultFlags_AreDisabled()
    {
        var options = new ExecuteOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.AutoServeTestResults, Is.False);
            Assert.That(options.EmptyAllureDirectory, Is.False);
            Assert.That(options.NoProcessExit, Is.False);
            Assert.That(options.CommandIdsToRun, Is.Empty);
        });
    }
}
