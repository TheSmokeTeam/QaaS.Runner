using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class ExecutionBuilderConfiguratorLoaderTests
{
    [Test]
    public void Load_WhenEntryAssemblyContainsInternalConfigurator_LoadsIt()
    {
        var logger = Mock.Of<ILogger>();

        var configurators = ExecutionBuilderConfiguratorLoader.Load(
            logger,
            typeof(InternalEntryAssemblyConfigurator).Assembly,
            [typeof(InternalEntryAssemblyConfigurator)]);

        Assert.That(
            configurators.Any(configurator => configurator.GetType() == typeof(InternalEntryAssemblyConfigurator)),
            Is.True);
    }

    [Test]
    public void Load_WhenDependencyConfiguratorCannotBeActivated_SkipsIt()
    {
        var logger = new Mock<ILogger>();

        Assert.DoesNotThrow(() => ExecutionBuilderConfiguratorLoader.Load(
            logger.Object,
            typeof(Runner).Assembly,
            [typeof(PublicDependencyConfiguratorWithoutDefaultConstructor)]));

        var configurators = ExecutionBuilderConfiguratorLoader.Load(
            logger.Object,
            typeof(Runner).Assembly,
            [typeof(PublicDependencyConfiguratorWithoutDefaultConstructor)]);

        Assert.That(
            configurators.Any(configurator =>
                configurator.GetType() == typeof(PublicDependencyConfiguratorWithoutDefaultConstructor)),
            Is.False);
    }

    internal sealed class InternalEntryAssemblyConfigurator : IExecutionBuilderConfigurator
    {
        public void Configure(ExecutionBuilder executionBuilder)
        {
        }
    }

    public sealed class PublicDependencyConfiguratorWithoutDefaultConstructor : IExecutionBuilderConfigurator
    {
        public PublicDependencyConfiguratorWithoutDefaultConstructor(string requiredValue)
        {
            _ = requiredValue;
        }

        public void Configure(ExecutionBuilder executionBuilder)
        {
        }
    }
}
