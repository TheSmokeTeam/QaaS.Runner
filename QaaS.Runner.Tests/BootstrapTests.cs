namespace QaaS.Runner.Tests;

using System.Collections.Generic;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using QaaS.Runner.Options;
using QaaS.Runner.WrappedExternals;
using Serilog;

[TestFixture]
public class BootstrapTests
{
    [TestCaseSource(nameof(GetRunnerTestCases))]
    public void TestGetRunner_CallsCorrectLoaderAndReturnsRunner(
        IEnumerable<string> args,
        string expectedRunnerType)
    {
        Runner result;

        result = Bootstrap.New(args);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<Runner>());
    }

    [TestCaseSource(nameof(GetNoProcessExitRunnerTestCases))]
    public void TestGetRunner_WithNoProcessExitFlag_DisablesProcessExit(string[] args)
    {
        var result = Bootstrap.New(args);

        Assert.That(result.ExitProcessOnCompletion, Is.False);
    }

    [Test]
    public void CreateRunnerScope_RegistersAllureWrapper()
    {
        using var scope = Bootstrap.CreateRunnerScope();

        Assert.That(scope.Resolve<AllureWrapper>(), Is.Not.Null);
    }

    [Test]
    public void CanUseFrameworkDefaultLoggers_ReturnsTrue_WhenAccessorsSucceed()
    {
        var canUseDefaultLoggers = Bootstrap.CanUseFrameworkDefaultLoggers(
            () => LoggerFactory.Create(_ => { }).CreateLogger("test"),
            () => new LoggerConfiguration().CreateLogger());

        Assert.That(canUseDefaultLoggers, Is.True);
    }

    [Test]
    public void CanUseFrameworkDefaultLoggers_ReturnsFalse_WhenLoggerAccessorThrowsTypeInitializationException()
    {
        var canUseDefaultLoggers = Bootstrap.CanUseFrameworkDefaultLoggers(
            () => throw new TypeInitializationException("BrokenLogger", new InvalidOperationException("boom")),
            () => new LoggerConfiguration().CreateLogger());

        Assert.That(canUseDefaultLoggers, Is.False);
    }

    [Test]
    public void CanUseFrameworkDefaultLoggers_ReturnsFalse_WhenSerilogAccessorThrowsUriFormatException()
    {
        var canUseDefaultLoggers = Bootstrap.CanUseFrameworkDefaultLoggers(
            () => LoggerFactory.Create(_ => { }).CreateLogger("test"),
            () => throw new UriFormatException("bad uri"));

        Assert.That(canUseDefaultLoggers, Is.False);
    }

    [Test]
    public void GetDefaultLoggers_WhenFrameworkDefaultsAreAvailable_ReusesFrameworkInstances()
    {
        var (logger, serilogLogger, ownsSerilogLogger) = Bootstrap.GetDefaultLoggers(false);

        Assert.Multiple(() =>
        {
            Assert.That(logger, Is.SameAs(QaaS.Framework.Executions.ExecutionLogging.DefaultLogger));
            Assert.That(serilogLogger, Is.SameAs(QaaS.Framework.Executions.ExecutionLogging.DefaultSerilogLogger));
            Assert.That(ownsSerilogLogger, Is.False);
        });
    }

    [Test]
    public void GetDefaultLoggers_WhenFrameworkDefaultsAreDisabled_CreatesOwnedFallbackLogger()
    {
        var (logger, serilogLogger, ownsSerilogLogger) = Bootstrap.GetDefaultLoggers(true);

        Assert.Multiple(() =>
        {
            Assert.That(logger, Is.Not.Null);
            Assert.That(serilogLogger, Is.Not.Null);
            Assert.That(ownsSerilogLogger, Is.True);
            Assert.That(serilogLogger, Is.AssignableTo<IDisposable>());
        });
    }

    [Test]
    public void GetSafeLoggerOptions_ReturnsOriginalOptions_WhenSendLogsMayRemainEnabled()
    {
        var options = new RunOptions { SendLogs = true };

        var safeOptions = Bootstrap.GetSafeLoggerOptions(options, false, currentOptions =>
            currentOptions with { SendLogs = false });

        Assert.That(safeOptions.SendLogs, Is.True);
    }

    [Test]
    public void GetSafeLoggerOptions_DisablesSendLogs_WhenForced()
    {
        var options = new RunOptions { SendLogs = true };

        var safeOptions = Bootstrap.GetSafeLoggerOptions(options, true, currentOptions =>
            currentOptions with { SendLogs = false });

        Assert.That(safeOptions.SendLogs, Is.False);
    }

    [Test]
    public void New_WithNullArgs_WritesHelpWithNoArgsGuidance()
    {
        var output = CaptureConsoleOut(out var exitCode, () =>
        {
            var runner = Bootstrap.New(null);
            Assert.That(runner.RunAndGetExitCode(), Is.EqualTo(0));
        });

        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("Usage:"));
            Assert.That(output, Does.Contain("Empty arguments only work for code-only hosts"));
            Assert.That(output, Does.Contain("dotnet run -- run <config-file>"));
            Assert.That(exitCode, Is.EqualTo(0));
        });
    }

    [Test]
    public void NormalizeArguments_WhenNoArgsAndDefaultConfigurationExists_PreservesEmptyArgs()
    {
        var normalizedArguments = Bootstrap.NormalizeArguments(
            [],
            @"C:\temp",
            _ => true);

        Assert.That(normalizedArguments, Is.Empty);
    }

    [Test]
    public void NormalizeArguments_WhenConfigurationPathIsPassedWithoutVerb_AssumesRunMode()
    {
        var normalizedArguments = Bootstrap.NormalizeArguments(["test.qaas.yaml"]);

        Assert.That(normalizedArguments, Is.EqualTo(new[] { "run", "test.qaas.yaml" }));
    }

    private static string CaptureConsoleOut(out int exitCode, Action action)
    {
        var originalOut = Console.Out;
        var originalExitCode = Environment.ExitCode;
        var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            action();
            exitCode = Environment.ExitCode;
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.ExitCode = originalExitCode;
        }
    }

    private static IEnumerable<TestCaseData> GetRunnerTestCases()
    {
        yield return new TestCaseData(
            new[] { "run", "TestData/test.qaas.yaml", "-w", "TestData/override.yaml", "--send-logs", "true" },
            "Runner"
        ).SetName("WithRunOptions");

        yield return new TestCaseData(
            new[] { "act", "TestData/test.qaas.yaml", "-w", "TestData/override.yaml", "--send-logs", "false" },
            "Runner"
        ).SetName("WithActOptions");

        yield return new TestCaseData(
            new[] { "assert", "TestData/test.qaas.yaml", "-w", "TestData/override.yaml", "--send-logs", "false" },
            "Runner"
        ).SetName("WithAssertOptions");

        yield return new TestCaseData(
            new[] { "template", "TestData/test.qaas.yaml", "-w", "TestData/override.yaml", "--send-logs", "false" },
            "Runner"
        ).SetName("WithTemplateOptions");

        yield return new TestCaseData(
            new[] { "execute", "TestData/executable.yaml", "--send-logs", "true" },
            "Runner"
        ).SetName("WithExecuteOptions");

        yield return new TestCaseData(
            new[] { "--help" },
            "Runner"
        ).SetName("WithHelpFlag");

        yield return new TestCaseData(
            new[] { "--version" },
            "Runner"
        ).SetName("WithVersionFlag");

        yield return new TestCaseData(
            new[] { "invalid-command" },
            "Runner"
        ).SetName("WithInvalidCommand");
    }

    private static IEnumerable<TestCaseData> GetNoProcessExitRunnerTestCases()
    {
        yield return new TestCaseData(new object[] { new[] { "run", "TestData/test.qaas.yaml", "--no-process-exit" } })
            .SetName("WithRunNoProcessExitFlag");

        yield return new TestCaseData(new object[] { new[] { "execute", "TestData/executable.yaml", "--no-process-exit" } })
            .SetName("WithExecuteNoProcessExitFlag");
    }
}
