using System.Reflection;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Loaders;

/// <summary>
/// Resolves the set of assemblies that may contain QaaS plugins by walking the reverse
/// dependency graph rooted at a contract anchor assembly via <see cref="DependencyContext"/>.
/// Falls back to scanning every DLL in the base directory only when the dependency manifest
/// cannot answer the question. Successful manifest-driven results are cached for the
/// process lifetime; fallback results are not cached so a later call can recover.
/// </summary>
/// <remarks>
/// The legacy bin-folder scan opens every DLL next to the entry exe. On machines running
/// on-access AV (Trellix, McAfee, etc.) that serialises a virus scan per file and dominates
/// cold-start time. The manifest-driven walk touches only the small reverse-closure of the
/// contract assembly, cutting startup from minutes to seconds in those environments.
/// </remarks>
internal static class PluginAssemblyDiscovery
{
    private static readonly object CacheLock = new();
    private static volatile IReadOnlyList<Assembly>? _configuratorCache;

    /// <summary>
    /// Returns plugin-candidate assemblies anchored at <see cref="IExecutionBuilderConfigurator"/>.
    /// Cached for the process lifetime on success; fallback results bypass the cache.
    /// </summary>
    public static IReadOnlyList<Assembly> GetCandidateAssemblies(ILogger logger)
    {
        if (_configuratorCache is { } cached)
            return cached;

        lock (CacheLock)
        {
            if (_configuratorCache is { } cached2)
                return cached2;

            var (result, fromManifest) = Discover(
                DependencyContext.Default,
                typeof(IExecutionBuilderConfigurator).Assembly,
                logger);

            if (fromManifest)
                _configuratorCache = result;

            return result;
        }
    }

    /// <summary>
    /// Resolves plugin-candidate assemblies for an arbitrary contract anchor. Not cached.
    /// Intended for tests and future callers (e.g. hook discovery) that need a different anchor.
    /// </summary>
    public static IReadOnlyList<Assembly> DiscoverFor(Assembly contractAnchor, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(contractAnchor);
        return Discover(DependencyContext.Default, contractAnchor, logger).Assemblies;
    }

    internal static (IReadOnlyList<Assembly> Assemblies, bool FromManifest) Discover(
        DependencyContext? dependencyContext,
        Assembly contractAnchor,
        ILogger logger)
    {
        var contractAssemblyName = contractAnchor.GetName().Name;
        if (string.IsNullOrEmpty(contractAssemblyName))
            return (Fallback(logger, "Contract anchor assembly has no simple name."), FromManifest: false);

        if (dependencyContext is null)
            return (Fallback(logger, "DependencyContext.Default is unavailable."), FromManifest: false);

        IReadOnlySet<string> closure;
        try
        {
            closure = ComputeReverseDependencyClosure(dependencyContext, contractAssemblyName);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Reverse-dependency walk over dependency manifest failed; falling back to bin-folder scan.");
            return (Fallback(logger, "Reverse-dependency walk threw."), FromManifest: false);
        }

        if (closure.Count == 0)
            return (Fallback(logger, $"Contract assembly {contractAssemblyName} not present in dependency manifest."),
                FromManifest: false);

        var assemblies = SeedFromAppDomain();

        foreach (var simpleName in closure)
        {
            if (ContainsAssembly(assemblies, simpleName))
                continue;

            try
            {
                AddAssembly(assemblies, Assembly.Load(new AssemblyName(simpleName)));
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not load candidate plugin assembly {AssemblyName} resolved from the dependency manifest; it will be skipped.",
                    simpleName);
            }
        }

        logger.LogDebug(
            "Plugin discovery resolved {ResolvedCount} candidate assemblies from {ClosureCount} manifest entries reverse-dependent on {ContractAssembly}.",
            assemblies.Count,
            closure.Count,
            contractAssemblyName);

        return (assemblies.Values.ToArray(), FromManifest: true);
    }

    /// <summary>
    /// Computes the set of assembly simple names that depend (transitively) on
    /// <paramref name="contractAssemblyName"/>. The closure is keyed by assembly name
    /// (not package name) — runtime asset paths from each <see cref="RuntimeLibrary"/>
    /// are extracted so packages whose ID differs from their assembly file name are handled.
    /// </summary>
    internal static IReadOnlySet<string> ComputeReverseDependencyClosure(
        DependencyContext dependencyContext,
        string contractAssemblyName)
    {
        var assembliesByLibrary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var libraryOwningAssembly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in dependencyContext.RuntimeLibraries)
        {
            var names = ExtractRuntimeAssemblyNames(library);
            assembliesByLibrary[library.Name] = names;
            foreach (var name in names)
                libraryOwningAssembly.TryAdd(name, library.Name);
        }

        if (!libraryOwningAssembly.TryGetValue(contractAssemblyName, out var contractLibraryName))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var reverseEdges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in dependencyContext.RuntimeLibraries)
        {
            foreach (var dependency in library.Dependencies)
            {
                if (!reverseEdges.TryGetValue(dependency.Name, out var dependents))
                {
                    dependents = new List<string>();
                    reverseEdges[dependency.Name] = dependents;
                }
                dependents.Add(library.Name);
            }
        }

        var libraryClosure = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { contractLibraryName };
        var queue = new Queue<string>();
        queue.Enqueue(contractLibraryName);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!reverseEdges.TryGetValue(current, out var dependents))
                continue;

            foreach (var dependent in dependents)
            {
                if (libraryClosure.Add(dependent))
                    queue.Enqueue(dependent);
            }
        }

        var assemblyClosure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var libraryName in libraryClosure)
        {
            if (!assembliesByLibrary.TryGetValue(libraryName, out var names))
                continue;

            foreach (var name in names)
                assemblyClosure.Add(name);
        }

        return assemblyClosure;
    }

    private static List<string> ExtractRuntimeAssemblyNames(RuntimeLibrary library)
    {
        var names = new List<string>();
        foreach (var group in library.RuntimeAssemblyGroups)
        {
            foreach (var assetPath in group.AssetPaths)
            {
                var name = Path.GetFileNameWithoutExtension(assetPath);
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
        }

        if (names.Count == 0)
            names.Add(library.Name);

        return names;
    }

    private static IReadOnlyList<Assembly> Fallback(ILogger logger, string reason)
    {
        logger.LogInformation("Falling back to bin-folder plugin scan: {Reason}", reason);

        var assemblies = SeedFromAppDomain();
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            return assemblies.Values.ToArray();

        foreach (var assemblyPath in Directory.GetFiles(baseDirectory, "*.dll"))
        {
            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
                if (ContainsAssembly(assemblies, assemblyName.Name ?? assemblyName.FullName))
                    continue;

                AddAssembly(assemblies, Assembly.LoadFrom(assemblyPath));
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "Skipping unloadable assembly at {AssemblyPath} during fallback bin scan.",
                    assemblyPath);
            }
        }

        return assemblies.Values.ToArray();
    }

    private static Dictionary<string, Assembly> SeedFromAppDomain()
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        AddAssembly(assemblies, Assembly.GetEntryAssembly());
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            AddAssembly(assemblies, loaded);
        return assemblies;
    }

    private static void AddAssembly(IDictionary<string, Assembly> assemblies, Assembly? assembly)
    {
        if (assembly is null || assembly.IsDynamic)
            return;

        var key = assembly.GetName().Name ?? assembly.FullName;
        if (string.IsNullOrWhiteSpace(key) || assemblies.ContainsKey(key))
            return;

        assemblies[key] = assembly;
    }

    private static bool ContainsAssembly(IDictionary<string, Assembly> assemblies, string? simpleName)
    {
        return !string.IsNullOrWhiteSpace(simpleName) && assemblies.ContainsKey(simpleName);
    }

    internal static void ResetCacheForTesting()
    {
        lock (CacheLock)
        {
            _configuratorCache = null;
        }
    }
}
