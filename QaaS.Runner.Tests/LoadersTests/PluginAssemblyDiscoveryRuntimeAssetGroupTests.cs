using Microsoft.Extensions.DependencyModel;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class PluginAssemblyDiscoveryRuntimeAssetGroupTests
{
    private const string ContractAssembly = "Contract.Anchor";

    [Test]
    public void ComputeReverseDependencyClosure_WhenTargetHasRid_UsesSelectedRuntimeAssemblyGroup()
    {
        var contractLibrary = LibraryProducing(
            packageId: "Contract.Package",
            assetGroups: new[]
            {
                new RuntimeAssetGroup(string.Empty, new[] { "lib/net10.0/Contract.Anchor.dll" })
            });

        var pluginLibrary = LibraryProducing(
            packageId: "Contoso.Plugin.Package",
            assetGroups: new[]
            {
                new RuntimeAssetGroup(string.Empty, new[] { "lib/net10.0/Contoso.Plugin.dll" }),
                new RuntimeAssetGroup("linux-x64", new[] { "runtimes/linux-x64/lib/net10.0/Contoso.Plugin.Linux.dll" }),
                new RuntimeAssetGroup("win-x64", new[] { "runtimes/win-x64/lib/net10.0/Contoso.Plugin.Windows.dll" })
            },
            dependencies: new[] { "Contract.Package" });

        var context = BuildContext(contractLibrary, pluginLibrary);

        var closure = PluginAssemblyDiscovery.ComputeReverseDependencyClosure(context, ContractAssembly);

        Assert.That(
            closure,
            Is.EquivalentTo(new[] { ContractAssembly, "Contoso.Plugin.Linux" }),
            "For a linux-x64 dependency context, runtime asset extraction should use the selected RID-specific group "
            + "instead of merging the default and every other RID-specific group.");
    }

    private static DependencyContext BuildContext(params RuntimeLibrary[] libraries) =>
        new(
            new TargetInfo("net10.0", "linux-x64", null, isPortable: false),
            CompilationOptions.Default,
            Array.Empty<CompilationLibrary>(),
            libraries,
            new[]
            {
                new RuntimeFallbacks("linux-x64", new[] { "linux", "unix-x64", "unix", string.Empty }),
                new RuntimeFallbacks("win-x64", new[] { "win", string.Empty })
            });

    private static RuntimeLibrary LibraryProducing(
        string packageId,
        RuntimeAssetGroup[] assetGroups,
        string[]? dependencies = null) =>
        new(
            type: "package",
            name: packageId,
            version: "1.0.0",
            hash: string.Empty,
            runtimeAssemblyGroups: assetGroups,
            nativeLibraryGroups: Array.Empty<RuntimeAssetGroup>(),
            resourceAssemblies: Array.Empty<ResourceAssembly>(),
            dependencies: (dependencies ?? Array.Empty<string>())
                .Select(d => new Dependency(d, "1.0.0"))
                .ToArray(),
            serviceable: true);
}
