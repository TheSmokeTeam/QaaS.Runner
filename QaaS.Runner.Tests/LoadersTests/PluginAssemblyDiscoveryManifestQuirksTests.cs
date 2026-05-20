using Microsoft.Extensions.DependencyModel;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class PluginAssemblyDiscoveryManifestQuirksTests
{
    private const string ContractAssembly = "Contract.Anchor";

    [Test]
    public void ComputeReverseDependencyClosure_WhenDependencyPathGoesThroughCompilationOnlyLibrary_KeepsRuntimePlugin()
    {
        var contract = RuntimeLibraryProducing(
            packageId: "Contract.Package",
            assemblyName: ContractAssembly);
        var compileOnlyAdapter = CompilationLibraryReferencing(
            packageId: "Compile.Only.Adapter",
            dependencies: new[] { "Contract.Package" });
        var plugin = RuntimeLibraryProducing(
            packageId: "Plugin.Package",
            assemblyName: "Plugin.Runtime",
            dependencies: new[] { "Compile.Only.Adapter" });

        var context = BuildContext(new[] { compileOnlyAdapter }, contract, plugin);

        var closure = PluginAssemblyDiscovery.ComputeReverseDependencyClosure(context, ContractAssembly);

        Assert.That(
            closure,
            Does.Contain("Plugin.Runtime"),
            "Plugin.Package is a runtime library, and its manifest dependency path reaches the contract through "
            + "Compile.Only.Adapter. The reverse walk should use CompilationLibraries for graph edges even when "
            + "only RuntimeLibraries contribute loadable runtime assemblies.");
    }

    private static DependencyContext BuildContext(
        IReadOnlyList<CompilationLibrary> compilationLibraries,
        params RuntimeLibrary[] runtimeLibraries) =>
        new(
            new TargetInfo("net10.0", null, null, isPortable: true),
            CompilationOptions.Default,
            compilationLibraries,
            runtimeLibraries,
            Array.Empty<RuntimeFallbacks>());

    private static RuntimeLibrary RuntimeLibraryProducing(
        string packageId,
        string assemblyName,
        string[]? dependencies = null) =>
        new(
            type: "package",
            name: packageId,
            version: "1.0.0",
            hash: string.Empty,
            runtimeAssemblyGroups: new[]
            {
                new RuntimeAssetGroup(string.Empty, new[] { $"lib/net10.0/{assemblyName}.dll" })
            },
            nativeLibraryGroups: Array.Empty<RuntimeAssetGroup>(),
            resourceAssemblies: Array.Empty<ResourceAssembly>(),
            dependencies: (dependencies ?? Array.Empty<string>())
                .Select(dependency => new Dependency(dependency, "1.0.0"))
                .ToArray(),
            serviceable: true);

    private static CompilationLibrary CompilationLibraryReferencing(
        string packageId,
        string[] dependencies) =>
        new(
            type: "package",
            name: packageId,
            version: "1.0.0",
            hash: string.Empty,
            assemblies: new[] { $"ref/net10.0/{packageId}.dll" },
            dependencies: dependencies
                .Select(dependency => new Dependency(dependency, "1.0.0"))
                .ToArray(),
            serviceable: true);
}
