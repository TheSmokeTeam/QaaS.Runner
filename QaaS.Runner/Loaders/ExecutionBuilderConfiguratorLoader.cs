using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Loaders;

internal static class ExecutionBuilderConfiguratorLoader
{
    private static readonly ConditionalWeakTable<Assembly, Type[]> ConfiguratorTypes = new();

    public static IReadOnlyList<IExecutionBuilderConfigurator> Load(ILogger logger)
    {
        return Load(logger, Assembly.GetEntryAssembly(), GetCandidateAssemblies());
    }

    internal static IReadOnlyList<IExecutionBuilderConfigurator> Load(
        ILogger logger,
        Assembly? entryAssembly,
        IEnumerable<Assembly> candidateAssemblies)
    {
        return Load(
            logger,
            entryAssembly,
            GetPotentialConfiguratorAssemblies(candidateAssemblies).SelectMany(GetLoadableTypes));
    }

    private static IEnumerable<Assembly> GetPotentialConfiguratorAssemblies(
        IEnumerable<Assembly> candidates
    )
    {
        var assemblies = candidates.ToArray();
        var available = assemblies
            .Select(a => a.FullName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dependents = assemblies
            .SelectMany(
                a => a.GetReferencedAssemblies(),
                (assembly, reference) =>
                    (Reference: reference.FullName, Dependent: assembly.FullName!)
            )
            .ToLookup(
                edge => edge.Reference,
                edge => edge.Dependent,
                StringComparer.OrdinalIgnoreCase
            );

        // Keep unresolved references and custom load contexts, where other dependencies may be supplied later.
        var pending = new Queue<string>(
            dependents.Select(group => group.Key).Where(name => !available.Contains(name))
        );
        pending.Enqueue(typeof(IExecutionBuilderConfigurator).Assembly.FullName!);
        foreach (var assembly in assemblies)
            if (
                AssemblyLoadContext.GetLoadContext(assembly) is { } context
                && context != AssemblyLoadContext.Default
            )
                pending.Enqueue(assembly.FullName!);

        // Include implementations inherited through intermediate base classes or interfaces.
        var possible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out var name))
            if (possible.Add(name))
                foreach (var dependent in dependents[name])
                    pending.Enqueue(dependent);

        return assemblies.Where(assembly => possible.Contains(assembly.FullName!));
    }

    internal static IReadOnlyList<IExecutionBuilderConfigurator> Load(
        ILogger logger,
        Assembly? entryAssembly,
        IEnumerable<Type> candidateTypes)
    {
        return candidateTypes
            .Where(type => IsConfiguratorCandidate(type, entryAssembly))
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => TryCreateConfigurator(type, entryAssembly, logger))
            .OfType<IExecutionBuilderConfigurator>()
            .ToArray();
    }

    private static IEnumerable<Assembly> GetCandidateAssemblies()
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);

        AddAssembly(assemblies, Assembly.GetEntryAssembly());

        foreach (var loadedAssembly in AppDomain.CurrentDomain.GetAssemblies())
            AddAssembly(assemblies, loadedAssembly);

        foreach (var assemblyPath in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll"))
        {
            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
                if (assemblies.ContainsKey(assemblyName.FullName ?? assemblyName.Name!))
                    continue;

                AddAssembly(assemblies, Assembly.LoadFrom(assemblyPath));
            }
            catch
            {
                // Ignore broken or unloadable binaries while scanning for configurators.
            }
        }

        return assemblies.Values;
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

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return ConfiguratorTypes.GetValue(
                assembly,
                static candidate =>
                    Array.FindAll(
                        candidate.GetTypes(),
                        typeof(IExecutionBuilderConfigurator).IsAssignableFrom
                    )
            );
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    private static bool IsConfiguratorCandidate(Type configuratorType, Assembly? entryAssembly)
    {
        if (!typeof(IExecutionBuilderConfigurator).IsAssignableFrom(configuratorType) ||
            configuratorType is { IsAbstract: true, IsInterface: true } ||
            configuratorType.ContainsGenericParameters)
        {
            return false;
        }

        if (configuratorType.Assembly == entryAssembly)
        {
            return configuratorType.IsPublic ||
                   configuratorType.IsNotPublic ||
                   configuratorType.IsNestedPublic ||
                   configuratorType.IsNestedAssembly ||
                   configuratorType.IsNestedFamORAssem ||
                   configuratorType.IsNestedFamANDAssem;
        }

        return configuratorType.IsPublic || configuratorType.IsNestedPublic;
    }

    private static IExecutionBuilderConfigurator? TryCreateConfigurator(
        Type configuratorType,
        Assembly? entryAssembly,
        ILogger logger)
    {
        var allowNonPublicConstructor = configuratorType.Assembly == entryAssembly;

        try
        {
            return (IExecutionBuilderConfigurator)(Activator.CreateInstance(
                                                       configuratorType,
                                                       allowNonPublicConstructor) ??
                                                   throw new InvalidOperationException(
                                                       $"Could not create runner execution configurator '{configuratorType.FullName}'."));
        }
        catch (Exception exception)
        {
            if (configuratorType.Assembly == entryAssembly)
            {
                logger.LogError(exception,
                    "Failed to create runner execution configurator {ConfiguratorType}",
                    configuratorType.FullName);
                throw;
            }

            logger.LogWarning(exception,
                "Skipping runner execution configurator {ConfiguratorType} because it could not be created.",
                configuratorType.FullName);
            return null;
        }
    }
}
