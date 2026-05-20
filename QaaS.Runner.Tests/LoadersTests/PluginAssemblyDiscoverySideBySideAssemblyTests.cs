using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class PluginAssemblyDiscoverySideBySideAssemblyTests
{
    [SetUp]
    public void ResetCache() => PluginAssemblyDiscovery.ResetCacheForTesting();

    [Test]
    public void Discover_WhenSideBySideAssembliesShareSimpleName_ReturnsBothVersions()
    {
        var simpleName = "QaaS.Adversarial.SideBySide.Plugin." + Guid.NewGuid().ToString("N");
        var tempDirectory = Path.Combine(Path.GetTempPath(), "QaaS.Runner.Tests", Guid.NewGuid().ToString("N"));
        var firstContext = new AssemblyLoadContext(simpleName + ".V1", isCollectible: true);
        var secondContext = new AssemblyLoadContext(simpleName + ".V2", isCollectible: true);

        try
        {
            var first = BuildAndLoadAssembly(
                simpleName,
                version: "1.0.0.0",
                tempDirectory,
                projectDirectoryName: "v1",
                firstContext);
            var second = BuildAndLoadAssembly(
                simpleName,
                version: "2.0.0.0",
                tempDirectory,
                projectDirectoryName: "v2",
                secondContext);

            var loadedVersions = AppDomain
                .CurrentDomain.GetAssemblies()
                .Where(assembly =>
                    string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                .Select(assembly => assembly.GetName().Version?.ToString())
                .OrderBy(version => version)
                .ToArray();

            Assert.That(
                loadedVersions,
                Is.EquivalentTo(new[] { "1.0.0.0", "2.0.0.0" }),
                "Test setup should load both side-by-side assemblies into the current AppDomain.");
            Assert.That(first, Is.Not.SameAs(second));

            var context = BuildContext(Library(simpleName));

            var (assemblies, fromManifest) = PluginAssemblyDiscovery.Discover(
                context,
                first,
                Mock.Of<ILogger>());

            var discoveredVersions = assemblies
                .Where(assembly =>
                    string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                .Select(assembly => assembly.GetName().Version?.ToString())
                .OrderBy(version => version)
                .ToArray();

            Assert.That(fromManifest, Is.True);
            Assert.That(
                discoveredVersions,
                Is.EquivalentTo(new[] { "1.0.0.0", "2.0.0.0" }),
                "Discovery should not collapse side-by-side loaded assemblies that share a simple name.");
        }
        finally
        {
            firstContext.Unload();
            secondContext.Unload();

            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static Assembly BuildAndLoadAssembly(
        string simpleName,
        string version,
        string tempDirectory,
        string projectDirectoryName,
        AssemblyLoadContext loadContext)
    {
        var projectDirectory = Path.Combine(tempDirectory, projectDirectoryName);
        Directory.CreateDirectory(projectDirectory);

        var projectFile = Path.Combine(projectDirectory, $"{simpleName}.csproj");
        File.WriteAllText(
            projectFile,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{simpleName}</AssemblyName>
                <AssemblyVersion>{version}</AssemblyVersion>
                <FileVersion>{version}</FileVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(projectDirectory, "Marker.cs"),
            """
            namespace SideBySideCollision;

            public sealed class Marker
            {
            }
            """);

        var buildResult = RunDotnetBuild(projectFile);
        Assert.That(buildResult.ExitCode, Is.EqualTo(0), buildResult.Output);

        var assemblyPath = Path.Combine(
            projectDirectory,
            "bin",
            "Debug",
            "net10.0",
            $"{simpleName}.dll");
        Assert.That(File.Exists(assemblyPath), Is.True, $"Expected test assembly at {assemblyPath}.");

        return loadContext.LoadFromAssemblyPath(assemblyPath);
    }

    private static (int ExitCode, string Output) RunDotnetBuild(string projectFile)
    {
        using var process = Process.Start(new ProcessStartInfo(GetDotnetExecutable())
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            ArgumentList =
            {
                "build",
                projectFile,
                "-nologo",
                "-clp:ErrorsOnly",
                "/p:RestoreIgnoreFailedSources=true"
            }
        });

        Assert.That(process, Is.Not.Null, "Could not start dotnet build for side-by-side test assembly.");

        var exited = process!.WaitForExit(milliseconds: 120000);
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Timed out while building side-by-side test assembly." + Environment.NewLine + output);
        }

        return (process.ExitCode, output);
    }

    private static string GetDotnetExecutable()
    {
        if (IsDotnetHost(Environment.ProcessPath))
            return Environment.ProcessPath!;

        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (IsDotnetHost(dotnetHostPath))
            return dotnetHostPath!;

        var userDotnet = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        if (File.Exists(userDotnet))
            return userDotnet;

        return "dotnet";
    }

    private static bool IsDotnetHost(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(path)
        && string.Equals(
            Path.GetFileNameWithoutExtension(path),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

    private static DependencyContext BuildContext(params RuntimeLibrary[] libraries) =>
        new(
            new TargetInfo("net10.0", null, null, isPortable: true),
            CompilationOptions.Default,
            Array.Empty<CompilationLibrary>(),
            libraries,
            Array.Empty<RuntimeFallbacks>());

    private static RuntimeLibrary Library(string name) =>
        new(
            type: "package",
            name: name,
            version: "1.0.0",
            hash: string.Empty,
            runtimeAssemblyGroups: Array.Empty<RuntimeAssetGroup>(),
            nativeLibraryGroups: Array.Empty<RuntimeAssetGroup>(),
            resourceAssemblies: Array.Empty<ResourceAssembly>(),
            dependencies: Array.Empty<Dependency>(),
            serviceable: true);
}
