using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations.ConfigurationBindingUtils;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Centralizes runner configuration mutation semantics so builders can apply partial updates consistently.
/// </summary>
public static class ConfigurationMutationExtensions
{
    /// <summary>
    /// Merges a protocol configuration into the current one when both share a compatible runtime type.
    /// When no current configuration exists, the incoming configuration becomes the current value.
    /// </summary>
    public static TConfiguration UpdateConfiguration<TConfiguration>(
        this TConfiguration? currentConfiguration,
        TConfiguration incomingConfiguration)
        where TConfiguration : class
    {
        ArgumentNullException.ThrowIfNull(incomingConfiguration);

        return currentConfiguration?.MergeConfiguration(incomingConfiguration) as TConfiguration ??
               incomingConfiguration;
    }

    /// <summary>
    /// Applies an object-shaped configuration update onto an <see cref="IConfiguration"/> tree.
    /// This is used by hook builders whose configuration is stored as raw key-value configuration.
    /// </summary>
    public static IConfiguration UpdateConfiguration(
        this IConfiguration? currentConfiguration,
        object incomingConfiguration)
    {
        ArgumentNullException.ThrowIfNull(incomingConfiguration);

        return (currentConfiguration ?? new ConfigurationBuilder().Build())
            .BindConfigurationObjectToIConfiguration(incomingConfiguration);
    }
}
