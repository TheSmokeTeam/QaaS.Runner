using System.Reflection;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Loaders;

/// <summary>
/// Resolves the assemblies that may contain QaaS plugin types by walking the reverse
/// dependency graph rooted at a contract anchor via <see cref="DependencyContext"/>.
/// Falls back to a base-directory DLL scan only when the dependency manifest is
/// unusable. Successful manifest-driven results are cached for the process lifetime.
/// </summary>
internal static class PluginAssemblyDiscovery
{
    private static readonly Lock CacheLock = new();
    private static volatile IReadOnlyList<Assembly>? _configuratorCache;

    /// <summary>
    /// Returns candidate plugin assemblies anchored at <see cref="IExecutionBuilderConfigurator"/>.
    /// Cached after the first manifest-driven discovery; fallback results bypass the cache.
    /// </summary>
    public static IReadOnlyList<Assembly> GetCandidateAssemblies(ILogger logger)
    {
        if (_configuratorCache is { } cached)
            return cached;

        lock (CacheLock)
        {
            if (_configuratorCache is { } existing)
                return existing;

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
    /// Resolves candidate assemblies for an arbitrary contract anchor. Not cached.
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
        var contractName = contractAnchor.GetName().Name;
        if (string.IsNullOrEmpty(contractName))
        {
            logger.LogInformation("Plugin discovery falling back: contract anchor has no simple name.");
            return (Fallback(logger), FromManifest: false);
        }

        if (dependencyContext is null)
        {
            logger.LogInformation("Plugin discovery falling back: DependencyContext.Default is unavailable.");
            return (Fallback(logger), FromManifest: false);
        }

        IReadOnlySet<string> closure;
        try
        {
            closure = ComputeReverseDependencyClosure(dependencyContext, contractName);
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            logger.LogWarning(exception, "Reverse-dependency walk failed; falling back to base-directory scan.");
            return (Fallback(logger), FromManifest: false);
        }

        if (closure.Count == 0)
        {
            logger.LogInformation(
                "Plugin discovery falling back: contract assembly {ContractAssembly} not present in dependency manifest.",
                contractName);
            return (Fallback(logger), FromManifest: false);
        }

        var assemblies = SeedFromAppDomain();
        foreach (var name in closure)
        {
            if (assemblies.ContainsSimpleName(name))
                continue;

            try
            {
                assemblies.Add(Assembly.Load(new AssemblyName(name)));
            }
            catch (Exception exception) when (!IsFatalException(exception))
            {
                logger.LogWarning(
                    exception,
                    "Could not load candidate plugin assembly {AssemblyName}; skipping.",
                    name);
            }
        }

        logger.LogDebug(
            "Plugin discovery resolved {Count} candidate assemblies for {ContractAssembly}.",
            assemblies.Count,
            contractName);

        return (assemblies.Snapshot(), FromManifest: true);
    }

    internal static IReadOnlySet<string> ComputeReverseDependencyClosure(
        DependencyContext dependencyContext,
        string contractAssemblyName)
    {
        var assembliesByLibrary = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var librariesOwningAssembly = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in dependencyContext.RuntimeLibraries)
        {
            var names = ExtractRuntimeAssemblyNames(library, dependencyContext);
            assembliesByLibrary[library.Name] = names;
            foreach (var name in names)
            {
                if (!librariesOwningAssembly.TryGetValue(name, out var owners))
                    librariesOwningAssembly[name] = owners = [];
                owners.Add(library.Name);
            }
        }

        if (!librariesOwningAssembly.TryGetValue(contractAssemblyName, out var contractOwners))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var reverseEdges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AppendEdges(reverseEdges, dependencyContext.RuntimeLibraries.Select(l => (l.Name, l.Dependencies)));
        AppendEdges(reverseEdges, dependencyContext.CompileLibraries.Select(l => (l.Name, l.Dependencies)));

        var libraryClosure = new HashSet<string>(contractOwners, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(contractOwners);
        while (queue.TryDequeue(out var current))
        {
            if (!reverseEdges.TryGetValue(current, out var dependents))
                continue;

            foreach (var dependent in dependents)
                if (libraryClosure.Add(dependent))
                    queue.Enqueue(dependent);
        }

        var assemblyClosure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var libraryName in libraryClosure)
            if (assembliesByLibrary.TryGetValue(libraryName, out var names))
                foreach (var name in names)
                    assemblyClosure.Add(name);

        return assemblyClosure;
    }

    private static void AppendEdges(
        Dictionary<string, List<string>> reverseEdges,
        IEnumerable<(string Name, IReadOnlyList<Dependency> Dependencies)> libraries)
    {
        foreach (var (name, dependencies) in libraries)
            foreach (var dependency in dependencies)
            {
                if (!reverseEdges.TryGetValue(dependency.Name, out var dependents))
                    reverseEdges[dependency.Name] = dependents = [];
                dependents.Add(name);
            }
    }

    private static IReadOnlyList<string> ExtractRuntimeAssemblyNames(RuntimeLibrary library, DependencyContext context)
    {
        var runtime = context.Target.Runtime;
        var resolved = string.IsNullOrEmpty(runtime)
            ? library.GetDefaultAssemblyNames(context)
            : library.GetRuntimeAssemblyNames(context, runtime);

        var names = new List<string>();
        foreach (var assemblyName in resolved)
            if (!string.IsNullOrEmpty(assemblyName.Name))
                names.Add(assemblyName.Name);

        if (names.Count == 0)
            names.Add(library.Name);

        return names;
    }

    private static IReadOnlyList<Assembly> Fallback(ILogger logger)
    {
        var assemblies = SeedFromAppDomain();
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            return assemblies.Snapshot();

        foreach (var path in Directory.EnumerateFiles(baseDirectory, "*.dll"))
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(path);
                if (assemblies.ContainsSimpleName(name.Name ?? string.Empty))
                    continue;

                assemblies.Add(Assembly.LoadFrom(path));
            }
            catch (Exception exception) when (!IsFatalException(exception))
            {
                logger.LogDebug(exception, "Skipping unloadable assembly at {AssemblyPath}.", path);
            }
        }

        return assemblies.Snapshot();
    }

    private static AssemblyCollection SeedFromAppDomain()
    {
        var assemblies = new AssemblyCollection();
        assemblies.Add(Assembly.GetEntryAssembly());
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            assemblies.Add(loaded);
        return assemblies;
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or ThreadAbortException;

    internal static void ResetCacheForTesting()
    {
        lock (CacheLock)
            _configuratorCache = null;
    }

    private sealed class AssemblyCollection
    {
        private readonly Dictionary<string, Assembly> _byFullName = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _simpleNames = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _byFullName.Count;

        public void Add(Assembly? assembly)
        {
            if (assembly is null || assembly.IsDynamic)
                return;

            var fullName = assembly.FullName;
            if (string.IsNullOrEmpty(fullName) || !_byFullName.TryAdd(fullName, assembly))
                return;

            var simpleName = assembly.GetName().Name;
            if (!string.IsNullOrEmpty(simpleName))
                _simpleNames.Add(simpleName);
        }

        public bool ContainsSimpleName(string simpleName) => _simpleNames.Contains(simpleName);

        public IReadOnlyList<Assembly> Snapshot() => [.._byFullName.Values];
    }
}
