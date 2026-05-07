using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Allure.Commons;
using NUnit.Framework;
using QaaS.Runner.WrappedExternals;

namespace QaaS.Runner.Tests.WrappedExternalTests;

[TestFixture]
public class AllureWrapperTests
{
    private TestableAllureWrapper _wrapper;
    private string _originalCurrentDirectory;
    private string _workingDirectory;

    private sealed class TestableAllureWrapper : AllureWrapper
    {
        public List<ProcessStartInfo> StartInfos { get; } = [];
        public string GeneratedReportDirectory { get; set; } = string.Empty;

        protected override string ResolveReportDirectory()
        {
            return GeneratedReportDirectory;
        }

        protected override void RunProcess(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            if (startInfo.Arguments.Contains(" generate", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.Combine(GeneratedReportDirectory, "history"));
                File.WriteAllText(Path.Combine(GeneratedReportDirectory, "history", "history-trend.json"),
                    "history-content");
            }
        }
    }

    [SetUp]
    public void SetUp()
    {
        _originalCurrentDirectory = Directory.GetCurrentDirectory();
        _workingDirectory = Path.Combine(Path.GetTempPath(), $"allure-wrapper-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workingDirectory);
        Directory.SetCurrentDirectory(_workingDirectory);
        _wrapper = new TestableAllureWrapper
        {
            GeneratedReportDirectory = Path.Combine(_workingDirectory, "allure-report")
        };
    }

    [TearDown]
    public void TearDown()
    {
        Directory.SetCurrentDirectory(_originalCurrentDirectory);
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, true);
    }

    [Test]
    public void TestCleanTestResultsDirectory_ShouldExecuteWithoutException()
    {
        Assert.DoesNotThrow(() => _wrapper.CleanTestResultsDirectory());
    }

    [Test]
    public void TestCleanTestResultsDirectory_PreservesHistoryDirectory()
    {
        var resultsDirectory = Path.GetFullPath(AllureLifecycle.Instance.ResultsDirectory, _workingDirectory);
        var historyDirectory = Path.Combine(resultsDirectory, "history");
        Directory.CreateDirectory(historyDirectory);
        Directory.CreateDirectory(Path.Combine(resultsDirectory, "SessionLogs"));
        File.WriteAllText(Path.Combine(historyDirectory, "history-trend.json"), "history-content");
        File.WriteAllText(Path.Combine(resultsDirectory, "marker.txt"), "marker");

        _wrapper.CleanTestResultsDirectory();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(historyDirectory, "history-trend.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(resultsDirectory, "marker.txt")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(resultsDirectory, "SessionLogs")), Is.False);
        });
    }

    [Test]
    [TestCase("unknown-path", "unknown-path", TestName = "allure path wrong")]
    [TestCase("allure", "allure", TestName = "allure path right")]
    [TestCase("", "allure", TestName = "default allure path right")]
    public void TestServeTestResults_WithVariousPaths_ShouldExecuteWithoutException(string path,
        string expectedRunnablePath)
    {
        Assert.DoesNotThrow(() => _wrapper.ServeTestResults(path));

        Assert.Multiple(() =>
        {
            Assert.That(_wrapper.StartInfos, Has.Count.EqualTo(2));
            Assert.That(_wrapper.StartInfos[0].Arguments, Does.Contain($"{expectedRunnablePath} generate"));
            Assert.That(_wrapper.StartInfos[0].Arguments, Does.Contain("-o"));
            Assert.That(_wrapper.StartInfos[1].Arguments, Does.Contain($"{expectedRunnablePath} open"));
            Assert.That(_wrapper.StartInfos[1].Arguments, Does.Contain(_wrapper.GeneratedReportDirectory));
        });
    }

    [Test]
    public void TestServeTestResults_WithEchoCommand_ShouldReadStandardOutputWithoutException()
    {
        Assert.DoesNotThrow(() => _wrapper.ServeTestResults("echo"));

        Assert.Multiple(() =>
        {
            Assert.That(_wrapper.StartInfos, Has.Count.EqualTo(2));
            Assert.That(_wrapper.StartInfos[0].Arguments, Does.Contain("echo generate"));
            Assert.That(_wrapper.StartInfos[1].Arguments, Does.Contain("echo open"));
        });
    }

    [Test]
    public void TestServeTestResults_WithExistingHistory_CopiesHistoryIntoResultsDirectory()
    {
        _wrapper.ServeTestResults();

        var resultsHistoryFile = Path.Combine(Path.GetFullPath(AllureLifecycle.Instance.ResultsDirectory, _workingDirectory),
            "history", "history-trend.json");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(resultsHistoryFile), Is.True);
            Assert.That(File.ReadAllText(resultsHistoryFile), Is.EqualTo("history-content"));
            Assert.That(_wrapper.StartInfos, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void TestServeTestResults_WithCustomFolder_GeneratesIntoRequestedDirectoryWhenMissing()
    {
        _wrapper.GeneratedReportDirectory = Path.Combine(_workingDirectory, "custom-report");

        _wrapper.ServeTestResults(resultsDirectoryName: "custom-report");

        Assert.Multiple(() =>
        {
            Assert.That(_wrapper.StartInfos, Has.Count.EqualTo(2));
            Assert.That(_wrapper.StartInfos[0].Arguments, Does.Contain(" generate "));
            Assert.That(_wrapper.StartInfos[0].Arguments, Does.Contain("-o"));
            Assert.That(_wrapper.StartInfos[0].Arguments, Does.Contain("custom-report"));
            Assert.That(_wrapper.StartInfos[1].Arguments, Does.Contain("open "));
            Assert.That(_wrapper.StartInfos[1].Arguments, Does.Contain("custom-report"));
        });
    }

    [Test]
    public void TestServeTestResults_WithExistingCustomFolder_OpensWithoutGenerating()
    {
        var existingReportDirectory = Path.Combine(_workingDirectory, "existing-report");
        Directory.CreateDirectory(existingReportDirectory);

        _wrapper.ServeTestResults(resultsDirectoryName: "existing-report");

        Assert.Multiple(() =>
        {
            Assert.That(_wrapper.StartInfos, Has.Count.EqualTo(1));
            Assert.That(_wrapper.StartInfos[0].Arguments, Does.Not.Contain(" generate"));
            Assert.That(_wrapper.StartInfos[0].Arguments, Does.Contain("open "));
            Assert.That(_wrapper.StartInfos[0].Arguments, Does.Contain("existing-report"));
        });
    }

    [Test]
    public void TestMethodExistence_ShouldHaveExpectedMethods()
    {
        var cleanMethod = typeof(AllureWrapper).GetMethod("CleanTestResultsDirectory",
            BindingFlags.Public | BindingFlags.Instance);
        var serveMethod =
            typeof(AllureWrapper).GetMethod("ServeTestResults", BindingFlags.Public | BindingFlags.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(cleanMethod, Is.Not.Null);
            Assert.That(cleanMethod!.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(serveMethod, Is.Not.Null);
            Assert.That(serveMethod!.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(serveMethod!.GetParameters(), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void TestConstructor_ShouldInitializeSuccessfully()
    {
        var wrapper = new AllureWrapper();

        Assert.That(wrapper, Is.Not.Null);
    }
}
