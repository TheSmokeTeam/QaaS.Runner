using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using QaaS.Runner.WrappedExternals;

namespace QaaS.Runner.Tests.WrappedExternalTests;

[TestFixture]
public class AllureWrapperTests
{
    private TestableAllureWrapper _wrapper;

    private sealed class TestableAllureWrapper : AllureWrapper
    {
        public ProcessStartInfo? LastStartInfo { get; private set; }

        protected override Process StartProcess(ProcessStartInfo startInfo)
        {
            LastStartInfo = startInfo;
            return Process.Start(new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = "/c echo allure-serve-test",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
        }
    }

    [SetUp]
    public void SetUp()
    {
        _wrapper = new TestableAllureWrapper();
    }

    [Test]
    public void TestCleanTestResultsDirectory_ShouldExecuteWithoutException()
    {
        Assert.DoesNotThrow(() => _wrapper.CleanTestResultsDirectory());
    }

    [Test]
    [TestCase("unknown-path", "unknown-path serve", TestName = "allure path wrong")]
    [TestCase("allure", "allure serve", TestName = "allure path right")]
    [TestCase("", "allure serve", TestName = "default allure path right")]
    public void TestServeTestResults_WithVariousPaths_ShouldExecuteWithoutException(string path,
        string expectedCommandSegment)
    {
        Assert.DoesNotThrow(() => _wrapper.ServeTestResults(path));

        Assert.That(_wrapper.LastStartInfo, Is.Not.Null);
        Assert.That(_wrapper.LastStartInfo!.Arguments, Does.Contain(expectedCommandSegment));
    }

    [Test]
    public void TestServeTestResults_WithEchoCommand_ShouldReadStandardOutputWithoutException()
    {
        Assert.DoesNotThrow(() => _wrapper.ServeTestResults("echo"));

        Assert.That(_wrapper.LastStartInfo, Is.Not.Null);
        Assert.That(_wrapper.LastStartInfo!.Arguments, Does.Contain("echo serve"));
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
        });
    }

    [Test]
    public void TestConstructor_ShouldInitializeSuccessfully()
    {
        var wrapper = new AllureWrapper();

        Assert.That(wrapper, Is.Not.Null);
    }
}
