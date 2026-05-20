using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class PluginAssemblyDiscoveryExceptionTests
{
    private const string MissingPluginAssembly = "QaaS.Runner.Tests.OutOfMemoryProbe.Plugin";

    [SetUp]
    public void ResetCache() => PluginAssemblyDiscovery.ResetCacheForTesting();

    [Test]
    public void Discover_WhenManifestAssemblyLoadThrowsOutOfMemoryException_PropagatesFatalException()
    {
        var contractAnchor = typeof(IExecutionBuilderConfigurator).Assembly;
        var contractAssemblyName = contractAnchor.GetName().Name!;
        var context = BuildContext(
            Library(contractAssemblyName),
            Library(MissingPluginAssembly, contractAssemblyName));

        Assembly? ThrowOutOfMemoryForMissingPlugin(AssemblyLoadContext _, AssemblyName assemblyName)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(assemblyName.Name, MissingPluginAssembly))
                throw new OutOfMemoryException("Synthetic fatal assembly load failure.");

            return null;
        }

        AssemblyLoadContext.Default.Resolving += ThrowOutOfMemoryForMissingPlugin;
        try
        {
            Assert.That(
                () => PluginAssemblyDiscovery.Discover(context, contractAnchor, Mock.Of<ILogger>()),
                Throws.TypeOf<OutOfMemoryException>()
                    .With.Message.EqualTo("Synthetic fatal assembly load failure."));
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= ThrowOutOfMemoryForMissingPlugin;
        }
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
            dependencies: dependencies.Select(dependency => new Dependency(dependency, "1.0.0")).ToArray(),
            serviceable: true);
}
