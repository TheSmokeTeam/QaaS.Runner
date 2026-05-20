using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class PluginAssemblyDiscoveryTests
{
    private static readonly string ContractAssemblyName =
        typeof(IExecutionBuilderConfigurator).Assembly.GetName().Name!;

    [SetUp]
    public void ResetCache() => PluginAssemblyDiscovery.ResetCacheForTesting();

    [Test]
    public void Discover_WhenDependencyContextIsNull_FallsBackToBinScan()
    {
        var logger = Mock.Of<ILogger>();

        var result = PluginAssemblyDiscovery.Discover(null, logger);

        Assert.That(result, Is.Not.Empty);
        Assert.That(
            result.Any(assembly => assembly == typeof(IExecutionBuilderConfigurator).Assembly),
            Is.True);
    }

    [Test]
    public void Discover_WhenContractAssemblyAbsentFromManifest_FallsBackToBinScan()
    {
        var context = BuildContext(
            Library("Some.Unrelated.Lib"),
            Library("Some.Other.Lib", "Some.Unrelated.Lib"));
        var logger = Mock.Of<ILogger>();

        var result = PluginAssemblyDiscovery.Discover(context, logger);

        Assert.That(
            result.Any(assembly => assembly == typeof(IExecutionBuilderConfigurator).Assembly),
            Is.True,
            "Fallback bin scan should still discover the contract assembly.");
    }

    [Test]
    public void Discover_WhenContractAssemblyPresent_ResolvesContractAssemblyFromManifest()
    {
        var context = BuildContext(
            Library(ContractAssemblyName),
            Library("Plugin.A", ContractAssemblyName),
            Library("Plugin.B", "Plugin.A"),
            Library("Unrelated.Lib", "System.Runtime"));
        var logger = Mock.Of<ILogger>();

        var result = PluginAssemblyDiscovery.Discover(context, logger);

        Assert.That(
            result.Any(assembly => assembly == typeof(IExecutionBuilderConfigurator).Assembly),
            Is.True);
        Assert.That(
            result.Any(assembly =>
                string.Equals(assembly.GetName().Name, "Unrelated.Lib", StringComparison.Ordinal)),
            Is.False);
    }

    [Test]
    public void GetCandidateAssemblies_IsCachedAcrossCalls()
    {
        var logger = Mock.Of<ILogger>();

        var first = PluginAssemblyDiscovery.GetCandidateAssemblies(logger);
        var second = PluginAssemblyDiscovery.GetCandidateAssemblies(logger);

        Assert.That(second, Is.SameAs(first));
    }

    private static DependencyContext BuildContext(params RuntimeLibrary[] libraries) =>
        new(
            new TargetInfo("net10.0", null, null, isPortable: true),
            CompilationOptions.Default,
            Array.Empty<CompilationLibrary>(),
            libraries,
            Array.Empty<RuntimeFallbacks>());

    private static RuntimeLibrary Library(string name, params string[] dependencies) =>
        new(
            type: "package",
            name: name,
            version: "1.0.0",
            hash: string.Empty,
            runtimeAssemblyGroups: Array.Empty<RuntimeAssetGroup>(),
            nativeLibraryGroups: Array.Empty<RuntimeAssetGroup>(),
            resourceAssemblies: Array.Empty<ResourceAssembly>(),
            dependencies: dependencies.Select(name => new Dependency(name, "1.0.0")).ToArray(),
            serviceable: true);
}
