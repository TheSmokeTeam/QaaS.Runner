using System.Reflection;
using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Loaders;

internal static class ExecutionBuilderConfiguratorLoader
{
    public static IReadOnlyList<IExecutionBuilderConfigurator> Load(ILogger logger)
    {
        return GetCandidateAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(type => typeof(IExecutionBuilderConfigurator).IsAssignableFrom(type) &&
                           type is { IsAbstract: false, IsInterface: false } &&
                           (type.IsPublic || type.IsNestedPublic))
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => CreateConfigurator(type, logger))
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
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    private static IExecutionBuilderConfigurator CreateConfigurator(Type configuratorType, ILogger logger)
    {
        try
        {
            return (IExecutionBuilderConfigurator)(Activator.CreateInstance(configuratorType) ??
                                                   throw new InvalidOperationException(
                                                       $"Could not create runner execution configurator '{configuratorType.FullName}'."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Failed to create runner execution configurator {ConfiguratorType}",
                configuratorType.FullName);
            throw;
        }
    }
}
