using System.Reflection;
using NUnit.Framework;
using QaaS.Runner.WrappedExternals;

namespace QaaS.Runner.Tests.WrappedExternalTests;

[TestFixture]
public class AllureWrapperTests
{
    private AllureWrapper _wrapper;

    [SetUp]
    public void SetUp()
    {
        _wrapper = new AllureWrapper();
    }

    [Test]
    public void TestCleanTestResultsDirectory_ShouldExecuteWithoutException()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => _wrapper.CleanTestResultsDirectory());
    }

    [Test,
     TestCase("unknown-path", TestName = "allure path wrong"),
     TestCase("allure", TestName = "allure path right"),
     TestCase("",TestName = "default allure path right")]
    public void TestServeTestResults_WithVariousPaths_ShouldExecuteWithoutException(string path)
    {
        // Act & Assert
        Assert.DoesNotThrow(() => _wrapper.ServeTestResults(path));
    }

    [Test]
    public void TestServeTestResults_WithEchoCommand_ShouldReadStandardOutputWithoutException()
    {
        Assert.DoesNotThrow(() => _wrapper.ServeTestResults("echo"));
    }

    [Test]
    public void TestMethodExistence_ShouldHaveExpectedMethods()
    {
        // Act
        var cleanMethod = typeof(AllureWrapper).GetMethod("CleanTestResultsDirectory",
            BindingFlags.Public | BindingFlags.Instance);
        var serveMethod =
            typeof(AllureWrapper).GetMethod("ServeTestResults", BindingFlags.Public | BindingFlags.Instance);

        // Assert
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
        // Act
        var wrapper = new AllureWrapper();

        // Assert
        Assert.That(wrapper, Is.Not.Null);
    }
}
