using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Backward-compatible runner shim for the public framework configuration update helpers.
/// </summary>
[Obsolete("Use QaaS.Framework.Configurations.ConfigurationUpdateExtensions instead.")]
public static class ConfigurationMutationExtensions
{
    /// <summary>
    /// Forwards typed configuration updates to <see cref="ConfigurationUpdateExtensions" />.
    /// </summary>
    public static TConfiguration UpdateConfiguration<TConfiguration>(
        this TConfiguration? currentConfiguration,
        TConfiguration incomingConfiguration)
        where TConfiguration : class
    {
        return ConfigurationUpdateExtensions.UpdateConfiguration(currentConfiguration, incomingConfiguration);
    }

    /// <summary>
    /// Forwards raw <see cref="IConfiguration" /> updates to <see cref="ConfigurationUpdateExtensions" />.
    /// </summary>
    public static IConfiguration UpdateConfiguration(
        this IConfiguration? currentConfiguration,
        object incomingConfiguration)
    {
        return ConfigurationUpdateExtensions.UpdateConfiguration(currentConfiguration, incomingConfiguration);
    }
}
