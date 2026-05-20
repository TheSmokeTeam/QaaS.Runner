using System.Reflection;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Loaders;

/// <summary>
/// Resolves the set of assemblies that may contain QaaS configurator plugins by walking
/// the reverse dependency graph rooted at <see cref="IExecutionBuilderConfigurator"/>'s
/// assembly via <see cref="DependencyContext"/>. Falls back to scanning every DLL in the
/// base directory only when the dependency manifest cannot answer the question.
/// </summary>
/// <remarks>
/// The legacy bin-folder scan opens every DLL next to the entry exe. On machines running
/// on-access AV (Trellix, McAfee, etc.) that serialises a virus scan per file and dominates
/// cold-start time. The manifest-driven walk touches only the small reverse-closure of the
/// QaaS contract assembly, cutting startup from minutes to seconds in those environments.
/// </remarks>
internal static class PluginAssemblyDiscovery
{
    private static readonly object CacheLock = new();
    private static IReadOnlyList<Assembly>? _cached;

    /// <summary>
    /// Returns the candidate plugin assemblies for the current process, computed once and
    /// cached for the process lifetime. Safe to call concurrently.
    /// </summary>
    public static IReadOnlyList<Assembly> GetCandidateAssemblies(ILogger logger)
    {
        if (_cached is not null)
            return _cached;

        lock (CacheLock)
        {
            return _cached ??= Discover(DependencyContext.Default, logger);
        }
    }

    internal static IReadOnlyList<Assembly> Discover(DependencyContext? dependencyContext, ILogger logger)
    {
        var contractAssemblyName = typeof(IExecutionBuilderConfigurator).Assembly.GetName().Name;
        if (string.IsNullOrEmpty(contractAssemblyName))
            return FallbackScan(logger, "QaaS contract assembly has no simple name.");

        if (dependencyContext is null)
            return FallbackScan(logger, "DependencyContext.Default is unavailable.");

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
            return FallbackScan(logger, "Reverse-dependency walk threw.");
        }

        if (closure.Count == 0)
            return FallbackScan(logger, $"Contract assembly {contractAssemblyName} not present in dependency manifest.");

        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        AddAssembly(assemblies, Assembly.GetEntryAssembly());

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            AddAssembly(assemblies, loaded);

        foreach (var simpleName in closure)
        {
            if (assemblies.Values.Any(asm =>
                    string.Equals(asm.GetName().Name, simpleName, StringComparison.Ordinal)))
                continue;

            try
            {
                AddAssembly(assemblies, Assembly.Load(new AssemblyName(simpleName)));
            }
            catch (Exception exception)
            {
                logger.LogDebug(
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

        return assemblies.Values.ToArray();
    }

    private static IReadOnlySet<string> ComputeReverseDependencyClosure(
        DependencyContext dependencyContext,
        string contractAssemblyName)
    {
        var reverseEdges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var anchorPresent = false;

        foreach (var library in dependencyContext.RuntimeLibraries)
        {
            if (string.Equals(library.Name, contractAssemblyName, StringComparison.OrdinalIgnoreCase))
                anchorPresent = true;

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

        if (!anchorPresent)
            return new HashSet<string>(StringComparer.Ordinal);

        var closure = new HashSet<string>(StringComparer.Ordinal) { contractAssemblyName };
        var queue = new Queue<string>();
        queue.Enqueue(contractAssemblyName);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!reverseEdges.TryGetValue(current, out var dependents))
                continue;

            foreach (var dependent in dependents)
            {
                if (closure.Add(dependent))
                    queue.Enqueue(dependent);
            }
        }

        return closure;
    }

    private static IReadOnlyList<Assembly> FallbackScan(ILogger logger, string reason)
    {
        logger.LogInformation("Falling back to bin-folder plugin scan: {Reason}", reason);

        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        AddAssembly(assemblies, Assembly.GetEntryAssembly());

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            AddAssembly(assemblies, loaded);

        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            return assemblies.Values.ToArray();

        foreach (var assemblyPath in Directory.GetFiles(baseDirectory, "*.dll"))
        {
            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
                if (assemblies.ContainsKey(assemblyName.FullName ?? assemblyName.Name!))
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

    private static void AddAssembly(IDictionary<string, Assembly> assemblies, Assembly? assembly)
    {
        if (assembly is null || assembly.IsDynamic)
            return;

        var key = assembly.FullName ?? assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(key) || assemblies.ContainsKey(key))
            return;

        assemblies[key] = assembly;
    }

    internal static void ResetCacheForTesting()
    {
        lock (CacheLock)
        {
            _cached = null;
        }
    }
}
