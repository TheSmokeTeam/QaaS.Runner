using Microsoft.Extensions.DependencyModel;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

/// <summary>
/// Adversarial: when two RuntimeLibrary entries each emit a runtime assembly with the
/// same simple name as the contract anchor, only the *first* library is recorded as the
/// owner (libraryOwningAssembly uses TryAdd). The reverse-walk therefore starts from a
/// single seed and silently drops the entire subgraph of dependents that hang off the
/// second library.
///
/// Real-world trigger: a plugin host that ships the contract anchor next to an SDK
/// metapackage which also re-exposes the contract assembly under the same simple name
/// (think `Microsoft.Bcl.AsyncInterfaces` style polyfills, or a contract assembly that
/// is split across a primary package and a back-compat shim package). NuGet does not
/// forbid two packages from publishing the same simple-named DLL.
/// </summary>
[TestFixture]
public class PluginAssemblyDiscoveryDuplicateAssemblyOwnerTests
{
    private const string ContractAssembly = "Contract.Anchor";

    [Test]
    public void ComputeReverseDependencyClosure_WhenContractAssemblyIsOwnedByTwoLibraries_WalksBothSubgraphs()
    {
        // Two packages each ship lib/net10.0/Contract.Anchor.dll.
        var contractPrimary = LibraryProducing(
            packageId: "Contract.Primary",
            assetPaths: new[] { "lib/net10.0/Contract.Anchor.dll" });

        var contractShim = LibraryProducing(
            packageId: "Contract.Shim",
            assetPaths: new[] { "lib/net10.0/Contract.Anchor.dll" });

        // Plugin.Primary depends on the primary package; Plugin.Shim depends on the shim.
        // Both legitimately consume the Contract.Anchor assembly at runtime and therefore
        // both should appear in the reverse-dependency closure of `Contract.Anchor`.
        var pluginPrimary = LibraryProducing(
            packageId: "Plugin.Primary",
            assetPaths: new[] { "lib/net10.0/Plugin.Primary.dll" },
            dependencies: new[] { "Contract.Primary" });

        var pluginShim = LibraryProducing(
            packageId: "Plugin.Shim",
            assetPaths: new[] { "lib/net10.0/Plugin.Shim.dll" },
            dependencies: new[] { "Contract.Shim" });

        var context = BuildContext(contractPrimary, contractShim, pluginPrimary, pluginShim);

        var closure = PluginAssemblyDiscovery.ComputeReverseDependencyClosure(context, ContractAssembly);

        Assert.That(closure, Does.Contain("Plugin.Primary"),
            "Plugin.Primary depends on Contract.Primary which ships the contract assembly; it must be in the closure.");
        Assert.That(closure, Does.Contain("Plugin.Shim"),
            "Plugin.Shim depends on Contract.Shim which also ships the contract assembly; it must be in the closure too. "
            + "Current implementation registers only the FIRST library as the owner of the assembly name via TryAdd, "
            + "so the BFS seed misses the second subgraph.");
    }

    private static DependencyContext BuildContext(params RuntimeLibrary[] libraries) =>
        new(
            new TargetInfo("net10.0", null, null, isPortable: true),
            CompilationOptions.Default,
            Array.Empty<CompilationLibrary>(),
            libraries,
            Array.Empty<RuntimeFallbacks>());

    private static RuntimeLibrary LibraryProducing(
        string packageId,
        string[] assetPaths,
        string[]? dependencies = null) =>
        new(
            type: "package",
            name: packageId,
            version: "1.0.0",
            hash: string.Empty,
            runtimeAssemblyGroups: new[]
            {
                new RuntimeAssetGroup(string.Empty, assetPaths)
            },
            nativeLibraryGroups: Array.Empty<RuntimeAssetGroup>(),
            resourceAssemblies: Array.Empty<ResourceAssembly>(),
            dependencies: (dependencies ?? Array.Empty<string>())
                .Select(d => new Dependency(d, "1.0.0"))
                .ToArray(),
            serviceable: true);
}
