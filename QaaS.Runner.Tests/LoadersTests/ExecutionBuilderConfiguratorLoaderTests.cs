using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class ExecutionBuilderConfiguratorLoaderTests
{
    [TestCase("older", false)]
    [TestCase("newer", true)]
    [TestCase("other key", true)]
    [TestCase("other culture", true)]
    public void Load_OnlyUnifiesCompatibleLoadedRuntimeReferences(string identity, bool shouldScan)
    {
        var runtime = typeof(object).Assembly;
        var reference = new AssemblyName(runtime.FullName!);
        reference.Version =
            identity == "newer" ? new Version(99, 0, 0, 0) : new Version(1, 0, 0, 0);
        if (identity == "other key")
            reference.SetPublicKeyToken([1, 2, 3, 4, 5, 6, 7, 8]);
        if (identity == "other culture")
            reference.CultureName = "fr-FR";
        var consumer = Candidate("Consumer", reference.FullName);

        ExecutionBuilderConfiguratorLoader.Load(
            Mock.Of<ILogger>(),
            null,
            [runtime, consumer.Object]
        );

        consumer.Verify(a => a.GetTypes(), shouldScan ? Times.Once() : Times.Never());
    }

    [Test]
    public void Load_KeepsIndirectImplementationsAcrossCompatibleAndCustomContexts()
    {
        var current = typeof(PublicConfigurator).Assembly;
        var earlier = new AssemblyName(current.FullName!) { Version = new Version(0, 0, 0, 0) };
        var indirect = Candidate("UseInheritedConfigurator", earlier.FullName);
        ExecutionBuilderConfiguratorLoader.Load(
            Mock.Of<ILogger>(),
            null,
            [current, indirect.Object]
        );
        indirect.Verify(a => a.GetTypes(), Times.Once);

        var custom = new AssemblyLoadContext("ConfiguratorTest", isCollectible: true);
        try
        {
            var plugin = custom.LoadFromAssemblyPath(current.Location);
            var found = ExecutionBuilderConfiguratorLoader.Load(Mock.Of<ILogger>(), null, [plugin]);
            Assert.That(
                found.Any(configurator => configurator.GetType().Assembly == plugin),
                Is.True
            );
        }
        finally
        {
            custom.Unload();
        }
    }

    [Test]
    public void Load_ScansTransitiveAndUnresolvedDependenciesAndSkipsUnrelatedTypes()
    {
        var contract = typeof(IExecutionBuilderConfigurator).Assembly.FullName!;
        var unrelated = Candidate("Library", "OtherLibrary");
        var other = Candidate("OtherLibrary", "Library");
        var bridge = Candidate("Bridge", contract);
        var indirect = Candidate("Plugin", "Bridge");
        var unresolved = Candidate("ExternalPlugin", "OutsideTheCandidateSet");
        indirect.Setup(a => a.GetTypes()).Returns([typeof(PublicConfigurator)]);
        var candidates = new[]
        {
            Candidate(contract).Object,
            unrelated.Object,
            other.Object,
            bridge.Object,
            indirect.Object,
            unresolved.Object,
        };

        var first = ExecutionBuilderConfiguratorLoader.Load(Mock.Of<ILogger>(), null, candidates);
        var second = ExecutionBuilderConfiguratorLoader.Load(Mock.Of<ILogger>(), null, candidates);

        Assert.That(first.Single(), Is.TypeOf<PublicConfigurator>());
        Assert.That(second.Single(), Is.Not.SameAs(first.Single()));
        unrelated.Verify(a => a.GetTypes(), Times.Never);
        indirect.Verify(a => a.GetTypes(), Times.Once);
        unresolved.Verify(a => a.GetTypes(), Times.AtLeastOnce);
    }

    [Test]
    public void Load_RetriesPartialTypeLoadsInLaterCommands()
    {
        var plugin = Candidate("Plugin", typeof(IExecutionBuilderConfigurator).Assembly.FullName!);
        plugin
            .SetupSequence(assembly => assembly.GetTypes())
            .Throws(new ReflectionTypeLoadException([null], [new FileNotFoundException()]))
            .Returns([typeof(PublicConfigurator)]);

        var first = ExecutionBuilderConfiguratorLoader.Load(
            Mock.Of<ILogger>(),
            null,
            [plugin.Object]
        );
        var next = ExecutionBuilderConfiguratorLoader.Load(
            Mock.Of<ILogger>(),
            null,
            [plugin.Object]
        );

        Assert.That(first, Is.Empty);
        Assert.That(next.Single(), Is.TypeOf<PublicConfigurator>());
    }

    private static Mock<Assembly> Candidate(string name, params string[] references)
    {
        var assembly = new Mock<Assembly>();
        assembly.Setup(a => a.FullName).Returns(new AssemblyName(name).FullName);
        assembly
            .Setup(a => a.GetReferencedAssemblies())
            .Returns(references.Select(r => new AssemblyName(r)).ToArray());
        assembly.Setup(a => a.GetTypes()).Returns(Array.Empty<Type>());
        return assembly;
    }

    public sealed class PublicConfigurator : IExecutionBuilderConfigurator
    {
        public void Configure(ExecutionBuilder executionBuilder) { }
    }

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
