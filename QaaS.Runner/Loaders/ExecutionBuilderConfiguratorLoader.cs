using System.Reflection;
using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Loaders;

internal static class ExecutionBuilderConfiguratorLoader
{
    public static IReadOnlyList<IExecutionBuilderConfigurator> Load(ILogger logger)
    {
        return Load(
            logger,
            Assembly.GetEntryAssembly(),
            PluginAssemblyDiscovery.GetCandidateAssemblies(logger));
    }

    internal static IReadOnlyList<IExecutionBuilderConfigurator> Load(
        ILogger logger,
        Assembly? entryAssembly,
        IEnumerable<Assembly> candidateAssemblies)
    {
        return Load(
            logger,
            entryAssembly,
            candidateAssemblies.SelectMany(assembly => GetLoadableTypes(assembly, logger)));
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

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, ILogger logger)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            logger.LogDebug(
                exception,
                "Partially loaded assembly {AssemblyFullName} while scanning for configurators; continuing with the types that did load.",
                assembly.FullName);
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
