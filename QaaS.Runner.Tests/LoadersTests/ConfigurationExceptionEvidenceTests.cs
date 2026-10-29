using NUnit.Framework;
using QaaS.Framework.Configurations.CustomExceptions;

namespace QaaS.Runner.Tests.LoadersTests;

public class ConfigurationExceptionEvidenceTests
{
    [TestCase("Could not parse the YAML file: invalid mapping.")]
    [TestCase("Cannot read configuration")]
    public void WrappedLoadFailure_PreservesOriginalExceptionAndInnerChain(string message)
    {
        var cause = new ArgumentException("original cause");
        var original = new InvalidOperationException(message, cause);
        var wrapped = RunnerYamlConfigurationExceptionFactory.CreateLocalFileLoadException(
            "test.qaas.yaml",
            original
        );
        Assert.Multiple(() =>
        {
            Assert.That(wrapped, Is.TypeOf<InvalidConfigurationsException>());
            Assert.That(wrapped.InnerException, Is.SameAs(original));
            Assert.That(wrapped.InnerException?.InnerException, Is.SameAs(cause));
        });
    }

    [Test]
    public void ExistingConfigurationFailure_IsNotWrappedAgain()
    {
        var original = new InvalidConfigurationsException("already explained");
        Assert.That(
            RunnerYamlConfigurationExceptionFactory.CreateLocalFileLoadException(
                "test.qaas.yaml",
                original
            ),
            Is.SameAs(original)
        );
    }
}
