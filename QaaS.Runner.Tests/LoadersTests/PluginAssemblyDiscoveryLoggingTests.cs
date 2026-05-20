using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class PluginAssemblyDiscoveryLoggingTests
{
    [SetUp]
    public void ResetCache() => PluginAssemblyDiscovery.ResetCacheForTesting();

    [Test]
    public void Discover_WhenContractAssemblyAbsentFromManifest_LogsContractAssemblyAsStructuredField()
    {
        var context = BuildContext(Library("Some.Unrelated.Lib"));
        var logger = new Mock<ILogger>();
        var contractAssemblyName = typeof(IExecutionBuilderConfigurator).Assembly.GetName().Name!;

        PluginAssemblyDiscovery.Discover(
            context,
            typeof(IExecutionBuilderConfigurator).Assembly,
            logger.Object);

        logger.Verify(log => log.Log(
                It.Is<LogLevel>(level => level == LogLevel.Information),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    ContainsStructuredValue(state, "ContractAssembly", contractAssemblyName)),
                It.Is<Exception?>(exception => exception == null),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static bool ContainsStructuredValue(object state, string key, string expectedValue) =>
        state is IEnumerable<KeyValuePair<string, object?>> values &&
        values.Any(value => value.Key == key && Equals(value.Value, expectedValue));

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
}
