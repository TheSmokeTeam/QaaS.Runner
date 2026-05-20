using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class PluginAssemblyDiscoveryTests
{
    private const string Contract = "Contract.Anchor";

    [SetUp]
    public void ResetCache() => PluginAssemblyDiscovery.ResetCacheForTesting();

    [Test]
    public void Discover_WhenDependencyContextIsNull_FallsBackToBinScan()
    {
        var (assemblies, fromManifest) = PluginAssemblyDiscovery.FindCandidateAssemblies(
            dependencyContext: null,
            contractAnchor: typeof(IExecutionBuilderConfigurator).Assembly,
            logger: Mock.Of<ILogger>());

        Assert.That(fromManifest, Is.False);
        Assert.That(assemblies, Is.Not.Empty);
        Assert.That(
            assemblies.Any(assembly => assembly == typeof(IExecutionBuilderConfigurator).Assembly),
            Is.True);
    }

    [Test]
    public void Discover_WhenContractAssemblyAbsentFromManifest_FallsBackToBinScan()
    {
        var context = BuildContext(Library("Some.Unrelated.Lib"), Library("Some.Other.Lib", "Some.Unrelated.Lib"));

        var (assemblies, fromManifest) = PluginAssemblyDiscovery.FindCandidateAssemblies(
            context,
            typeof(IExecutionBuilderConfigurator).Assembly,
            Mock.Of<ILogger>());

        Assert.That(fromManifest, Is.False);
        Assert.That(
            assemblies.Any(assembly => assembly == typeof(IExecutionBuilderConfigurator).Assembly),
            Is.True,
            "Fallback bin scan should still discover the contract assembly.");
    }

    [Test]
    public void FindAssembliesReferencingContract_WalksTransitively()
    {
        var context = BuildContext(
            Library(Contract),
            Library("Plugin.A", Contract),
            Library("Plugin.B", "Plugin.A"),
            Library("Plugin.C", "Plugin.B"),
            Library("Unrelated", "System.Runtime"));

        var closure = PluginAssemblyDiscovery.FindAssembliesReferencingContract(context, Contract);

        Assert.That(closure, Is.EquivalentTo(new[] { Contract, "Plugin.A", "Plugin.B", "Plugin.C" }));
    }

    [Test]
    public void FindAssembliesReferencingContract_HandlesBranchingGraph()
    {
        var context = BuildContext(
            Library(Contract),
            Library("Branch.Left", Contract),
            Library("Branch.Right", Contract),
            Library("Branch.LeftChild", "Branch.Left"));

        var closure = PluginAssemblyDiscovery.FindAssembliesReferencingContract(context, Contract);

        Assert.That(closure, Is.EquivalentTo(new[]
        {
            Contract,
            "Branch.Left",
            "Branch.Right",
            "Branch.LeftChild"
        }));
    }

    [Test]
    public void FindAssembliesReferencingContract_HandlesCyclesWithoutInfiniteLoop()
    {
        var context = BuildContext(
            Library(Contract),
            Library("Cycle.A", Contract, "Cycle.B"),
            Library("Cycle.B", "Cycle.A"));

        var closure = PluginAssemblyDiscovery.FindAssembliesReferencingContract(context, Contract);

        Assert.That(closure, Is.EquivalentTo(new[] { Contract, "Cycle.A", "Cycle.B" }));
    }

    [Test]
    public void FindAssembliesReferencingContract_ExcludesDisconnectedLibraries()
    {
        var context = BuildContext(
            Library(Contract),
            Library("Plugin", Contract),
            Library("Disconnected", "System.Runtime"),
            Library("Disconnected.Child", "Disconnected"));

        var closure = PluginAssemblyDiscovery.FindAssembliesReferencingContract(context, Contract);

        Assert.That(closure, Is.EquivalentTo(new[] { Contract, "Plugin" }));
        Assert.That(closure, Does.Not.Contain("Disconnected"));
        Assert.That(closure, Does.Not.Contain("Disconnected.Child"));
    }

    [Test]
    public void FindAssembliesReferencingContract_HandlesPackageIdDifferentFromAssemblyName()
    {
        var context = BuildContext(
            LibraryWithAssemblies("Contract.Package", Contract),
            LibraryWithAssemblies("Acme.Plugin.Package", "Acme.Plugin.Core")
                .WithDependency("Contract.Package"));

        var closure = PluginAssemblyDiscovery.FindAssembliesReferencingContract(context, Contract);

        Assert.That(closure, Does.Contain(Contract));
        Assert.That(closure, Does.Contain("Acme.Plugin.Core"));
    }

    [Test]
    public void FindAssembliesReferencingContract_IsCaseInsensitive()
    {
        var context = BuildContext(
            Library(Contract),
            Library("UPPER.Plugin", Contract.ToLower()),
            Library("lower.plugin", Contract.ToUpper()));

        var closure = PluginAssemblyDiscovery.FindAssembliesReferencingContract(context, Contract);

        Assert.That(closure, Does.Contain("UPPER.Plugin"));
        Assert.That(closure, Does.Contain("lower.plugin"));
    }

    [Test]
    public void GetCandidateAssemblies_CachesSuccessfulManifestResults()
    {
        var first = PluginAssemblyDiscovery.GetCandidateAssemblies(Mock.Of<ILogger>());
        var second = PluginAssemblyDiscovery.GetCandidateAssemblies(Mock.Of<ILogger>());

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
            dependencies: dependencies.Select(d => new Dependency(d, "1.0.0")).ToArray(),
            serviceable: true);

    private static LibraryBuilder LibraryWithAssemblies(string packageId, params string[] assemblyNames) =>
        new(packageId, assemblyNames);

    private sealed class LibraryBuilder
    {
        private readonly string _packageId;
        private readonly string[] _assemblyNames;
        private readonly List<string> _dependencies = new();

        public LibraryBuilder(string packageId, string[] assemblyNames)
        {
            _packageId = packageId;
            _assemblyNames = assemblyNames;
        }

        public LibraryBuilder WithDependency(string packageId)
        {
            _dependencies.Add(packageId);
            return this;
        }

        public static implicit operator RuntimeLibrary(LibraryBuilder builder) => new(
            type: "package",
            name: builder._packageId,
            version: "1.0.0",
            hash: string.Empty,
            runtimeAssemblyGroups: new[]
            {
                new RuntimeAssetGroup(
                    string.Empty,
                    builder._assemblyNames.Select(name => $"lib/net10.0/{name}.dll").ToArray())
            },
            nativeLibraryGroups: Array.Empty<RuntimeAssetGroup>(),
            resourceAssemblies: Array.Empty<ResourceAssembly>(),
            dependencies: builder._dependencies.Select(d => new Dependency(d, "1.0.0")).ToArray(),
            serviceable: true);
    }
}
